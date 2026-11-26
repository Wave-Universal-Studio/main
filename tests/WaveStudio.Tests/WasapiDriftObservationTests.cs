using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiDriftObservationTests
{
    private const long Origin = 1_000_000_000;
    private const long Cadence = 1_000_000;
    private const int Rate = 48_000;

    [Fact]
    public void LongWindowReducesPhaseUncertaintyWithoutChangingTheRollingEstimator()
    {
        var control = Create();
        Feed(control, 1, 1_200, renderPpm: 500);

        var report = control.Snapshot();
        var observation = Assert.IsType<WasapiDriftObservationReport>(report.Observation);
        var window = Assert.IsType<WasapiDriftObservationWindow>(observation.LongestWindow);
        Assert.Equal(WasapiDriftObservationStatus.Positive, observation.Status);
        Assert.InRange(window.Interval.Minimum, 165, 168);
        Assert.InRange(window.Interval.Maximum, 832, 835);
        Assert.True(window.Overlap100ns >= 1_190_000_000);
        Assert.True(observation.CheckpointCoverageConfirmed(1_200 * Cadence));
        Assert.True(report.Recent.Last().CorrectionInterval!.Value.Width > 25_000);
        Assert.False(observation.WithinControllerWidth);
        Assert.False(observation.CorrectionSeen);
        Assert.Equal(0, report.TrackingUpdates);
    }

    [Fact]
    public void FullRunCountsRetainEvidenceAfterTheRecentTraceIsOverwritten()
    {
        var control = Create();
        Feed(control, 1, 100);
        for (int index = 101; index <= 300; index++)
            control.Step(Origin + index * Cadence, new(WasapiCheckpointStatus.OwnerBusy));

        var report = control.Snapshot();
        var observation = Assert.IsType<WasapiDriftObservationReport>(report.Observation);
        Assert.All(report.Recent, sample => Assert.Null(sample.CorrectionInterval));
        Assert.True(observation.RollingCoverageConfirmed);
        Assert.Equal(69_000_000, observation.RollingWindowSpan100ns);
        Assert.Equal(WasapiDriftObservationStatus.IncludesZero, observation.Status);
        Assert.Equal(100, observation.CoherentCuts);
        Assert.Equal(0, observation.MinimumQueuedFrames);
        Assert.Equal(400, observation.MaximumQueuedFrames);
        Assert.False(observation.CheckpointCoverageConfirmed(300 * Cadence));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LongWindowCannotSpanAClockEpochChangeOrLossOfHistory(bool changeEpoch)
    {
        var control = Create();
        for (int block = 0; block < 10; block++)
        {
            int start = 1 + block * 38;
            if (!changeEpoch && block != 0)
                control.Step(Origin + (start - 1) * Cadence, new(WasapiCheckpointStatus.HistoryLost));
            Feed(control, start, start + 36, captureEpoch: changeEpoch ? block : 0);
        }

        var observation = Assert.IsType<WasapiDriftObservationReport>(control.Snapshot().Observation);
        Assert.True(observation.RollingCoverageConfirmed);
        Assert.Equal(10, observation.ContinuousSegments);
        Assert.Equal(36_000_000UL, observation.LongestEligibleOverlap100ns);
        Assert.Equal(WasapiDriftObservationStatus.InsufficientEvidence, observation.Status);
        Assert.Null(observation.LongestWindow);
    }

    [Fact]
    public void PacketDisagreementWithholdsAClockEstimate()
    {
        var pair = Pair(1, 0, 0);
        var late = Pair(1_200, 0, 0);
        late = late with { Packet = late.Packet with { Position = late.Packet.Position + 4_800 } };
        Assert.Null(WasapiDriftMath.EstimateObservation(pair, late, Rate, Rate, new(480, 480, 480)));
    }

    [Theory]
    [InlineData(false, 11, false)]
    [InlineData(true, 120, true)]
    [InlineData(true, 121, false)]
    public async Task OnlyTheDiagnosticMayUseTheExtendedBound(bool diagnostic, int seconds, bool withinBound)
    {
        var route = WasapiContractTests.Route();
        route = route with
        {
            Capture = route.Capture with { Evidence = FormatEvidence.ExactCapability },
            Playback = route.Playback with { Evidence = FormatEvidence.ExactCapability }
        };
        var guard = new TransportActivityGuard();
        using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic);
        Assert.NotNull(lease);
        await using var session = new WasapiSilentSession(route, guard, nativeDriftDiagnostic: diagnostic);
        var token = TestContext.Current.CancellationToken;

        // O bloqueio impede que este teste abra o dispositivo, inclusive se o limite de duração regredir.
        if (withinBound)
            Assert.Throws<InvalidOperationException>(() => { _ = session.RunAsync(TimeSpan.FromSeconds(seconds), token); });
        else
            Assert.Throws<ArgumentOutOfRangeException>("duration", () => { _ = session.RunAsync(TimeSpan.FromSeconds(seconds), token); });
    }

    private static WasapiDriftControl Create() =>
        new(Rate, Rate, 4096, apply: false, new(480, 480, 480), Origin, observeLongDuration: true);

    private static void Feed(WasapiDriftControl control, int first, int last, int renderPpm = 0, int captureEpoch = 0)
    {
        for (int index = first; index <= last; index++)
        {
            long now = Origin + index * Cadence;
            var pair = Pair(index, renderPpm, captureEpoch);
            var capture = new WasapiDriftOwner(pair.Capture, pair.CaptureEpoch, NativeClockResult.Valid,
                pair.Packet, CaptureWindows: true, FifoPrimingFrames: 2048, AppliedPpm: 0, AppliedCommand100ns: 0, AppliedAt100ns: 0);
            var render = new WasapiDriftOwner(pair.Render, pair.RenderEpoch, NativeClockResult.Valid, default,
                CaptureWindows: false, FifoPrimingFrames: 0, AppliedPpm: 0, AppliedCommand100ns: 0, AppliedAt100ns: 0);
            var cut = new WasapiCheckpoint(control, now, new(index, now, default, capture), new(index, now, default, render),
                index % 5 * 100, null);
            control.Step(now, new(WasapiCheckpointStatus.Available, cut));
        }
    }

    private static WasapiDriftPair Pair(int index, int renderPpm, long captureEpoch)
    {
        ulong qpc = (ulong)(Origin + index * Cadence);
        ulong frames = (ulong)index * 4_800;
        ulong render = (ulong)Math.Round(frames * (1 + renderPpm / 1_000_000d));
        return new(new(100_000 + frames, Rate, qpc), new(200_000 + render, Rate, qpc),
            new(300_000 + frames, Rate, qpc), captureEpoch, 0);
    }
}
