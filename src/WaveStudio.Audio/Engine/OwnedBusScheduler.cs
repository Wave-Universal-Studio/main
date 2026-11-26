using System.Runtime.ExceptionServices;
using WaveStudio.Audio;

namespace WaveStudio.Audio.Engine;

// Liga os owners já abertos a partir de um corte QPC com uma leitura por endpoint.
// Cada passo ocorre na MTA proprietária, sem abrir ou iniciar dispositivos.
internal readonly record struct OwnedBusCaptureSlot(MixSource Source, WasapiBusCaptureOwnerStep Owner);
internal readonly record struct OwnedBusRenderSlot(TransportEndpoint Endpoint, MixBus Bus, WasapiBusRenderOwnerStep Owner);

internal sealed partial class OwnedBusScheduler : IDisposable
{
    private readonly long generation;
    private readonly int ownerThread;
    private readonly CancellationToken cancellation;
    private readonly BusMixSession mixer;
    private readonly BoundedBusPcmTransport bridge;
    private readonly OwnedBusCaptureSlot[] captures;
    private readonly OwnedBusRenderSlot[] renders;
    private readonly float[] discard;
    private readonly bool personalBound, streamBound;
    private readonly IDisposable activityLease;
    private readonly OwnedBusClockAdmission clocks;
    private readonly NativeClockSample[]? nativeCut;
    private readonly Func<long> readReferenceTime;
    private long lastBlock = -1;
    private long lastTime;
    private bool stopped;

    public bool ResourcesRetained { get; private set; }
    public bool IsRunning => !stopped && !cancellation.IsCancellationRequested;
    public bool ClockAdmitted => IsRunning && clocks.Admitted;
    public OwnedBusClockFailure ClockFailure => clocks.Failure;
    public NativeClockReason NativeClockFailure => clocks.NativeReason;

    // Os owners são adquiridos previamente pelo chamador na mesma MTA.
    // Nenhum destino externo é ativado por esta sessão de integração.
    public OwnedBusScheduler(long generation, PcmFloatFormat format,
        ReadOnlySpan<MixSource> sources, BoundedBusPcmTransport bridge,
        ReadOnlySpan<OwnedBusCaptureSlot> captures, ReadOnlySpan<OwnedBusRenderSlot> renders,
        TransportActivityGuard guard, CancellationToken cancellation = default)
        : this(generation, format, sources, bridge, captures, renders, guard, cancellation, null, WasapiTime.Now100ns) { }

    private OwnedBusScheduler(long generation, PcmFloatFormat format,
        ReadOnlySpan<MixSource> sources, BoundedBusPcmTransport bridge,
        ReadOnlySpan<OwnedBusCaptureSlot> captures, ReadOnlySpan<OwnedBusRenderSlot> renders,
        TransportActivityGuard guard, CancellationToken cancellation, IDisposable? preparedLease, Func<long> readReferenceTime)
    {
        RequireMta();
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(guard);
        if (generation <= 0 || sources.Length == 0 || sources.Length > BusMixSession.MaximumSources ||
            captures.Length != sources.Length || renders.Length is < 1 or > 2)
            throw new ArgumentException("A sessão exige fontes e saídas explícitas com limites válidos.");
        cancellation.ThrowIfCancellationRequested();

        if (!bridge.MatchesOwnedBus(generation, format, sources))
            throw new ArgumentException("A ponte precisa pertencer à mesma geração e configuração do scheduler.", nameof(bridge));

        var sourceSnapshot = sources.ToArray();
        var captureSnapshot = captures.ToArray();
        var renderSnapshot = renders.ToArray();
        for (int i = 0; i < sourceSnapshot.Length; i++)
        {
            var source = sourceSnapshot[i];
            source.Endpoint.Validate();
            if (source.Endpoint.Generation != generation || source.Endpoint.Direction != EndpointDirection.Capture)
                throw new ArgumentException("Fonte de captura com identidade ou geração incorreta.", nameof(sources));
            if (captureSnapshot.Count(c => Equals(c.Source, source) && c.Owner is { } owner &&
                owner.IsAvailableOnOwner && owner.Endpoint == source.Endpoint && owner.Format == source.Format &&
                ReferenceEquals(owner.Bridge, bridge)) != 1)
                throw new ArgumentException("Cada fonte precisa de um owner único com formato idêntico.", nameof(captures));
            for (int j = i + 1; j < sourceSnapshot.Length; j++)
                if (source.Endpoint == sourceSnapshot[j].Endpoint ||
                    ReferenceEquals(captureSnapshot[i].Owner, captureSnapshot[j].Owner))
                    throw new ArgumentException("Fonte ou owner de captura duplicado.", nameof(sources));
        }
        for (int i = 0; i < renderSnapshot.Length; i++)
        {
            var output = renderSnapshot[i];
            output.Endpoint.Validate();
            if (output.Endpoint.Direction != EndpointDirection.Playback || output.Endpoint.Generation != generation ||
                !Enum.IsDefined(output.Bus) || output.Owner is null || output.Owner.Format != format ||
                !output.Owner.IsAvailableOnOwner || output.Owner.Endpoint != output.Endpoint ||
                !ReferenceEquals(output.Owner.Bridge, bridge))
                throw new ArgumentException("Saída de render inválida.", nameof(renders));
            for (int j = i + 1; j < renderSnapshot.Length; j++)
                if (output.Endpoint == renderSnapshot[j].Endpoint || output.Bus == renderSnapshot[j].Bus ||
                    ReferenceEquals(output.Owner, renderSnapshot[j].Owner))
                    throw new ArgumentException("Um destino não pode compartilhar owner, bus ou endpoint.", nameof(renders));
        }

        int attached = captureSnapshot.Count(slot => slot.Owner.HasNativeClock) + renderSnapshot.Count(slot => slot.Owner.HasNativeClock);
        if (preparedLease is null ? attached != 0 : attached != captureSnapshot.Length + renderSnapshot.Length)
            throw new ArgumentException("Os serviços nativos exigem aquisição conjunta pelo scheduler.");
        nativeCut = attached == 0 ? null : new NativeClockSample[attached];
        this.readReferenceTime = readReferenceTime;
        clocks = new(captureSnapshot, renderSnapshot, format.SampleRate);
        var lease = preparedLease ?? guard.TryAcquireOwnedBus(generation)
            ?? throw new InvalidOperationException("Atividade ocupada ou geração do barramento já utilizada.");
        try
        {
            ownerThread = Environment.CurrentManagedThreadId;
            this.generation = generation;
            this.cancellation = cancellation;
            this.bridge = bridge;
            this.captures = captureSnapshot;
            this.renders = renderSnapshot;
            personalBound = renderSnapshot.Any(slot => slot.Bus == MixBus.Personal);
            streamBound = renderSnapshot.Any(slot => slot.Bus == MixBus.Stream);
            discard = new float[checked(format.SampleRate / 100 * format.Channels)];
            mixer = new(bridge);
            mixer.Start(generation, format, sourceSnapshot, cancellation: cancellation);
            activityLease = lease;
        }
        catch
        {
            if (preparedLease is null) lease.Dispose();
            throw;
        }
    }

