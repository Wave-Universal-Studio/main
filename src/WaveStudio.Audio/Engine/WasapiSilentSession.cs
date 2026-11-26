namespace WaveStudio.Audio.Engine;

// Os pontos de entrada B1 e B2 sempre liberam o render com SILENT.
internal sealed class WasapiSilentSession : IAsyncDisposable
{
    private readonly WasapiSessionCore core;

    public WasapiSilentSession(TransportRoute route, TransportActivityGuard? guard = null,
        Func<TransportEndpoint, IWasapiPreparationDevice>? open = null, TimeSpan? timeout = null,
        bool simulateCaptureTiming = false, bool observeCheckpoints = false,
        WasapiDriftSimulation? simulation = null, bool nativeDriftDiagnostic = false) =>
        core = new(route, guard, open, timeout, simulateCaptureTiming, observeCheckpoints, simulation, nativeDriftDiagnostic);

    public Task<WasapiSilentSnapshot> RunAsync(TimeSpan duration, CancellationToken token = default) => core.RunAsync(duration, token);
    internal WasapiCheckpointResult ReadCheckpoint(long generation, WasapiCheckpoint? baseline = null) => core.ReadCheckpoint(generation, baseline);
    internal bool DriftRunning => core.DriftRunning;
    internal WasapiDriftSample StepDrift() => core.StepDrift();
    internal WasapiDriftReport? ReadDriftReport() => core.ReadDriftReport();
    public void Invalidate(long generation) => core.Invalidate(generation);
    public void RequestStop() => core.RequestStop();
    public ValueTask DisposeAsync() => core.DisposeAsync();
}

// Núcleo de uso único e dois proprietários COM. As fachadas restringem os modos que cada chamador pode usar.
internal sealed class WasapiSessionCore : IAsyncDisposable
{
    private static readonly HashSet<WasapiSessionCore> live = [];
    private readonly TransportRoute route;
    private readonly Func<TransportEndpoint, IWasapiPreparationDevice> open;
    private readonly TransportActivityGuard guard;
    private readonly TimeSpan timeout;
    private readonly bool verifyCaptureTiming, observeCheckpoints, nativeDriftDiagnostic;
    private readonly WasapiDriftSimulation? simulation;
    private readonly WasapiNominalControl? nominal;
    private readonly object driftGate = new();
    private WasapiDriftControl? drift;
    private WasapiDriftCommand? command;
    private readonly object control = new(); // O processamento de amostras nunca adquire este lock.
    private readonly SemaphoreSlim finalizing = new(1);
    private readonly ManualResetEventSlim stop = new();
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource nominalFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long observationDeadline;
    private readonly DeliveryTicket ticket;
    private readonly Owner capture, playback;
    private IDisposable? lease;
    private CancellationTokenRegistration cancellation, operatorCancellation;
    private WasapiSilentPath? path;
    private WasapiCheckpointLedger? checkpoints;
    private Task<WasapiSilentSnapshot>? run;
    private bool completed;
    private string? failure;
    private int? hresult;
    private int faultWritten;
    private long observation100ns, observationEnd100ns;
    private bool durationReached;
    private long monitorClippedSamples;

