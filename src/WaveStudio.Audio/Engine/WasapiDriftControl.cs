namespace WaveStudio.Audio.Engine;

internal readonly record struct WasapiDriftOwner(WasapiDriftPoint Clock, long ClockEpoch, NativeClockResult? ClockStatus,
    WasapiDriftPoint Packet, bool CaptureWindows, long FifoPrimingFrames, double AppliedPpm, long AppliedCommand100ns, long AppliedAt100ns);
internal sealed record WasapiDriftSimulation(Func<long> Now, Action<bool, WaitHandle> WaitForCycle,
    Action<bool> CycleCompleted, WasapiTimingUncertainty Uncertainty, bool ApplyCorrection = true);
internal sealed record WasapiDriftCommand(long Generation, long Issued100ns, double Ppm);
internal enum WasapiDriftReason
{
    Waiting, CheckpointUnavailable, HistoryLost, PhaseChanged, InsufficientPrecision, FifoBoundary,
    EvidenceExpired, StartupExpired, Observed, Tracking, Saturated
}
internal readonly record struct WasapiDriftSample(long Cut100ns, WasapiCheckpointStatus CheckpointStatus, WasapiDriftReason Reason,
    WasapiCheckpointOwner Capture, WasapiCheckpointOwner Render, long QueuedFrames,
    double? MinimumMeanFrames, double? MaximumMeanFrames, PpmInterval? CorrectionInterval, double? RequestedPpm,
    long? OccupancyStart100ns = null, long? ClockAnchorCut100ns = null);
internal sealed record WasapiDriftReport(bool CorrectionEnabled, WasapiTimingUncertainty Uncertainty,
    long Samples, long AvailableCuts, long MeasuredWindows, long TrackingUpdates, long StartupUnderrunFrames,
    WasapiDriftReason FinalReason, IReadOnlyList<WasapiDriftSample> Recent, WasapiDriftObservationReport? Observation);