    // O corte segue a ordem dos slots de captura e depois dos de render.
    // A primeira leitura válida só estabelece as âncoras, sem adquirir buffers.
    public bool Tick(long block)
    {
        CheckOwner();
        if (stopped) return false;
        if (nativeCut is null || cancellation.IsCancellationRequested || !NextBlock(block))
        {
            Stop();
            return false;
        }

        try
        {
            int index = 0;
            foreach (var source in captures) nativeCut[index++] = source.Owner.ReadNativeClock();
            foreach (var output in renders) nativeCut[index++] = output.Owner.ReadNativeClock();
            // O tempo de referência é colhido depois de todos os GetPosition.
            return Advance(block, readReferenceTime(), nativeCut);
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public bool Tick(long block, long now100ns, ReadOnlySpan<NativeClockSample> samples)
    {
        CheckOwner();
        if (nativeCut is not null)
        {
            Stop();
            return false;
        }
        return Advance(block, now100ns, samples);
    }

    private bool NextBlock(long block) => block >= 0 && (lastBlock < 0 || block == lastBlock + 1);

    private bool Advance(long block, long now100ns, ReadOnlySpan<NativeClockSample> samples)
    {
        if (stopped) return false;
        if (cancellation.IsCancellationRequested || !NextBlock(block) || now100ns <= 0 ||
            (lastBlock >= 0 && (now100ns <= lastTime || now100ns - lastTime > OwnedBusClockAdmission.MaximumGap100ns)))
        {
            Stop();
            return false;
        }

        if (!clocks.Observe(samples, now100ns))
        {
            Stop();
            return false;
        }
        lastBlock = block;
        lastTime = now100ns;
        if (!ClockAdmitted)
        {
            if (IsRunning) return true;
            Stop();
            return false;
        }

        try
        {
            foreach (var source in captures)
                if (!source.Owner.TryCapture(block, now100ns, cancellation, OwnedBusClockAdmission.Block100ns))
                {
                    Stop();
                    return false;
                }

            if (!mixer.Process(generation, block))
            {
                Stop();
                return false;
            }

            // Um bus sem saída vinculada é consumido e descartado, jamais roteado por engano.
            if (!personalBound)
                bridge.TryReadOutput(MixBus.Personal, block, discard, out _);
            if (!streamBound)
                bridge.TryReadOutput(MixBus.Stream, block, discard, out _);

            foreach (var output in renders)
                if (!output.Owner.TryRender(output.Bus, block, cancellation))
                {
                    Stop();
                    return false;
                }

            if (IsRunning) return true;
            Stop();
            return false;
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        CheckOwner();
        if (stopped) return;
        stopped = true;
        mixer.Stop(generation);
        bridge.Dispose();

        ResourcesRetained = ReleaseOwners(captures, renders, out var failure);
        if (ResourcesRetained) WasapiResourceScope.Retain(activityLease);
        else activityLease.Dispose();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public void Dispose() => Stop();

    private void CheckOwner()
    {
        RequireMta();
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("O scheduler pertence à MTA original.");
    }

    private static void RequireMta()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA)
            throw new InvalidOperationException("O scheduler precisa de um owner MTA.");
    }
}