    public WasapiSessionCore(TransportRoute route, TransportActivityGuard? guard = null,
        Func<TransportEndpoint, IWasapiPreparationDevice>? open = null, TimeSpan? timeout = null,
        bool simulateCaptureTiming = false, bool observeCheckpoints = false,
        WasapiDriftSimulation? simulation = null, bool nativeDriftDiagnostic = false, WasapiNominalControl? nominal = null)
    {
        route.Validate();
        if (nominal?.Encoder is not null)
        {
            bool simulatedRoute = open is not null && simulation is not null &&
                route.Capture.Evidence == FormatEvidence.SimulatedExact && route.Playback.Evidence == FormatEvidence.SimulatedExact;
            if (!simulatedRoute && (open is not null || nominal.Audible is null))
                throw new ArgumentException("Render PCM exige backend simulado ou consentimento da prova audível nativa.", nameof(open));
        }
        this.nominal = nominal;
        if ((observeCheckpoints || simulation is not null) && open is null)
            throw new ArgumentException("A instrumentação simulada exige backend injetado.", nameof(open));
        if (nativeDriftDiagnostic && open is not null)
            throw new ArgumentException("Use o modo de simulação com backend injetado.", nameof(open));
        this.observeCheckpoints = observeCheckpoints || simulation is not null || nativeDriftDiagnostic || nominal is not null;
        this.simulation = simulation;
        this.nativeDriftDiagnostic = nativeDriftDiagnostic;
        if (open is null && (route.Capture.Evidence != FormatEvidence.ExactCapability ||
            route.Playback.Evidence != FormatEvidence.ExactCapability))
            throw new ArgumentException("A prova nativa requer formatos consultados no Windows.", nameof(route));
        if (nominal?.Audible is { } consent)
        {
            if (nominal.Encoder is null) throw new ArgumentException("A prova audível requer codificador.", nameof(nominal));
            consent.Consume(route);
        }
        if (route.Capture.Endpoint.Generation == long.MaxValue) throw new ArgumentException("Geração sem espaço para limpeza.");
        this.route = route; this.guard = guard ?? TransportActivityGuard.Shared;
        verifyCaptureTiming = open is null || simulateCaptureTiming || simulation is not null || nominal is not null;
        this.open = open ?? NativeWasapiPreparationDevice.Open; this.timeout = timeout ?? TimeSpan.FromSeconds(3);
        if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromSeconds(5)) throw new ArgumentOutOfRangeException(nameof(timeout));
        ticket = new(route); capture = new(this, route.Capture, true); playback = new(this, route.Playback, false);
    }
    public Task<WasapiSilentSnapshot> RunAsync(TimeSpan duration, CancellationToken token = default)
    {
        TimeSpan maximumDuration = nominal is not null ? WasapiNominalSession.MaximumDuration
            : nativeDriftDiagnostic ? WasapiDriftObservation.MaximumDuration
            : TimeSpan.FromSeconds(simulation is null ? 10 : 120);
        if (duration < TimeSpan.FromMilliseconds(50) || duration > maximumDuration) throw new ArgumentOutOfRangeException(nameof(duration));
        lock (control)
        {
            if (run is not null || stop.IsSet) throw new InvalidOperationException("Sessão de prova é de uso único.");
            token.ThrowIfCancellationRequested();
            nominal?.Audible?.StopToken.ThrowIfCancellationRequested();
            lease = guard.TryAcquire(AudioActivity.IsolatedTransport) ?? throw new InvalidOperationException("Outra operação de áudio está ativa.");
            lock (live) live.Add(this); // Mantém os proprietários COM e o lease referenciados mesmo se o chamador abandonar uma operação que excedeu o prazo.
            cancellation = token.Register(RequestStop);
            if (nominal?.Audible is { } consent) operatorCancellation = consent.StopToken.Register(RequestStop);
            try { capture.Launch(); playback.Launch(); }
            catch (Exception error) { Fail("OwnerLaunch", error); capture.NotLaunched(); playback.NotLaunched(); }
            return run = ControlAsync(duration);
        }
    }
    internal WasapiCheckpointResult ReadCheckpoint(long generation, WasapiCheckpoint? baseline = null)
    {
        if (!observeCheckpoints) return new(WasapiCheckpointStatus.Disabled);
        var result = Volatile.Read(ref checkpoints)?.Read(generation, baseline) ?? new(WasapiCheckpointStatus.Inactive);
        if (result.Status is WasapiCheckpointStatus.OwnerStale or WasapiCheckpointStatus.InvalidTime or
            WasapiCheckpointStatus.InvalidAccounting or WasapiCheckpointStatus.InvalidSequence or
            WasapiCheckpointStatus.Overflow or WasapiCheckpointStatus.OwnerFault)
            Fail("Checkpoint/" + result.Status);
        return result;
    }
    internal bool DriftRunning => ticket.IsValid && drift is not null;
    private long Now100ns() => simulation?.Now() ?? WasapiTime.Now100ns();
    private bool ObservationDeadlinePassed => (nominal is not null || nativeDriftDiagnostic) &&
        ticket.IsValid && Now100ns() > observationDeadline;

    internal WasapiDriftSample StepDrift()
    {
        lock (driftGate)
        {
            if (drift is not { } state) return default;
            long now = Now100ns();
            if (!ticket.IsValid || !state.IsDue(now)) return state.Last;
            long generation = route.Capture.Endpoint.Generation;
            var sample = state.Step(now, ReadCheckpoint(generation, state.Baseline));
            if (state.MustStop) Fail("Drift/" + sample.Reason);
            if (sample.RequestedPpm is { } ppm && ticket.IsValid)
            {
                Volatile.Write(ref command, new(generation, sample.Cut100ns, ppm));
                if (!ticket.IsValid) Volatile.Write(ref command, null);
            }
            return sample;
        }
    }
    internal WasapiDriftReport? ReadDriftReport()
    {
        lock (driftGate) return drift?.Snapshot();
    }
    internal bool NominalRunning => nominal is not null && ticket.IsValid;
    internal void SetFakeGain(float gain)
    {
        if (nominal is not { Encoder: { } encoder, Audible: null })
            throw new InvalidOperationException("O ajuste de ganho está disponível somente no fake de monitoramento.");
        encoder.SetGain(gain);
    }
    internal void StepNominal()
    {
        lock (driftGate)
        {
            if (nominal is null || !ticket.IsValid || checkpoints is not { } ledger) return;
            var cut = ReadCheckpoint(route.Capture.Endpoint.Generation, nominal.Baseline);
            var reason = nominal.Observe(ledger, cut, Now100ns());
            if (reason is not null) Fail(reason);
            else if (simulation is not null && Now100ns() >= observationDeadline && cut.Checkpoint is not null)
                nominalFinished.TrySetResult();
        }
    }
    internal WasapiNominalReport ReadNominalReport(WasapiSilentSnapshot result)
    {
        lock (driftGate)
        {
            var state = nominal ?? throw new InvalidOperationException("Sessão nominal ausente.");
            return state.Report(result, result.Completed && capture.HasNominalPackets(observationEnd100ns),
                result.Completed ? capture.ClockEvidenceUnavailable : 0, path?.FifoPrimingFrames ?? 0,
                playback.PreTicketRender, playback.WarmupRender, observationEnd100ns);
        }
    }
    internal WasapiAudibleOutput? ReadAudibleOutput(WasapiSilentSnapshot result) =>
        nominal?.Audible is not null && result.Completed ? playback.AudibleOutput(monitorClippedSamples) : null;

    internal ExternalRouteLevels? ReadExternalLevels(WasapiSilentSnapshot result) =>
        nominal?.ExternalRoute == true && result.Completed ? playback.ExternalLevels : null;

    public void RequestStop()
    {
        ticket.Invalidate(); // Invalidação irreversível antes de qualquer espera ou parada nativa.
        Volatile.Write(ref command, null);
        stop.Set(); stopped.TrySetResult();
    }
    public void Invalidate(long generation)
    {
        if (generation == route.Capture.Endpoint.Generation && !stop.IsSet) Fail("SelectionInvalidated");
    }
    private void Fail(string stage, Exception? error = null)
    {
        if (Interlocked.CompareExchange(ref faultWritten, 1, 0) == 0)
        {
            failure = error is WasapiCallException call ? stage + "/" + call.Stage : stage;
            hresult = error is WasapiCallException result ? result.Result : error is System.Runtime.InteropServices.COMException ? error.HResult : null;
        }
        RequestStop();
    }
    private async Task<WasapiSilentSnapshot> ControlAsync(TimeSpan duration)
    {
        try
        {
            await Task.WhenAll(capture.Ready.Task, playback.Ready.Task).WaitAsync(timeout);
            if (!stop.IsSet)
            {
                if (nominal?.ExternalRoute == true && (capture.Period > 200_000 || playback.Period > 200_000 ||
                    capture.Capacity > 2048 || playback.Capacity > 2048)) throw new NotSupportedException("Orçamento de blocos externo.");
                path = new(route, capture.Capacity, playback.Capacity, capture.Period, playback.Period);
                nominal?.Configure(path.CapacityFrames, route.Capture.Mix!.SampleRate, route.Playback.Mix!.SampleRate,
                    capture.Capacity, playback.Capacity);
                if (observeCheckpoints) Volatile.Write(ref checkpoints, new(ticket, path.CapacityFrames, Now100ns));
                playback.Go.Set(); await playback.Started.Task.WaitAsync(timeout);
            }
            if (!stop.IsSet) { capture.Go.Set(); await capture.Started.Task.WaitAsync(timeout); }
            if (!stop.IsSet)
            {
                long begin = Now100ns();
                if (nominal is null && (simulation is not null || nativeDriftDiagnostic))
                {
                    int inputRate = route.Capture.Mix!.SampleRate, outputRate = route.Playback.Mix!.SampleRate;
                    var uncertainty = simulation?.Uncertainty ?? new WasapiTimingUncertainty(
                        Math.Ceiling(inputRate * capture.Period / 10_000_000d),
                        Math.Ceiling(outputRate * playback.Period / 10_000_000d),
                        Math.Ceiling(inputRate * capture.Period / 10_000_000d));
                    lock (driftGate) drift = new(inputRate, outputRate, path!.CapacityFrames,
                        simulation?.ApplyCorrection == true, uncertainty, begin, observeLongDuration: nativeDriftDiagnostic);
                }
                nominal?.Start(begin);
                if (nominal is not null || nativeDriftDiagnostic) observationDeadline = checked(begin + duration.Ticks);
                ticket.Enable();
                using var delayCancellation = new CancellationTokenSource();
                var limit = Task.Delay(duration, delayCancellation.Token);
                if (nativeDriftDiagnostic)
                    while (!limit.IsCompleted && !stop.IsSet)
                    {
                        StepDrift();
                        await Task.WhenAny(limit, stopped.Task, Task.Delay(55, delayCancellation.Token));
                    }
                if (nominal is not null && simulation is null)
                    while (!limit.IsCompleted && !stop.IsSet)
                    {
                        StepNominal();
                        await Task.WhenAny(limit, stopped.Task, Task.Delay(23, delayCancellation.Token));
                    }
                if (nominal is not null && simulation is not null)
                    durationReached = await Task.WhenAny(limit, stopped.Task, nominalFinished.Task) == nominalFinished.Task && !stop.IsSet;
                else
                    durationReached = await Task.WhenAny(limit, stopped.Task) == limit && !stop.IsSet;
                observationEnd100ns = Now100ns();
                observation100ns = observationEnd100ns - begin;
                delayCancellation.Cancel();
            }
        }
        catch (Exception error) { Fail("Control", error); }
        RequestStop(); return await FinishAsync(retryStop: false);
    }
    private async Task<WasapiSilentSnapshot> FinishAsync(bool retryStop)
    {
        await finalizing.WaitAsync();
        try
        {
        if (completed) return Snapshot();
        if (retryStop) { capture.Retry.Set(); playback.Retry.Set(); }
        try { await Task.WhenAll(capture.Done.Task, playback.Done.Task).WaitAsync(timeout); }
        catch (TimeoutException) { return Snapshot(); } // Preserva proprietários, COM, memória de trabalho e lease. Nunca interrompe uma chamada nativa.
        if (!capture.Join() || !playback.Join()) return Snapshot();
            if (nominal?.Audible is not null) monitorClippedSamples = path?.ClippedSamples ?? 0;
            path?.Dispose();
            cancellation.Dispose();
            operatorCancellation.Dispose();
            lease?.Dispose();
            lease = null;
            completed = true;
            lock (live) live.Remove(this);
            return Snapshot();
        }
        finally { finalizing.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        RequestStop();
        Task<WasapiSilentSnapshot>? pending; lock (control) pending = run;
        if (pending is null) return;
        await pending;
        var result = await FinishAsync(retryStop: true);
        if (result.ResourcesRetained) throw new InvalidOperationException("Encerramento nativo não confirmado; recursos e bloqueio preservados.");
    }
    private WasapiSilentSnapshot Snapshot() => new(capture.Snapshot(completed), playback.Snapshot(completed), completed, lease is not null,
        failure, hresult, path?.CapacityFrames ?? 0, path?.Produced ?? 0, path?.Read ?? 0, path?.Dropped ?? 0, path?.Underrun ?? 0,
        observation100ns, durationReached) { NominalMode = nominal is not null, PcmRenderSelected = nominal?.Encoder is not null };

    private sealed class Owner
    {
        private readonly WasapiSessionCore session;
        private readonly WasapiEndpointInfo info;
        private readonly bool input;
        private readonly Thread thread;
        private readonly WasapiResourceScope resources = new();
        private IWasapiPreparationDevice? device;
        private IWasapiStreamClient? client;
        private IWasapiPreparationClock? clock;
        private IWasapiCapturePackets? packets;
        private IWasapiRenderPackets? render;
        private bool launched, initialized, startAttempted, started, stopConfirmed, bufferInFlight, bufferUncertain;
        private int ownerId;
        public uint Capacity;
        public long Period;
        private long latency, acquired, released, acquiredFrames, releasedFrames, primed, preTicketRenderFrames, warmupRenderFrames;
        public long PreTicketRender => Interlocked.Read(ref preTicketRenderFrames);
        public long WarmupRender => Interlocked.Read(ref warmupRenderFrames);
        public long ClockEvidenceUnavailable => captureTiming?.UnavailableReadings ?? 0;
        public bool HasNominalPackets(long now) => captureTiming?.HasNominalPackets(now) == true;
        private long clockEpoch, appliedCommand100ns, appliedAt100ns;
        private long pcmFrames, nonZeroFrames;
        private float peakAmplitude, peakGain, bypassPeak, effectPeak;
        private long bypassNonZeroFrames, effectNonZeroFrames;
        public ExternalRouteLevels ExternalLevels => new(bypassNonZeroFrames, bypassPeak, effectNonZeroFrames, effectPeak);
        public WasapiAudibleOutput AudibleOutput(long clipped) => new(releasedFrames - pcmFrames, pcmFrames, nonZeroFrames, peakAmplitude, peakGain, clipped);
        private long acceptedPackets, acceptedFrames, startupDiscardedPackets, startupDiscardedFrames;
        private uint padding, flags, startupDiscardFlags;
        private ulong packetPosition, packetQpc, startupDiscardPosition, startupDiscardQpc;
        private string? error, cleanup;
        private WasapiRunningClock? motion;
        private WasapiCaptureTiming? captureTiming;
        private WasapiCapturePacketRejection? packetRejection;
        private WasapiClockReading? lastClock;
        private int? startResult, stopResult, acquireResult, releaseResult;
        private long nextClock, startTime, startRequested100ns;
        private NativeClockResult? clockStatus;
        private const long StartupDiscardWindow100ns = 5_000_000; // Limitado pelo prazo de frescor já aplicado aos pacotes.
        private const uint Discontinuity = (uint)TransportPacketFlags.Discontinuity;
        private const uint Silent = (uint)TransportPacketFlags.Silent;
        private const int BufferEmpty = 0x08890001;
        public readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Go = new(), Retry = new();
        public Owner(WasapiSessionCore session, WasapiEndpointInfo info, bool input)
        {
            this.session = session; this.info = info; this.input = input;
            thread = new Thread(Run) { IsBackground = true, Name = input ? "WaveStudio WASAPI capture owner" : "WaveStudio WASAPI silent render owner" };
            thread.SetApartmentState(ApartmentState.MTA);
        }
        public void Launch() { thread.Start(); launched = true; }
        public void NotLaunched() { if (!launched) { Ready.TrySetResult(); Started.TrySetResult(); Done.TrySetResult(); } }
        public bool Join() => !launched || thread.Join(TimeSpan.FromMilliseconds(200));
        private void Check() { if (session.stop.IsSet) throw new OperationCanceledException(); }
        private void Prepare()
        {
            Check(); device = resources.Add(session.open(info.Endpoint)); Check(); device.ValidateIdentity(info.Endpoint); Check();
            client = (IWasapiStreamClient)resources.Add(device.Activate()); Check();
            var mix = client.ReadMixFormat(); mix.Validate();
            if (mix != info.Mix) throw new InvalidOperationException("Formato divergente; sem fallback.");
            Check(); client.RequireExactSupport(); (Period, long minimum) = client.ReadPeriods();
            if (Period is <= 0 or > 1000000 || minimum <= 0) throw new NotSupportedException("Período fora do orçamento.");
            Check(); device.ValidateIdentity(info.Endpoint); Check(); client.Initialize(); initialized = true; Check();
            (Capacity, latency) = client.ReadBufferInfo();
            if (Capacity is 0 or > 4096 || latency < 0) throw new NotSupportedException("Capacidade/latência fora do orçamento.");
            if (input && session.verifyCaptureTiming) captureTiming = new(mix.SampleRate, Period, Capacity, limitedNominal: session.nominal is not null);
            padding = client.ReadPadding(); if (padding > Capacity) throw new InvalidOperationException("Padding inválido na preparação.");
            Check(); clock = resources.Add(client.OpenClock()); Check();
            if (input) packets = resources.Add(client.OpenCapture()); else render = resources.Add(client.OpenRender());
            device.ValidateIdentity(info.Endpoint); Check();
        }
        private void Run()
        {
            ownerId = Environment.CurrentManagedThreadId;
            try
            {
                Prepare(); Ready.TrySetResult();
                while (!Go.IsSet && !session.stop.Wait(2)) { }
                Check(); device!.ValidateIdentity(info.Endpoint); client!.RevalidateFormat(info.Mix!); Check();
                if (!input) { ProcessBoundary(prime: true); Check(); }
                startRequested100ns = session.Now100ns();
                startAttempted = true; startResult = client!.Start(); WasapiCallException.RequireSuccess(startResult.Value, "IAudioClient.Start"); started = true;
                startTime = session.Now100ns(); motion = new(info.Endpoint, info.Mix!.SampleRate, startTime, captureTiming); nextClock = startTime + 1000000;
                Started.TrySetResult(); Check();
                int pollMs = Math.Clamp((int)(Period / 20000), 1, 10);
                while (!session.stop.IsSet)
                {
                    if (session.simulation is { } simulation)
                    {
                        simulation.WaitForCycle(input, session.stop.WaitHandle);
                        if (session.stop.IsSet) break;
                    }
                    ProcessBoundary(prime: false);
                    if (session.drift is null && session.nominal is null) ObserveClockIfDue();
                    if (session.simulation is { } pacing) pacing.CycleCompleted(input);
                    else session.stop.Wait(pollMs);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception problem)
            {
                error = problem is WasapiCallException c ? c.Stage : problem.GetType().Name;
                string stage = session.nominal is not null && problem is InvalidOperationException &&
                    problem.Message.StartsWith("Nominal/", StringComparison.Ordinal) ? problem.Message : input ? "Capture" : "Render";
                session.Fail(stage, problem);
            }
            finally
            {
                Ready.TrySetResult(); Started.TrySetResult(); session.RequestStop();
                // Só repete Stop em uma tentativa explícita de descarte. Não repete uma liberação malsucedida sem confirmação.
                while (true)
                {
                    if (TryCleanup()) { Done.TrySetResult(); break; }
                    Retry.Wait(); Retry.Reset();
                }
            }
        }
        private void ProcessBoundary(bool prime)
        {
            var ledger = session.checkpoints;
            var journal = input ? ledger?.Capture : ledger?.Render;
            if (journal is null) { ProcessAudio(prime); return; }

            if (!prime && (session.ObservationDeadlinePassed || !CheckCommandAge())) return;
            var path = session.path!;
            long missingBefore = input ? path.Dropped : path.Underrun;
            journal.Begin();
            try
            {
                ProcessAudio(prime);
                if (!prime && (session.drift is not null || session.nominal is not null)) ObserveClockIfDue();
            }
            catch
            {
                // Uma operação sem confirmação de liberação não forma fronteira válida.
                session.ticket.Invalidate();
                journal.Abort();
                throw;
            }
            if (session.stop.IsSet) { journal.Abort(); return; }
            if (bufferInFlight || bufferUncertain)
            {
                journal.Abort();
                session.Fail("Checkpoint/OwnerFault");
                return;
            }

            if (Volatile.Read(ref session.command) is not null && (input ? path.Dropped : path.Underrun) != missingBefore)
            {
                session.Fail("Drift/FifoBoundary");
                journal.Abort();
                return;
            }
            var status = journal.Complete(new(acquired, released, acquiredFrames, releasedFrames,
                acceptedPackets, acceptedFrames, startupDiscardedPackets, startupDiscardedFrames,
                input ? path.Produced : path.Read, input ? path.Dropped : path.Underrun, primed, preTicketRenderFrames,
                warmupRenderFrames), session.drift is null && session.nominal is null ? default : DriftEvidence(path));
            if (status != WasapiCheckpointStatus.Available) session.Fail("Checkpoint/" + status);
        }
        private void ProcessAudio(bool prime)
        {
            if (!input) { FillRender(prime); return; }
            padding = client!.ReadPadding();
            if (padding > Capacity) throw new InvalidOperationException("Padding de captura inválido.");
            if (session.ticket.IsValid) DrainCapture();
        }
        private WasapiDriftOwner DriftEvidence(WasapiSilentPath path)
        {
            var point = lastClock is { } reading ? new WasapiDriftPoint(reading.Position, reading.Frequency, reading.Qpc100ns) : default;
            var packet = input && acceptedPackets > 0 ? new WasapiDriftPoint(packetPosition, (ulong)info.Mix!.SampleRate, packetQpc) : default;
            return new(point, clockEpoch, clockStatus, packet, captureTiming?.HasRateWindows == true,
                input ? path.FifoPrimingFrames : 0, input ? path.AppliedPpm : 0, appliedCommand100ns, appliedAt100ns);
        }
        private void ObserveClockIfDue()
        {
            if (!session.ticket.IsValid) return;
            long now = session.Now100ns();
            captureTiming?.CheckProgress(now, startTime);
            if (now >= nextClock) { ReadClock(); nextClock = now + 1_000_000; }
        }
        private void ReadClock()
        {
            lastClock = clock!.Read();
            long now = session.Now100ns();
            var result = motion!.Observe(lastClock.Value, now);
            clockStatus = result;
            if (session.drift is not null && result == NativeClockResult.Provisional) clockEpoch++;
            if (result == NativeClockResult.Stalled && lastClock.Value.Position == 0 && now - startTime <= 5000000) return;
            if (result == NativeClockResult.NominalEvidenceUnavailable && input && session.nominal is not null) return;
            if (result is not (NativeClockResult.Valid or NativeClockResult.Provisional))
                throw new InvalidOperationException("Clock inválido: " + result);
        }
        private unsafe void DrainCapture()
        {
            for (int count = 0; count < 32 && !session.stop.IsSet && session.ticket.IsValid; count++)
            {
                if (session.ObservationDeadlinePassed) return;
                uint nextFrames = packets!.NextPacketSize();
                if (nextFrames == 0) return;
                if (session.nominal is { } nominal && session.path!.ProducerQueued >= session.path.CapacityFrames - nominal.MarginFrames)
                    throw new InvalidOperationException("Nominal/FifoHigh");
                Check();
                var buffer = packets.GetBuffer();
                acquireResult = buffer.HResult;
                if (buffer.HResult == BufferEmpty) return; // AUDCLNT_S_BUFFER_EMPTY: nenhum pacote adquirido, nenhuma liberação.
                WasapiCallException.RequireSuccess(buffer.HResult, "IAudioCaptureClient.GetBuffer");
                bufferInFlight = true; acquired++; acquiredFrames += buffer.Frames; uint consumed = 0;
                bool discardStartup = (buffer.Flags & Discontinuity) != 0;
                try
                {
                    flags |= buffer.Flags; packetPosition = buffer.Position; packetQpc = buffer.Qpc100ns;
                    long now = session.Now100ns();
                    // BUFFER_EMPTY não chega a este ponto. Um primeiro pacote válido também esgota a exceção de uso único.
                    if (buffer.Frames != nextFrames)
                        throw RejectPacket(WasapiCapturePacketRejectionReason.SizeMismatch, nextFrames, buffer, now);
                    if (buffer.Frames > Capacity)
                        throw RejectPacket(WasapiCapturePacketRejectionReason.CapacityExceeded, nextFrames, buffer, now);
                    if ((buffer.Flags & ~(Silent | Discontinuity)) != 0)
                        throw RejectPacket(WasapiCapturePacketRejectionReason.InvalidFlags, nextFrames, buffer, now);
                    if (discardStartup && (acquired != 1 || now - startRequested100ns > StartupDiscardWindow100ns))
                        throw RejectPacket(WasapiCapturePacketRejectionReason.UnexpectedDiscontinuity, nextFrames, buffer, now);
                    if (buffer.Qpc100ns == 0)
                        throw RejectPacket(WasapiCapturePacketRejectionReason.MissingTimestamp, nextFrames, buffer, now);
                    if (buffer.Qpc100ns > (ulong)now)
                        throw RejectPacket(WasapiCapturePacketRejectionReason.FutureTimestamp, nextFrames, buffer, now);
                    if ((ulong)now - buffer.Qpc100ns > 5000000)
                        throw RejectPacket(WasapiCapturePacketRejectionReason.StaleTimestamp, nextFrames, buffer, now);
                    bool silent = (buffer.Flags & Silent) != 0;
                    if (!silent && buffer.Data == 0)
                        throw RejectPacket(WasapiCapturePacketRejectionReason.MissingData, nextFrames, buffer, now);
                    Check();
                    if (!discardStartup) captureTiming?.ValidatePacket(buffer.Position, buffer.Frames, buffer.Qpc100ns);
                    var bytes = silent ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>((void*)buffer.Data, checked((int)buffer.Frames * info.Mix!.BlockAlign));
                    if (discardStartup)
                    {
                        // ReleaseBuffer(0) faria o mesmo pacote reaparecer. Consuma o pacote inteiro
                        // antes de avançar, sem ler amostras quando SILENT e sem alimentar o FIR em nenhum dos casos.
                        if (!silent) session.path!.DiscardCapture(bytes, (int)buffer.Frames);
                    }
                    else session.path!.CopyCapture(bytes, (int)buffer.Frames, silent);
                    consumed = buffer.Frames;
                }
                finally
                {
                    int hr;
                    try { hr = packets.ReleaseBuffer(consumed); releaseResult = hr; }
                    catch { bufferUncertain = true; throw; }
                    if (hr != 0) { bufferUncertain = true; throw new WasapiCallException("IAudioCaptureClient.ReleaseBuffer", hr); }
                    bufferInFlight = false; released++; releasedFrames += consumed;
                }
                if (discardStartup)
                {
                    startupDiscardedPackets++;
                    startupDiscardedFrames += consumed;
                    startupDiscardFlags = buffer.Flags;
                    startupDiscardPosition = buffer.Position;
                    startupDiscardQpc = buffer.Qpc100ns;
                    continue;
                }
                captureTiming?.AcceptReleasedPacket(buffer.Position, consumed, buffer.Qpc100ns);
                acceptedPackets++;
                acceptedFrames += consumed;
                if (session.drift is not null && !ApplyCommand()) return;
                var path = session.path!;
                long dropped = path.Dropped;
                path.Produce((int)consumed, session.ticket, prefill: session.drift is not null || session.nominal is not null);
                if (session.nominal is not null && path.Dropped != dropped) throw new InvalidOperationException("Nominal/Drop");
            }
        }
        private InvalidOperationException RejectPacket(WasapiCapturePacketRejectionReason reason,
            uint announcedFrames, WasapiCaptureBuffer buffer, long now)
        {
            packetRejection = new(reason, announcedFrames, buffer.Frames, buffer.Flags, buffer.Position, buffer.Qpc100ns, now);
            return new InvalidOperationException(reason.ToString());
        }

        private bool CheckCommandAge()
        {
            var command = Volatile.Read(ref session.command);
            if (command is null || session.Now100ns() - command.Issued100ns <= WasapiDriftMath.MaxAge100ns)
                return true;
            session.Fail("Drift/CommandExpired");
            return false;
        }
        private bool ApplyCommand()
        {
            var command = Volatile.Read(ref session.command);
            if (!session.ticket.IsValid) return false;
            if (command is null) return true;
            long now = session.Now100ns();
            if (now < command.Issued100ns || now - command.Issued100ns > WasapiDriftMath.MaxAge100ns)
            {
                session.Fail("Drift/CommandExpired");
                return false;
            }
            if (command.Issued100ns == appliedCommand100ns) return true;
            if (!session.path!.SetTargetPpm(command.Generation, command.Ppm))
            {
                session.Fail("Drift/CommandRejected");
                return false;
            }
            appliedCommand100ns = command.Issued100ns;
            appliedAt100ns = now;
            return true;
        }
        private unsafe void FillRender(bool prime)
        {
            if (session.stop.IsSet) return;
            padding = client!.ReadPadding();
            if (padding > Capacity || (prime && padding != 0)) throw new InvalidOperationException("Padding nativo inválido.");
            uint frames = Capacity - padding;
            if (frames == 0 || session.stop.IsSet) return;
            nint data = render!.GetBuffer(frames); bufferInFlight = true; acquired++; acquiredFrames += frames;
            acquireResult = 0;
            uint releaseFlags = Silent;
            Span<byte> output = default;
            WasapiEncodedBlock encoded = default;
            try
            {
                if (data == 0) throw new InvalidOperationException("Buffer de render inválido.");
                if (!prime && session.nominal is { } nominal)
                {
                    var path = session.path!;
                    if (nominal.Encoder is not null)
                        output = new Span<byte>((void*)data, checked((int)frames * info.Mix!.BlockAlign));
                    if (!session.ticket.IsValid) preTicketRenderFrames += frames;
                    else if (path.FifoPrimingFrames == 0) warmupRenderFrames += frames;
                    else
                    {
                        if (path.ConsumerQueued <= nominal.MarginFrames) throw new InvalidOperationException("Nominal/FifoLow");
                        if (path.ConsumeNominal((int)frames, session.ticket, nominal.Encoder, output, out encoded))
                            releaseFlags = 0;
                    }
                }
            }
            finally
            {
                // Só os modos com codificador explícito podem entregar PCM. O ticket ainda precisa estar válido no Release.
                if (releaseFlags == 0 && (!session.ticket.IsValid || session.ObservationDeadlinePassed)) releaseFlags = Silent;
                if (releaseFlags == Silent) output.Clear();
                int hr;
                try { hr = render.ReleaseBuffer(frames, releaseFlags); releaseResult = hr; }
                catch { bufferUncertain = true; throw; }
                if (hr != 0) { bufferUncertain = true; throw new WasapiCallException("IAudioRenderClient.ReleaseBuffer", hr); }
                bufferInFlight = false; released++; releasedFrames += frames; if (prime) primed += frames;
                if (releaseFlags == 0 && session.nominal?.Audible is not null)
                {
                    pcmFrames += frames;
                    nonZeroFrames += encoded.NonZeroFrames;
                    peakAmplitude = Math.Max(peakAmplitude, encoded.PeakAmplitude);
                    peakGain = Math.Max(peakGain, encoded.PeakGain);
                    if (session.nominal.ExternalRoute)
                    {
                        if (encoded.MinimumGain >= WasapiExternalRouteSession.BypassGain - 1e-6f)
                        {
                            bypassNonZeroFrames += encoded.NonZeroFrames;
                            bypassPeak = Math.Max(bypassPeak, encoded.PeakAmplitude);
                        }
                        else if (encoded.PeakGain <= WasapiExternalRouteSession.EffectGain + 1e-6f &&
                            encoded.MinimumGain >= WasapiExternalRouteSession.EffectGain - 1e-6f)
                        {
                            effectNonZeroFrames += encoded.NonZeroFrames;
                            effectPeak = Math.Max(effectPeak, encoded.PeakAmplitude);
                        }
                    }
                }
            }
            if (!prime && session.nominal is null)
            {
                if (session.ticket.IsValid)
                {
                    if (session.drift is not null && session.path!.FifoPrimingFrames == 0) warmupRenderFrames += frames;
                    else session.path!.Consume((int)frames, session.ticket);
                }
                else if (session.observeCheckpoints) preTicketRenderFrames += frames;
            }
        }
        private bool TryCleanup()
        {
            if (cleanup is not null) return false;
            if (startAttempted && !stopConfirmed)
            {
                try
                {
                    int hr = client!.Stop(); stopResult = hr;
                    if (hr is not (0 or 1)) throw new WasapiCallException("IAudioClient.Stop", hr);
                    stopConfirmed = true;
                }
                catch (Exception problem) { error ??= "Stop não confirmado"; session.Fail(input ? "CaptureStop" : "RenderStop", problem); return false; }
            }
            if (bufferUncertain) { cleanup = "ReleaseBuffer não confirmado"; return false; }
            resources.Dispose(); cleanup = resources.CleanupError;
            if (!resources.Released) { session.Fail(input ? "CaptureRelease" : "RenderRelease"); return false; }
            return true;
        }
        public WasapiRoleSnapshot Snapshot(bool joined) => new(initialized, startAttempted, started, stopConfirmed, resources.Released, ownerId,
            Capacity, Period, latency, Interlocked.Read(ref acquired), Interlocked.Read(ref released), Interlocked.Read(ref acquiredFrames),
            Interlocked.Read(ref releasedFrames), Interlocked.Read(ref primed), padding, motion?.MovementSamples ?? 0,
            Done.Task.IsCompleted ? lastClock : null, flags, packetPosition, packetQpc, bufferInFlight, error, cleanup,
            startResult, stopResult, acquireResult, releaseResult, Done.Task.IsCompleted ? clockStatus : null,
            Interlocked.Read(ref acceptedPackets), Interlocked.Read(ref acceptedFrames),
            Interlocked.Read(ref startupDiscardedPackets), Interlocked.Read(ref startupDiscardedFrames),
            startupDiscardFlags, startupDiscardPosition, startupDiscardQpc, joined ? motion?.Snapshot() : null,
            joined ? captureTiming?.Snapshot(session.durationReached && session.failure is null ? session.observationEnd100ns : 0) : null,
            joined ? packetRejection : null);
    }
}
