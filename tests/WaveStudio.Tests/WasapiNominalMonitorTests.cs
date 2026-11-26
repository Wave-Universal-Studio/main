using System.Buffers.Binary;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiNominalMonitorTests
{
    [Theory]
    [InlineData(44100, 48000, 300, true)]
    [InlineData(48000, 44100, -300, false)]
    [InlineData(48000, 48000, 0, true)]
    public async Task DeliversNegotiatedPcmThroughFakeRenderAtNominalRatio(int input, int output, int ppm, bool floating)
    {
        using var rig = new WasapiDriftTests.VirtualRig(input, output, ppm, phase: 1, outputFloat: floating);
        var guard = new TransportActivityGuard();
        await using var session = rig.CreateNominal(guard);
        var format = rig.Route.Playback.Mix!;
        int firstPcm = 0, silent = 0, ordinal = 0;
        float peak = 0, firstSample = 0;
        rig.Calls.Output.OnRenderSubmitted = (frames, flags) =>
        {
            if (flags == 2) { silent++; return; }
            Assert.Equal(0u, flags);
            var span = rig.Calls.Output.Data.AsSpan(0, (int)frames * format.BlockAlign);
            float At(int i, Span<byte> bytes) => floating
                ? BinaryPrimitives.ReadSingleLittleEndian(bytes[(4 * i)..])
                : BinaryPrimitives.ReadInt16LittleEndian(bytes[(2 * i)..]) / 32768f;
            if (firstPcm == 0)
            {
                firstPcm = ordinal;
                firstSample = At(0, span);
            }
            for (int f = 0; f < frames; f++)
            {
                float value = At(f * format.Channels, span);
                Assert.True(float.IsFinite(value));
                if (format.Channels == 2) Assert.Equal(value, At(f * format.Channels + 1, span));
                peak = Math.Max(peak, Math.Abs(value));
            }
        };
        var run = session.RunAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
        await WaitForRunning(session, run);
        for (ordinal = 1; ordinal <= 400 && !run.IsCompleted; ordinal++)
        {
            if (ordinal == 11) session.SetFakeGain(.1f);
            rig.Advance(ordinal, run);
            session.Step();
        }

        var report = await run;
        Assert.Null(report.Cleanup.Failure);
        Assert.True(report.NominalObserved);
        Assert.True(report.PacketRateConfirmed);
        Assert.False(report.DriftPrecisionAvailable);
        Assert.False(report.Cleanup.TransportObserved);
        Assert.False(report.Cleanup.DriftControlEnabled);
        Assert.False(report.RenderSilent);
        Assert.True(silent >= 11);
        Assert.Equal(11, firstPcm);
        Assert.InRange(Math.Abs(firstSample), 0, .001f);
        Assert.InRange(peak, .05f, .10001f);
        Assert.Equal(0, report.Cleanup.DroppedFrames + report.Cleanup.UnderrunFrames);
        Assert.Equal(512, report.EndpointPrimed);
        Assert.True(report.FifoPrimed >= report.Cleanup.FifoCapacityFrames / 2);
        Assert.True(report.CoherentCuts > 350);
        Assert.True(report.IndependentCaptureObservations >= 35);
        Assert.True(report.IndependentRenderObservations >= 35);
        Assert.True(report.MinimumMean <= report.MaximumMean);
        Assert.True(report.Covered100ns >= 39_000_000);
        using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic);
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task ImpulseCrossesTheRealNominalPathWithinTheSyntheticFrameBudget()
    {
        using var rig = new WasapiDriftTests.VirtualRig(48000, 48000, 0, 0);
        await using var session = rig.CreateNominal(new());
        var input = rig.Calls.Input.Data;
        Array.Clear(input);
        // O impulso entra no primeiro frame real. O ganho parte de zero e só sobe em render autorizado.
        BinaryPrimitives.WriteInt16LittleEndian(input, 20000);
        BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan(2), 20000);
        rig.Calls.Input.OnStep = step =>
        {
            if (step == "ReleaseCapture") Array.Clear(input);
        };
        int ordinal = 0, firstSignalBlock = 0, firstSignalOffset = -1;
        rig.Calls.Output.OnRenderSubmitted = (frames, flags) =>
        {
            if (flags != 0 || firstSignalBlock != 0) return;
            for (int f = 0; f < frames; f++)
            {
                if (Math.Abs(BinaryPrimitives.ReadSingleLittleEndian(rig.Calls.Output.Data.AsSpan(f * 4))) < .00001f) continue;
                firstSignalBlock = ordinal;
                firstSignalOffset = f;
                break;
            }
        };
        session.SetFakeGain(.1f);
        var run = session.RunAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
        await WaitForRunning(session, run);
        for (ordinal = 1; ordinal <= 12; ordinal++)
        {
            rig.Advance(ordinal, run);
            session.Step();
        }
        session.RequestStop();
        var report = await run;
        Assert.Null(report.Cleanup.Failure);
        Assert.False(report.NominalObserved);
        Assert.Equal(3, firstSignalBlock);
        Assert.InRange(firstSignalOffset, 0, 160);
        long elapsedFrames = firstSignalBlock * 480L + firstSignalOffset;
        Assert.InRange(elapsedFrames / 48d, 30, 34);
    }

    [Theory]
    [InlineData("low", "Nominal/FifoLow")]
    [InlineData("high", "Nominal/FifoHigh")]
    [InlineData("get", "Render/GetRender")]
    [InlineData("selection", "SelectionInvalidated")]
    [InlineData("timestamp", "Capture")]
    [InlineData("clock", "Capture")]
    [InlineData("busy", "Nominal/CheckpointExpired")]
    public async Task FailuresStopDeliveryAndPreserveTheSingleSession(string fault, string expected)
    {
        using var rig = new WasapiDriftTests.VirtualRig(48000, 48000, 0, 0);
        var guard = new TransportActivityGuard();
        await using var session = rig.CreateNominal(guard);
        var run = session.RunAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
        await WaitForRunning(session, run);
        for (int ordinal = 1; ordinal <= 240; ordinal++)
        {
            rig.Advance(ordinal, run);
            session.Step();
        }
        long before = rig.Calls.Output.ReleasedFrames;
        rig.Calls.Output.OnRenderSubmitted = (_, flags) => Assert.Equal(2u, flags);
        switch (fault)
        {
            case "low":
                rig.ExtraRenderCycles = 4; rig.ExtraRenderFrames = 512;
                rig.Advance(241, run);
                break;
            case "high":
                rig.Advance(250, run);
                break;
            case "get":
                rig.Calls.Output.FailStep = "GetRender";
                rig.Advance(241, run);
                break;
            case "selection":
                rig.Calls.Output.OnStep = step => { if (step == "GetRender") session.Invalidate(rig.Generation); };
                session.SetFakeGain(.1f);
                rig.Advance(241, run);
                break;
            case "timestamp":
                var packet = rig.Calls.Input.CaptureBufferOverride!;
                rig.Calls.Input.CaptureBufferOverride = () => packet() with { Flags = 4 };
                rig.Advance(241, run);
                break;
            case "clock":
                for (int ordinal = 241; ordinal < 250; ordinal++) rig.Advance(ordinal, run);
                rig.Calls.Input.ClockResult = 1;
                rig.Advance(250, run);
                break;
            case "busy":
                for (int ordinal = 241; ordinal <= 292; ordinal++) rig.Advance(ordinal, run);
                session.Step();
                break;
        }

        var report = await run.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.False(report.NominalObserved);
        Assert.Equal(expected, report.Cleanup.Failure);
        Assert.True(report.Cleanup.Completed);
        Assert.False(report.Cleanup.ResourcesRetained);
        if (fault == "timestamp")
        {
            var rejected = Assert.IsType<WasapiCapturePacketRejection>(report.Cleanup.Capture.PacketRejection);
            Assert.Equal(WasapiCapturePacketRejectionReason.InvalidFlags, rejected.Reason);
            Assert.Equal(4u, rejected.Flags);
            Assert.Equal(0u, rig.Calls.Input.CaptureReleases.Last());
            Assert.Equal(before, report.Cleanup.Playback.FramesReleased);
        }
        Assert.True(rig.Calls.Output.ReleasedFrames >= before);
        Assert.Equal(0, report.Cleanup.UnderrunFrames + report.Cleanup.DroppedFrames);
        using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic);
        Assert.NotNull(lease);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeFacadeCannotEnableGainAndFiniteTimeCannotBeExtended(bool requestGain)
    {
        using var rig = new WasapiDriftTests.VirtualRig(48000, 48000, 0, 0);
        var route = rig.Route with
        {
            Capture = rig.Route.Capture with { Evidence = FormatEvidence.ExactCapability },
            Playback = rig.Route.Playback with { Evidence = FormatEvidence.ExactCapability }
        };
        await using var facade = WasapiNominalSession.CreateSilentProof(route);
        if (requestGain) Assert.Throws<InvalidOperationException>(() => facade.SetFakeGain(.1f));
        else await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => facade.RunAsync(TimeSpan.FromSeconds(6), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteStopOrReleaseRetainsTheActivityGuard(bool failRelease)
    {
        using var rig = new WasapiDriftTests.VirtualRig(48000, 48000, 0, 0);
        var guard = new TransportActivityGuard();
        var session = rig.CreateNominal(guard, TimeSpan.FromSeconds(1));
        var run = session.RunAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
        try
        {
            await WaitForRunning(session, run);
            for (int i = 1; i <= 12; i++) { rig.Advance(i, run); session.Step(); }
            if (failRelease)
            {
                rig.Calls.Output.ReleaseResult = WasapiSilentTests.Invalidated;
                rig.Advance(13, run);
            }
            else
            {
                rig.Calls.Output.StopResult = WasapiSilentTests.Invalidated;
                session.RequestStop();
            }
            var report = await run;
            Assert.False(report.NominalObserved);
            Assert.True(report.Cleanup.ResourcesRetained);
            Assert.Null(guard.TryAcquire(AudioActivity.ShortDiagnostic));
        }
        finally
        {
            if (failRelease)
                await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask());
            else
            {
                rig.Calls.Output.StopResult = 0;
                await session.DisposeAsync();
            }
        }
        using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic);
        if (failRelease) Assert.Null(lease); else Assert.NotNull(lease);
    }

    [Fact]
    public void EncoderKeepsZeroDefaultAndSlewsWithFiniteLimitedOutput()
    {
        var format = WasapiContractTests.Pcm(2, floating: false);
        var encoder = new WasapiMonitorEncoder(format);
        float[] source = [1, -1, float.NaN, float.PositiveInfinity, 4, -4];
        var buffer = new byte[12];
        Assert.False(encoder.Encode(source, buffer));
        Assert.Throws<ArgumentOutOfRangeException>(() => encoder.SetGain(.10001f));
        encoder.SetGain(.1f);
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < 1000; i++) encoder.Encode(source, buffer);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) encoder.Encode(source, buffer);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, bytes);
        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(5));
        Assert.Equal(3277, BinaryPrimitives.ReadInt16LittleEndian(buffer));
        Assert.Equal(-3277, BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(2)));
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(4)));
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(6)));
        Assert.Equal(3277, BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(8)));
        encoder.SetGain(0);
        for (int i = 0; i < 100; i++) encoder.Encode(source, buffer);
        Assert.False(encoder.Encode(source, buffer));
    }

    private static async Task WaitForRunning(WasapiNominalSession session, Task<WasapiNominalReport> run)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        while (!session.Running)
        {
            if (run.IsCompleted) Assert.Fail("A sessão nominal encerrou durante a preparação: " + (await run).Cleanup.Failure);
            await Task.Delay(1, timeout.Token);
        }
    }
}
