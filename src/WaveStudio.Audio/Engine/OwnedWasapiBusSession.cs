using System.Diagnostics;

namespace WaveStudio.Audio.Engine;

internal readonly record struct WasapiBusCaptureRoute(MixSource Source, WasapiPcm NativeFormat);
internal readonly record struct WasapiBusRenderRoute(TransportEndpoint Endpoint, MixBus Bus, WasapiPcm NativeFormat);

internal sealed record OwnedWasapiBusResult(long Generation, int OwnerThreadId, long BlocksProcessed,
    bool DurationReached, bool Canceled, bool ResourcesRetained, Exception? Failure,
    IReadOnlyList<Exception> CleanupFailures, OwnedBusClockFailure ClockFailure, NativeClockReason NativeClockFailure);

// Somente a MTA consulta e registra o prazo de uma geração já iniciada.
internal sealed class OwnedBusDeliveryDeadline(TimeSpan duration)
{
    private readonly long started = Stopwatch.GetTimestamp();
    public TimeSpan Remaining => duration - Stopwatch.GetElapsedTime(started);
    public bool Reached { get; private set; }

    public bool HasExpired()
    {
        if (Remaining <= TimeSpan.Zero) Reached = true;
        return Reached;
    }
}

// Entrada explícita para uma geração. A MTA conserva todos os recursos WASAPI desde a preparação até a parada.
internal static class OwnedWasapiBusSession
{
    private const uint MaximumBufferFrames = 4096;
    private static readonly TimeSpan BlockInterval = TimeSpan.FromMilliseconds(BusMixSession.BlockMilliseconds);

    public static Task<OwnedWasapiBusResult> RunAsync(long generation, PcmFloatFormat format,
        ReadOnlySpan<WasapiBusCaptureRoute> captures, ReadOnlySpan<WasapiBusRenderRoute> renders,
        TimeSpan maximumDuration, CancellationToken cancellation = default) =>
        RunAsync(generation, format, captures, renders, maximumDuration, TransportActivityGuard.Shared, cancellation,
            NativeWasapiPreparationDevice.OpenNonPersistent);