// O coordenador é o único autor da política. A captura só lê o último comando e o aplica entre operações.
internal sealed class WasapiDriftControl(int inputRate, int outputRate, int capacity, bool apply,
    WasapiTimingUncertainty uncertainty, long origin, bool observeLongDuration = false)
{
    public const long Cadence100ns = 1_000_000;
    private const long Warmup100ns = 80_000_000;
    public const double MaximumWidthPpm = 200;
    private readonly WasapiDriftObservation? observation = observeLongDuration && !apply
        ? new(inputRate, outputRate, uncertainty, origin)
        : null;
    private readonly ClockRatioController controller = new(inputRate, outputRate, 10_000_000, capacity, capacity / 2);
    private readonly (long Cut, WasapiDriftPair Pair)[] anchors = new (long, WasapiDriftPair)[36];
    private readonly WasapiDriftSample[] trace = new WasapiDriftSample[128];
    private int head, count;
    private long samples, available, measured, tracked, startupUnderruns;
    private readonly long startedAt = origin;
    private long lastCut = origin, lastTick = -1;
    public WasapiCheckpoint? Baseline { get; private set; }
    public WasapiDriftSample Last { get; private set; }
    public bool MustStop { get; private set; }

    public bool IsDue(long now) => lastTick < 0 || now < lastTick || now - lastTick >= Cadence100ns;

    public WasapiDriftSample Step(long now, WasapiCheckpointResult result)
    {
        lastTick = now;
        if (result.Checkpoint is not { } cut)
        {
            bool lost = result.Status == WasapiCheckpointStatus.HistoryLost;
            if (lost && !apply)
            {
                Baseline = null;
                if (observation is not null) head = count = 0;
            }
            return Record(new(now, result.Status, lost ? WasapiDriftReason.HistoryLost :
                now - lastCut >= WasapiDriftMath.MaxAge100ns ? WasapiDriftReason.EvidenceExpired : WasapiDriftReason.CheckpointUnavailable,
                default, default, 0, null, null, null, null));
        }

        var previous = Baseline;
        Baseline = cut;
        available++;
        long elapsed = cut.Cut100ns - lastCut;
        lastCut = cut.Cut100ns;
        var current = new WasapiDriftSample(cut.Cut100ns, result.Status, WasapiDriftReason.Waiting, cut.Capture, cut.Render,
            cut.QueuedFrames, null, null, null, null);
        if (cut.Mean is { } area)
            current = current with { MinimumMeanFrames = (double)area.MinimumFrameTicks / area.Duration100ns,
                MaximumMeanFrames = (double)area.MaximumFrameTicks / area.Duration100ns, OccupancyStart100ns = cut.Cut100ns - area.Duration100ns };

        if (tracked == 0) startupUnderruns = cut.Render.Counts.FifoMissingFrames;
        if (apply && (cut.Capture.Counts.FifoMissingFrames != 0 ||
            (tracked != 0 && (cut.Render.Counts.FifoMissingFrames != startupUnderruns ||
                cut.QueuedFrames == 0 || cut.QueuedFrames == capacity))))
            return Record(current with { Reason = WasapiDriftReason.FifoBoundary });
        if (apply && elapsed > WasapiDriftMath.MaxAge100ns)
            return Record(current with { Reason = WasapiDriftReason.EvidenceExpired });

        var capture = cut.Capture.Evidence;
        var render = cut.Render.Evidence;
        if (capture.ClockStatus == NativeClockResult.Provisional)
        {
            head = count = 0;
            return Record(current with { Reason = WasapiDriftReason.PhaseChanged });
        }
        if (capture.ClockStatus != NativeClockResult.Valid || render.ClockStatus != NativeClockResult.Valid ||
            !Fresh(capture.Clock, lastCut) || !Fresh(render.Clock, lastCut) || !Fresh(capture.Packet, lastCut))
            return Record(current with { Reason = WasapiDriftReason.Waiting });

        var pair = new WasapiDriftPair(capture.Clock, render.Clock, capture.Packet, capture.ClockEpoch, render.ClockEpoch);
        if (count != 0 && (pair.CaptureEpoch != anchors[head].Pair.CaptureEpoch ||
            pair.RenderEpoch != anchors[head].Pair.RenderEpoch)) head = count = 0;
        var estimate = ObservePair(pair, lastCut, out long anchorCut);
        if (estimate is null || !capture.CaptureWindows)
            return Record(current with { Reason = WasapiDriftReason.Waiting }, pair);
        measured++;
        current = current with { CorrectionInterval = estimate, ClockAnchorCut100ns = anchorCut };
        if (!apply) return Record(current with { Reason = WasapiDriftReason.Observed }, pair);
        if (estimate.Value.Width > MaximumWidthPpm || current.MinimumMeanFrames is not { } minimum ||
            current.MaximumMeanFrames is not { } maximum || capture.FifoPrimingFrames == 0)
            return Record(current with { Reason = WasapiDriftReason.InsufficientPrecision });
        if (tracked == 0) controller.Reset(previous!.Cut100ns);
        var update = controller.UpdateBounded(lastCut, minimum, maximum, estimate.Value);
        if (update.State == DriftControlState.ReprimeRequired)
            return Record(current with { Reason = WasapiDriftReason.FifoBoundary });
        tracked++;
        return Record(current with { Reason = update.State == DriftControlState.Saturated
            ? WasapiDriftReason.Saturated : WasapiDriftReason.Tracking, RequestedPpm = update.CorrectionPpm });
    }

    private PpmInterval? ObservePair(WasapiDriftPair pair, long cut, out long anchorCut)
    {
        while (count > 1 && cut - anchors[(head + 1) % anchors.Length].Cut >= WasapiDriftMath.Window100ns)
        {
            head = (head + 1) % anchors.Length;
            count--;
        }
        anchorCut = count > 0 ? anchors[head].Cut : cut;
        PpmInterval? estimate = null;
        if (cut - anchorCut >= WasapiDriftMath.Window100ns)
            estimate = WasapiDriftMath.Estimate(anchors[head].Pair, pair, inputRate, outputRate, uncertainty);
        int tail = (head + count) % anchors.Length;
        if (count == 0 || cut - anchors[(tail + anchors.Length - 1) % anchors.Length].Cut >= Cadence100ns)
        {
            if (count == anchors.Length) { head = (head + 1) % anchors.Length; count--; }
            anchors[tail] = (cut, pair);
            count++;
        }
        return estimate;
    }

    private static bool Fresh(WasapiDriftPoint point, long cut) =>
        cut >= 0 && point.Qpc100ns != 0 && point.Qpc100ns <= (ulong)cut &&
        (ulong)cut - point.Qpc100ns <= WasapiDriftMath.MaxAge100ns;

    private WasapiDriftSample Record(WasapiDriftSample sample, WasapiDriftPair? eligible = null)
    {
        if (apply && sample.RequestedPpm is null)
        {
            if (sample.Reason is WasapiDriftReason.HistoryLost or WasapiDriftReason.FifoBoundary or WasapiDriftReason.EvidenceExpired ||
                (tracked != 0 && sample.Reason is not WasapiDriftReason.CheckpointUnavailable))
                MustStop = true;
            else if (tracked == 0 && sample.Cut100ns - startedAt >= Warmup100ns)
            {
                sample = sample with { Reason = WasapiDriftReason.StartupExpired };
                MustStop = true;
            }
        }
        observation?.Observe(sample, eligible);
        trace[(int)(samples++ % trace.Length)] = Last = sample;
        return sample;
    }

    public WasapiDriftReport Snapshot()
    {
        var recent = new WasapiDriftSample[(int)Math.Min(samples, trace.Length)];
        for (int i = 0; i < recent.Length; i++)
            recent[i] = trace[(int)((samples - recent.Length + i) % trace.Length)];
        return new(apply, uncertainty, samples, available, measured, tracked, startupUnderruns, Last.Reason,
            Array.AsReadOnly(recent), observation?.Snapshot());
    }
}
