using WaveStudio.Audio;

namespace WaveStudio.Audio.Engine;

// Liga os owners já abertos aos dois barramentos por um único relógio injetado.
// Não inicia dispositivos nem cria thread: cada passo ocorre na MTA proprietária.
// A sincronização física entre endpoints continua responsabilidade de um backend futuro.
internal readonly record struct OwnedBusCaptureSlot(MixSource Source, WasapiBusCaptureOwnerStep Owner);
internal readonly record struct OwnedBusRenderSlot(TransportEndpoint Endpoint, MixBus Bus, WasapiBusRenderOwnerStep Owner);

internal sealed class OwnedBusScheduler : IDisposable
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
    private long lastBlock = -1;
    private long lastTime;
    private bool stopped;

    public bool IsRunning => !stopped && !cancellation.IsCancellationRequested;

    // Os owners são adquiridos previamente pelo chamador na mesma MTA.
    // Nenhum destino externo é ativado por esta sessão de integração.
    public OwnedBusScheduler(long generation, PcmFloatFormat format,
        ReadOnlySpan<MixSource> sources, BoundedBusPcmTransport bridge,
        ReadOnlySpan<OwnedBusCaptureSlot> captures, ReadOnlySpan<OwnedBusRenderSlot> renders,
        TransportActivityGuard guard, CancellationToken cancellation = default)
    {
        RequireMta();
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(guard);
        if (generation <= 0 || sources.Length == 0 || sources.Length > BusMixSession.MaximumSources ||
            captures.Length != sources.Length || renders.Length is < 1 or > 2)
            throw new ArgumentException("A sessão exige fontes e saídas explícitas com limites válidos.");
        cancellation.ThrowIfCancellationRequested();

        var sourceSnapshot = sources.ToArray();
        var captureSnapshot = captures.ToArray();
        var renderSnapshot = renders.ToArray();
        for (int i = 0; i < sourceSnapshot.Length; i++)
        {
            var source = sourceSnapshot[i];
            source.Endpoint.Validate();
            if (source.Endpoint.Generation != generation || source.Endpoint.Direction != EndpointDirection.Capture)
                throw new ArgumentException("Fonte de captura com identidade ou geração incorreta.", nameof(sources));
            if (captureSnapshot.Count(c => Equals(c.Source, source) && c.Owner is not null) != 1)
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
                !Enum.IsDefined(output.Bus) || output.Owner is null)
                throw new ArgumentException("Saída de render inválida.", nameof(renders));
            for (int j = i + 1; j < renderSnapshot.Length; j++)
                if (output.Endpoint == renderSnapshot[j].Endpoint || output.Bus == renderSnapshot[j].Bus ||
                    ReferenceEquals(output.Owner, renderSnapshot[j].Owner))
                    throw new ArgumentException("Um destino não pode compartilhar owner, bus ou endpoint.", nameof(renders));
        }

        var lease = guard.TryAcquire(AudioActivity.IsolatedTransport)
            ?? throw new InvalidOperationException("Outra operação de áudio já ocupa a sessão.");
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
        catch { lease.Dispose(); throw; }
    }

    // Um passo = 10 ms. Não avança sobre blocos ausentes ou relógios incoerentes.
    // A entrega só prossegue quando TODAS as capturas e saídas confirmaram o bloco.
    public bool Tick(long block, long now100ns)
    {
        CheckOwner();
        if (stopped) return false;
        if (cancellation.IsCancellationRequested || block < 0 || now100ns <= 0 ||
            (lastBlock >= 0 && (block != lastBlock + 1 ||
             now100ns <= lastTime || now100ns - lastTime > 500_000)))
        {
            Stop();
            return false;
        }

        try
        {
            foreach (var source in captures)
                if (!source.Owner.TryCapture(block, now100ns, cancellation))
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

            lastBlock = block;
            lastTime = now100ns;
            return IsRunning;
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

        bool retained = false;
        try
        {
            foreach (var input in captures)
            {
                input.Owner.Dispose();
                retained |= input.Owner.ResourcesRetained;
            }
            foreach (var output in renders)
            {
                output.Owner.Dispose();
                retained |= output.Owner.ResourcesRetained;
            }
        }
        catch
        {
            // Sem liberação confirmada, nunca admitir nova prova nativa sobre os mesmos recursos.
            WasapiResourceScope.Retain(activityLease);
            throw;
        }

        if (retained) WasapiResourceScope.Retain(activityLease);
        else activityLease.Dispose();
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