    internal static Task<OwnedWasapiBusResult> RunAsync(long generation, PcmFloatFormat format,
        ReadOnlySpan<WasapiBusCaptureRoute> captures, ReadOnlySpan<WasapiBusRenderRoute> renders,
        TimeSpan maximumDuration, TransportActivityGuard guard, CancellationToken cancellation,
        Func<TransportEndpoint, IWasapiPreparationDevice> open, Func<long>? readReferenceTime = null)
    {
        ValidateRoutes(generation, format, captures, renders);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumDuration, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(open);

        var captureRoutes = captures.ToArray();
        var renderRoutes = renders.ToArray();
        var completion = new TaskCompletionSource<OwnedWasapiBusResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                var result = Run(generation, format, captureRoutes, renderRoutes, maximumDuration, guard, cancellation,
                    open, readReferenceTime, out bool nativeResourcesRetained);
                completion.SetResult(result);
                // A quarentena conserva a MTA e os objetos nativos. Não há nova tentativa implícita de Stop ou de ReleaseBuffer.
                if (nativeResourcesRetained) Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true, Name = "WaveStudio WASAPI bus owner" };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        return completion.Task;
    }

    private static OwnedWasapiBusResult Run(long generation, PcmFloatFormat format, WasapiBusCaptureRoute[] captureRoutes,
        WasapiBusRenderRoute[] renderRoutes, TimeSpan maximumDuration, TransportActivityGuard guard, CancellationToken cancellation,
        Func<TransportEndpoint, IWasapiPreparationDevice> open, Func<long>? readReferenceTime, out bool nativeResourcesRetained)
    {
        var sources = Array.ConvertAll(captureRoutes, static route => route.Source);
        using var bridge = new BoundedBusPcmTransport(generation, format, sources);
        var captureStreams = Array.ConvertAll(captureRoutes, static r => new Stream(r.Source.Endpoint, r.NativeFormat));
        var renderStreams = Array.ConvertAll(renderRoutes, static r => new Stream(r.Endpoint, r.NativeFormat));
        Stream[] startOrder = [.. renderStreams, .. captureStreams];
        TransportActivityGuard.OwnedBusLease? lease = null;
        OwnedBusScheduler? scheduler = null;
        Exception? failure = null;
        var cleanupFailures = new List<Exception>();
        bool canceled = false, durationReached = false, retained = false;
        long processed = 0;

        try
        {
            cancellation.ThrowIfCancellationRequested();
            var stopSignal = cancellation.WaitHandle;
            lease = guard.TryAcquireOwnedBus(generation)
                ?? throw new InvalidOperationException("Atividade ocupada ou geração WASAPI já utilizada.");
            foreach (var stream in startOrder) stream.Prepare(open, cancellation);

            var captures = new PreparedBusCaptureSlot[captureStreams.Length];
            var renders = new PreparedBusRenderSlot[renderStreams.Length];
            for (int i = 0; i < captures.Length; i++)
            {
                var source = sources[i];
                var client = captureStreams[i].Client;
                captures[i] = new(source, client, new(bridge, source.Endpoint, source.Format, captureRoutes[i].NativeFormat));
            }
            for (int i = 0; i < renders.Length; i++)
            {
                var route = renderRoutes[i];
                renders[i] = new(route.Endpoint, route.Bus, renderStreams[i].Client, new(bridge, format, route.NativeFormat));
            }
            scheduler = OwnedBusScheduler.CreateForLifecycle(generation, format, bridge, captures, renders, guard,
                lease, cancellation, readReferenceTime);

            for (int i = 0; i < renders.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (renderStreams[i].Client.ReadPadding() != 0)
                    throw new InvalidOperationException("O buffer de render precisa estar vazio antes do preenchimento inicial.");
                scheduler.PrimeOutputSilence(i, renderStreams[i].Capacity);
            }
            // Revalida a topologia inteira antes do primeiro Start e sempre inicia destinos antes das fontes.
            foreach (var stream in startOrder) stream.Revalidate(cancellation);
            foreach (var stream in startOrder) stream.Start(cancellation);

            var deadline = new OwnedBusDeliveryDeadline(maximumDuration);
            long nextBlock = 0;
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                TimeSpan remaining = deadline.Remaining;
                if (remaining <= TimeSpan.Zero) break;

                // O trecho final de duração inferior a um bloco só espera. O arredondamento evita uma espera de zero milissegundos.
                bool finalWait = remaining <= BlockInterval;
                int waitMilliseconds = finalWait ? (int)Math.Ceiling(remaining.TotalMilliseconds) : BusMixSession.BlockMilliseconds;
                stopSignal.WaitOne(waitMilliseconds);
                cancellation.ThrowIfCancellationRequested();
                if (finalWait || deadline.HasExpired()) continue;

                // Um passo por espera. Se o owner atrasar, não tenta recuperar o atraso em rajada.
                if (!scheduler.Tick(nextBlock++, deadline))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (deadline.Reached) break;
                    throw new InvalidOperationException("O scheduler recusou o bloco WASAPI da geração ativa.");
                }
                if (scheduler.ClockAdmitted) processed++;
            }
            durationReached = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { canceled = true; }
        catch (Exception error)
        {
            failure = error;
            retained = error is WasapiBusBindingCleanupException;
        }
        finally
        {
            scheduler?.Quiesce();
            bridge.Dispose();
            if (lease is not null)
            {
                // Tenta parar inclusive o cliente cujo Start retornou falha. Uma falha não interrompe os demais Stops.
                for (int i = startOrder.Length - 1; i >= 0; i--)
                    retained |= !startOrder[i].TryStop(cleanupFailures);

                if (!retained)
                {
                    try { scheduler?.Dispose(); }
                    catch (Exception error) { cleanupFailures.Add(error); retained = true; }
                    retained |= scheduler?.ResourcesRetained == true;
                }

                if (!retained)
                {
                    for (int i = startOrder.Length - 1; i >= 0; i--)
                        retained |= !startOrder[i].TryRelease(cleanupFailures);
                }

                if (retained) WasapiResourceScope.Retain((lease, scheduler, startOrder));
                else lease.Dispose();
            }
        }

        nativeResourcesRetained = retained && startOrder.Any(static stream => stream.HasNativeDevice);
        return new(generation, Environment.CurrentManagedThreadId, processed, durationReached, canceled, retained,
            failure, cleanupFailures.AsReadOnly(), scheduler?.ClockFailure ?? OwnedBusClockFailure.None,
            scheduler?.NativeClockFailure ?? NativeClockReason.None);
    }

    private static void ValidateRoutes(long generation, PcmFloatFormat format, ReadOnlySpan<WasapiBusCaptureRoute> captures,
        ReadOnlySpan<WasapiBusRenderRoute> renders)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        ArgumentNullException.ThrowIfNull(format);
        if (captures.Length is < 1 or > BusMixSession.MaximumSources || renders.Length is < 1 or > 2)
            throw new ArgumentException("O ciclo WASAPI exige fontes e destinos explícitos.");
        foreach (var capture in captures)
        {
            ArgumentNullException.ThrowIfNull(capture.Source);
            Validate(capture.Source.Endpoint, EndpointDirection.Capture, capture.NativeFormat, capture.Source.Format);
        }
        for (int i = 0; i < renders.Length; i++)
        {
            var render = renders[i];
            Validate(render.Endpoint, EndpointDirection.Playback, render.NativeFormat, format);
            if (!Enum.IsDefined(render.Bus) || (i > 0 && (render.Bus == renders[0].Bus || render.Endpoint == renders[0].Endpoint)))
                throw new ArgumentException("Cada render precisa de barramento e endpoint exclusivos.", nameof(renders));
        }

        void Validate(TransportEndpoint endpoint, EndpointDirection direction, WasapiPcm native, PcmFloatFormat expected)
        {
            endpoint.Validate();
            ArgumentNullException.ThrowIfNull(native);
            native.Validate();
            if (endpoint.Generation != generation || endpoint.Direction != direction || native.FloatFormat != expected)
                throw new ArgumentException("A rota WASAPI precisa coincidir com a geração, a direção e o formato declarados.");
        }
    }

    private sealed class Stream(TransportEndpoint endpoint, WasapiPcm expected)
    {
        private readonly WasapiResourceScope resources = new();
        private IWasapiPreparationDevice? device;
        private bool startAttempted;
        internal uint Capacity { get; private set; }
        internal IWasapiBoundStreamClient Client { get; private set; } = null!;
        internal bool HasNativeDevice => device is NativeWasapiPreparationDevice || Client is IWasapiNativeBoundStreamClient;

        internal void Prepare(Func<TransportEndpoint, IWasapiPreparationDevice> open, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            device = resources.Add(open(endpoint) ?? throw new InvalidOperationException("Dispositivo WASAPI ausente."));
            device.ValidateIdentity(endpoint);
            cancellation.ThrowIfCancellationRequested();
            var prepared = resources.Add(device.Activate() ?? throw new InvalidOperationException("Cliente WASAPI ausente."));
            if (prepared is not IWasapiBoundStreamClient client || client.Endpoint != endpoint)
                throw new InvalidOperationException("Cliente WASAPI não corresponde ao endpoint solicitado.");
            Client = client;
            cancellation.ThrowIfCancellationRequested();
            if (client.ReadMixFormat() != expected)
                throw new InvalidOperationException("Formato WASAPI divergiu do formato autorizado.");
            client.RequireExactSupport();
            (long period, long minimum) = client.ReadPeriods();
            if (period <= 0 || minimum <= 0) throw new InvalidOperationException("Período WASAPI inválido.");
            device.ValidateIdentity(endpoint);
            cancellation.ThrowIfCancellationRequested();
            client.Initialize();
            cancellation.ThrowIfCancellationRequested();
            (uint capacity, long latency) = client.ReadBufferInfo();
            if (capacity < (uint)(expected.SampleRate / 100) || capacity > MaximumBufferFrames || latency < 0)
                throw new InvalidOperationException("Capacidade ou latência WASAPI fora do orçamento dos blocos.");
            Capacity = capacity;
        }

        internal void Revalidate(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            device!.ValidateIdentity(endpoint);
            Client.RevalidateFormat(expected);
        }

        internal void Start(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            startAttempted = true;
            WasapiCallException.RequireSuccess(Client.Start(), "IAudioClient.Start");
        }

        internal bool TryStop(List<Exception> cleanupFailures)
        {
            if (!startAttempted) return true;
            try
            {
                int result = Client.Stop();
                if (result is not (0 or 1)) throw new WasapiCallException("IAudioClient.Stop", result);
                return true;
            }
            catch (Exception error)
            {
                cleanupFailures.Add(error);
                return false;
            }
        }

        internal bool TryRelease(List<Exception> cleanupFailures)
        {
            resources.Dispose();
            if (resources.Released) return true;
            cleanupFailures.Add(new InvalidOperationException($"Liberação do cliente/dispositivo WASAPI incerta: {resources.CleanupError}."));
            return false;
        }
    }
}
