using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed partial class WasapiDriftTests
{
    [Theory]
    [InlineData(44100, 48000, 300, 1)]
    [InlineData(48000, 44100, -300, -1)]
    [InlineData(48000, 48000, 0, 0)]
    public async Task NativeSessionPathTracksOnlyAfterFifoPrefillAndFreshEvidence(int input, int output, int drift, int phase)
    {
        var adaptive = await RunVirtual(input, output, drift, phase, 1800, apply: true);
        Assert.InRange(adaptive.Report.TrackingUpdates, 100, 180);
        Assert.Equal(0, adaptive.Final.DroppedFrames + adaptive.Final.UnderrunFrames);
        Assert.True(adaptive.Final.Completed);
        Assert.True(adaptive.Final.AlwaysSilent);
        Assert.Null(adaptive.Final.Failure);
        Assert.True(adaptive.Last.Capture.Evidence.FifoPrimingFrames >= adaptive.Final.FifoCapacityFrames / 2);
        Assert.Equal(512, adaptive.Last.Render.Counts.PrimedFrames);
        Assert.True(adaptive.Last.Render.Counts.WarmupRenderFrames > 0);
        Assert.InRange(adaptive.FirstTracking100ns - VirtualRig.Origin, 30_000_000, 48_000_000);
        var evidence = adaptive.Last.Capture.Evidence;
        Assert.InRange(evidence.AppliedAt100ns - evidence.AppliedCommand100ns, 0, 100_000);
        Assert.Equal(adaptive.Last.QueuedFrames, adaptive.Final.ProducedFrames - adaptive.Final.ReadFrames);
        Assert.InRange(Math.Abs(adaptive.Last.RequestedPpm!.Value - evidence.AppliedPpm), 0, 5);
        if (drift == 0)
            Assert.InRange(Math.Abs(evidence.AppliedPpm), 0, 20);
        else
            Assert.InRange(evidence.AppliedPpm * -Math.Sign(drift), 150, 410);
    }

    [Fact]
    public async Task LongerVirtualRunImprovesOccupancyComparedWithNominalPath()
    {
        var nominal = await RunVirtual(48000, 48000, 300, 0, 4500, apply: false);
        var adaptive = await RunVirtual(48000, 48000, 300, 0, 4500, apply: true);

        Assert.Equal(0, nominal.Report.TrackingUpdates);
        Assert.Equal(0, nominal.Last.Capture.Evidence.AppliedPpm);
        Assert.True(adaptive.Report.TrackingUpdates > 400);
        Assert.Equal(0, adaptive.Final.DroppedFrames + adaptive.Final.UnderrunFrames);
        Assert.True(adaptive.AverageDistanceToTarget + 140 < nominal.AverageDistanceToTarget);
        Assert.True(adaptive.Final.ProducedFrames < nominal.Final.ProducedFrames);
        SaveMetrics("occupancy-45s", new
        {
            Nominal = nominal.AverageDistanceToTarget, Adaptive = adaptive.AverageDistanceToTarget,
            NominalProduced = nominal.Final.ProducedFrames, AdaptiveProduced = adaptive.Final.ProducedFrames,
            AdaptivePpm = adaptive.Last.Capture.Evidence.AppliedPpm
        });
    }

    [Fact]
    public async Task UncertainClocksRemainNominalAndReachBoundedStartupDeadline()
    {
        var trial = await RunVirtual(48000, 48000, 300, 0, 850, apply: true, uncertainty: new(480, 480, 480));
        Assert.Equal("Drift/StartupExpired", trial.Final.Failure);
        Assert.True(trial.Final.Completed);
        Assert.Equal(0, trial.Report.TrackingUpdates);
        Assert.True(trial.Report.MeasuredWindows > 0);
        Assert.True(trial.Last.CorrectionInterval!.Value.Width > 1_000);
        Assert.Equal(0, trial.Last.Capture.Evidence.AppliedPpm);
    }

    [Fact]
    public async Task SaturationAndSlewAreExercisedThroughTheActualPath()
    {
        var trial = await RunVirtual(48000, 48000, -850, 0, 1200, apply: true);

        Assert.Null(trial.Final.Failure);
        Assert.Equal(WasapiDriftReason.Saturated, trial.Last.Reason);
        Assert.InRange(trial.Last.RequestedPpm!.Value, 100, 500);
        Assert.InRange(trial.Last.Capture.Evidence.AppliedPpm, 100, 500);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("history")]
    [InlineData("clock")]
    [InlineData("selection")]
    [InlineData("underrun")]
    [InlineData("get")]
    public async Task FaultsAfterActivationInvalidateDeliveryAndKeepRenderSilent(string fault)
    {
        using var rig = new VirtualRig(48000, 48000, 300, phase: 0);
        var guard = new TransportActivityGuard();
        await using var session = rig.Create(guard, apply: true, new(2, 2, 2));
        var run = session.RunAsync(TimeSpan.FromSeconds(90), TestContext.Current.CancellationToken);
        await Activate(rig, session, run);

        switch (fault)
        {
            case "timeout":
                for (int i = 431; i <= 481 && !run.IsCompleted; i++) rig.Advance(i, run);
                break;
            case "history":
                // Aumenta a granularidade das operações, preservando o intervalo de relógio.
                for (int i = 431; i < 440; i++) rig.Advance(i, run);
                rig.ExtraRenderCycles = 258;
                rig.Advance(440, run);
                session.StepDrift();
                break;
            case "clock":
                rig.Calls.Input.ClockResult = 1;
                rig.Advance(441, run);
                break;
            case "selection":
                session.Invalidate(rig.Generation);
                break;
            case "underrun":
                for (int i = 431; i < 440; i++) rig.Advance(i, run);
                rig.ExtraRenderCycles = 5;
                rig.ExtraRenderFrames = 512;
                rig.Advance(440, run);
                session.StepDrift();
                break;
            case "get":
                rig.Calls.Output.FailStep = "GetRender";
                rig.Advance(431, run);
                break;
        }

        var result = await run.WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
        Assert.True(result.AlwaysSilent);
        Assert.True(result.Completed);
        Assert.False(result.TransportObserved);
        string expected = fault switch
        {
            "timeout" => "Drift/CommandExpired",
            "history" => "Drift/HistoryLost",
            "clock" => "Capture",
            "selection" => "SelectionInvalidated",
            "underrun" => "Drift/FifoBoundary",
            _ => "Render/GetRender"
        };
        Assert.Equal(expected, result.Failure);
        Assert.Equal(WasapiCheckpointStatus.Inactive, session.ReadCheckpoint(rig.Generation).Status);
        using var lease = guard.TryAcquire(AudioActivity.NativePreparation);
        Assert.NotNull(lease);
    }


    [Fact]
    public async Task AnOwnerHoldingABufferPreventsRenewalAndKeepsTheGuardUntilRelease()
    {
        using var rig = new VirtualRig(48000, 48000, 300, phase: 0);
        var guard = new TransportActivityGuard();
        var session = rig.Create(guard, apply: true, new(2, 2, 2), TimeSpan.FromSeconds(1));
        using var leave = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = session.RunAsync(TimeSpan.FromSeconds(90), TestContext.Current.CancellationToken);
        try
        {
            await Activate(rig, session, run);
            rig.Calls.Input.OnStep = step =>
            {
                if (step != "ReleaseCapture") return;
                entered.TrySetResult();
                Assert.True(leave.Wait(TimeSpan.FromSeconds(8)));
            };
            rig.BeginCapture(440);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
            var unavailable = session.StepDrift();
            Assert.Equal(WasapiCheckpointStatus.OwnerBusy, unavailable.CheckpointStatus);
            Assert.Null(unavailable.RequestedPpm);

            rig.SetTime(481);
            var expired = session.StepDrift();
            Assert.Equal(WasapiCheckpointStatus.OwnerStale, expired.CheckpointStatus);
            var partial = await run;
            Assert.Equal("Checkpoint/OwnerStale", partial.Failure);
            Assert.True(partial.Capture.BufferInFlight);
            Assert.True(partial.ResourcesRetained);
            Assert.Null(guard.TryAcquire(AudioActivity.ShortDiagnostic));
        }
        finally
        {
            leave.Set();
            await session.DisposeAsync();
        }
        using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic);
        Assert.NotNull(lease);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnconfirmedStopOrReleaseRetainsGuardInDriftMode(bool failRelease)
    {
        using var rig = new VirtualRig(48000, 48000, 300, phase: 0);
        var guard = new TransportActivityGuard();
        var session = rig.Create(guard, apply: true, new(2, 2, 2), TimeSpan.FromSeconds(1));
        var run = session.RunAsync(TimeSpan.FromSeconds(90), TestContext.Current.CancellationToken);
        await Activate(rig, session, run);
        if (failRelease)
        {
            rig.Calls.Input.ReleaseResult = WasapiSilentTests.Invalidated;
            rig.Advance(431, run);
        }
        else
        {
            rig.Calls.Output.StopResult = WasapiSilentTests.Invalidated;
            session.RequestStop();
        }
        var partial = await run;
        Assert.True(partial.ResourcesRetained);
        Assert.False(partial.Completed);
        Assert.Equal(failRelease ? "Capture/IAudioCaptureClient.ReleaseBuffer" : "RenderStop/IAudioClient.Stop", partial.Failure);
        Assert.Equal(WasapiCheckpointStatus.Inactive, session.ReadCheckpoint(rig.Generation).Status);
        Assert.Null(guard.TryAcquire(AudioActivity.ShortDiagnostic));

        if (failRelease)
        {
            int calls = rig.Calls.Input.CaptureReleases.Count;
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask());
            Assert.Equal(calls, rig.Calls.Input.CaptureReleases.Count);
            Assert.Equal(0, rig.Calls.Input.DisposeCalls);
            Assert.Null(guard.TryAcquire(AudioActivity.ShortDiagnostic));
        }
        else
        {
            rig.Calls.Output.StopResult = 0;
            await session.DisposeAsync();
            using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic);
            Assert.NotNull(lease);
        }
    }

    private static async Task Activate(VirtualRig rig, WasapiSilentSession session, Task<WasapiSilentSnapshot> run)
    {
        await WaitUntilRunning(session, run);
        for (int i = 1; i <= 430; i++)
        {
            rig.Advance(i, run);
            session.StepDrift();
        }
        Assert.Equal(WasapiDriftReason.Tracking, session.StepDrift().Reason);
    }

    [Fact]
    public void ApplyingCommandsAndPublishingCaptureEvidenceDoesNotAllocateOnTheProducer()
    {
        var route = WasapiContractTests.Route(inputRate: 44100);
        using var path = new WasapiSilentPath(route, 512, 512, 100_000, 100_000);
        var ticket = new DeliveryTicket(route);
        ticket.Enable();
        long time = 100;
        var ledger = new WasapiCheckpointLedger(ticket, path.CapacityFrames, () => time);
        void Batch()
        {
            ledger.Capture.Begin();
            path.SetTargetPpm(ticket.Capture.Generation, 300);
            path.CopyCapture(ReadOnlySpan<byte>.Empty, 32, true);
            path.Produce(32, ticket, prefill: true);
            ledger.Capture.Complete(new(time - 99, time - 99, (time - 99) * 32, (time - 99) * 32,
                time - 99, (time - 99) * 32, 0, 0, path.Produced, path.Dropped, 0),
                new(default, 0, NativeClockResult.Valid, default, false, path.FifoPrimingFrames, path.AppliedPpm, 0, 0));
            path.Consume(35, ticket);
            time++;
        }
        for (int i = 0; i < 1000; i++) Batch();
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Batch();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        double milliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Assert.Equal(0, allocated);
        Assert.InRange(milliseconds, 0, 5_000);
        SaveMetrics("producer-1000", new { AllocatedBytes = allocated, ElapsedMilliseconds = milliseconds });
        Assert.Equal(2000, ledger.Capture.State.Sequence);
        Assert.InRange(path.AppliedPpm, 0.1, 500);
    }

    private static void SaveMetrics(string name, object metrics)
    {
        string baseDirectory = AppContext.BaseDirectory;
        string configuration = Path.GetFileName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(baseDirectory)))!;
        string path = Path.GetFullPath(Path.Combine(baseDirectory, "../../../../../artifacts/f2-i6b2b-drift"));
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, $"{name}-{configuration}.json"), System.Text.Json.JsonSerializer.Serialize(metrics));
    }

    private readonly record struct VirtualResult(WasapiSilentSnapshot Final, WasapiDriftReport Report,
        WasapiDriftSample Last, long FirstTracking100ns, double AverageDistanceToTarget);

    private static async Task<VirtualResult> RunVirtual(int input, int output, int drift, int phase, int blocks, bool apply,
        WasapiTimingUncertainty? uncertainty = null)
    {
        using var rig = new VirtualRig(input, output, drift, phase);
        await using var session = rig.Create(new(), apply, uncertainty ?? new(2, 2, 2));
        var run = session.RunAsync(TimeSpan.FromSeconds(90), TestContext.Current.CancellationToken);
        await WaitUntilRunning(session, run);
        long first = 0;
        double total = 0;
        int count = 0;
        double lastPpm = 0;
        WasapiDriftSample last = default;
        for (int i = 1; i <= blocks && !run.IsCompleted; i++)
        {
            rig.Advance(i, run);
            if (i % 10 != 0) continue;
            last = session.StepDrift();
            if (last.RequestedPpm is { } target)
            {
                if (first == 0) first = last.Cut100ns;
                Assert.InRange(Math.Abs(target - lastPpm), 0, 2.500001);
                Assert.InRange(Math.Abs(target), 0, AdaptiveResampler.MaximumPpm);
                lastPpm = target;
            }
            if (i > blocks - 500 && last.CheckpointStatus == WasapiCheckpointStatus.Available)
            {
                total += Math.Abs(last.QueuedFrames - 1024);
                count++;
            }
        }

        session.RequestStop();
        var final = await run;
        return new(final, session.ReadDriftReport()!, last, first, count == 0 ? 0 : total / count);
    }

    private static async Task WaitUntilRunning(WasapiSilentSession session, Task<WasapiSilentSnapshot> run)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        while (!session.DriftRunning)
        {
            if (run.IsCompleted) Assert.Fail("A abertura da sessão virtual falhou: " + (await run).Failure);
            await Task.Delay(1, timeout.Token);
        }
    }

    internal sealed class VirtualRig : IDisposable
    {
        public const long Origin = 10_000_000_000;
        public const int Block100ns = 100_000;
        private readonly AutoResetEvent[] pulse = [new(false), new(false)];
        private readonly AutoResetEvent[] finished = [new(false), new(false)];
        private readonly WaitHandle[][] waits = [new WaitHandle[2], new WaitHandle[2]];
        private readonly ManualResetEventSlim stopKnown = new();
        private WaitHandle? ownerStop;
        private readonly double inputActual, outputActual;
        private readonly TransportRoute route;
        internal TransportRoute Route => route;
        public WasapiSilentTests.Calls Calls { get; }
        public long Generation => route.Capture.Endpoint.Generation;
        private long now = Origin;
        private long captured, rendered;
        private long desiredCapture, desiredRender;
        public int ExtraRenderCycles, ExtraRenderFrames = 1;

        public VirtualRig(int input, int output, int drift, int phase, bool outputFloat = true, TransportRoute? assigned = null)
        {
            route = assigned ?? WasapiContractTests.Route(inputRate: input, outputRate: output, inputChannels: input == 44100 ? 1 : 2,
                outputChannels: output == 44100 ? 2 : 1, outputFloat: outputFloat);
            inputActual = input * (1 + drift * 1e-6);
            outputActual = output;
            Calls = new(route);
            Calls.Input.Capacity = Calls.Output.Capacity = 512;
            Calls.Input.PacketSizeOverride = () => (uint)Math.Min(512, desiredCapture - captured);
            Calls.Input.CaptureBufferOverride = () =>
            {
                ulong timestamp = (ulong)(Origin + Math.Round(captured / inputActual * 10_000_000)
                    + captured % 17);
                uint frames = (uint)Math.Min(512, desiredCapture - captured);
                ulong position = (ulong)captured + 800_000;
                captured += frames;
                return new(0, 0, frames, 0, position, timestamp);
            };
            Calls.Output.RenderPaddingOverride = () =>
            {
                uint frames = (uint)Math.Clamp(desiredRender - rendered, 0, 512);
                rendered += frames;
                return 512 - frames;
            };
            Calls.Input.ReadClockOverride = index => Clock(index, inputActual, input * 4, phase * input / 100, 30_000_000);
            Calls.Output.ReadClockOverride = index => Clock(index, outputActual, output * 8, 0, 70_000_000);
        }

        public WasapiSilentSession Create(TransportActivityGuard guard, bool apply, WasapiTimingUncertainty uncertainty, TimeSpan? timeout = null) =>
            new(route, guard, Calls.Open, timeout, simulation: new(() => Volatile.Read(ref now), Wait, input => finished[input ? 0 : 1].Set(), uncertainty, apply));

        internal WasapiNominalSession CreateNominal(TransportActivityGuard guard, TimeSpan? timeout = null)
        {
            Calls.Output.OnRenderSubmitted ??= (_, flags) => Assert.Equal(2u, flags);
            return WasapiNominalSession.CreateFake(route, guard, Calls.Open,
                new(() => Volatile.Read(ref now), Wait, input => finished[input ? 0 : 1].Set(), default, false), timeout);
        }

        internal WasapiAudibleProofSession CreateAudible(TransportActivityGuard guard, WasapiAudibleConsent consent) =>
            WasapiAudibleProofSession.CreateSimulated(route, consent, guard, Calls.Open,
                new(() => Volatile.Read(ref now), Wait, input => finished[input ? 0 : 1].Set(), default, false));

        internal WasapiExternalRouteSession CreateExternal(ExternalRouteTopology topology, ExternalFlow flow,
            ExternalRouteConsent consent, TransportActivityGuard guard) => new(topology, flow, consent, guard, Calls.Open,
                new(() => Volatile.Read(ref now), Wait, input => finished[input ? 0 : 1].Set(), default, false));

        internal WasapiDriftSimulation NominalPacing() =>
            new(() => Volatile.Read(ref now), Wait, input => finished[input ? 0 : 1].Set(), default, false);

        private WasapiClockReading Clock(int index, double actualRate, int frequency, int phaseFrames, ulong offset)
        {
            long qpc = now - (index * 29 % 170) - 1;
            double elapsed = (qpc - Origin) / 10_000_000d;
            int nominalRate = frequency / (offset == 30_000_000 ? 4 : 8);
            long units = (long)Math.Floor(elapsed * actualRate / nominalRate * frequency);
            if (index == 1) units += (long)phaseFrames * frequency / nominalRate;
            int result = offset == 30_000_000 ? Calls.Input.ClockResult : Calls.Output.ClockResult;
            return new(0, (ulong)frequency, result, offset + (ulong)units, (ulong)qpc);
        }

        private void Wait(bool input, WaitHandle stopped)
        {
            int index = input ? 0 : 1;
            waits[index][0] = pulse[index];
            waits[index][1] = stopped;
            if (Interlocked.CompareExchange(ref ownerStop, stopped, null) is null) stopKnown.Set();
            WaitHandle.WaitAny(waits[index]);
        }

        public void SetTime(int ordinal) => Volatile.Write(ref now, Origin + (long)ordinal * Block100ns);

        public void BeginCapture(int ordinal)
        {
            SetTime(ordinal);
            desiredCapture = (long)Math.Floor(ordinal * inputActual / 100);
            pulse[0].Set();
        }

        public void Advance(int ordinal, Task run)
        {
            if (run.IsCompleted) return;
            SetTime(ordinal);
            desiredCapture = (long)Math.Floor(ordinal * inputActual / 100);
            desiredRender = (long)Math.Floor(ordinal * outputActual / 100);
            if (!Cycle(0, run) || !Cycle(1, run)) return;
            for (int i = 0; i < ExtraRenderCycles; i++)
            {
                desiredRender = rendered + ExtraRenderFrames;
                if (!Cycle(1, run)) break;
            }
        }

        private bool Cycle(int role, Task run)
        {
            if (run.IsCompleted) return false;
            if (Volatile.Read(ref ownerStop) is not { } stopped)
            {
                int ready = WaitHandle.WaitAny([stopKnown.WaitHandle, ((IAsyncResult)run).AsyncWaitHandle], 5000);
                Assert.NotEqual(WaitHandle.WaitTimeout, ready);
                if (ready != 0) return false;
                stopped = Volatile.Read(ref ownerStop)!;
            }
            if (stopped.WaitOne(0)) return false;
            pulse[role].Set();
            // Uma falha pula CycleCompleted. O sinal de Stop chega do owner antes de o teste aguardar o relatório.
            int result = WaitHandle.WaitAny([stopped, finished[role]], 5000);
            Assert.NotEqual(WaitHandle.WaitTimeout, result);
            return result == 1;
        }

        public void Dispose()
        {
            stopKnown.Dispose();
            foreach (var signal in pulse) signal.Dispose();
            foreach (var signal in finished) signal.Dispose();
        }
    }
}
