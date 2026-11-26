using System.Text.Json.Serialization;

namespace WaveStudio.Audio.Engine;

[JsonConverter(typeof(JsonStringEnumConverter<WasapiDriftObservationStatus>))]
internal enum WasapiDriftObservationStatus { InsufficientEvidence, IncludesZero, Positive, Negative }

internal readonly record struct WasapiDriftObservationWindow(long FirstCut100ns, long LastCut100ns,
    WasapiDriftPair First, WasapiDriftPair Last, ulong Overlap100ns, PpmInterval Interval);

internal sealed record WasapiDriftObservationReport(long Origin100ns, long CoherentCuts, long LastCoherentCut100ns,
    long MaximumCutGap100ns, long HistoryLosses, long RollingWindows, long? FirstRollingCut100ns, long? LastRollingCut100ns,
    long ContinuousSegments, ulong LongestEligibleOverlap100ns, long RejectedLongWindows,
    long? MinimumQueuedFrames, long? MaximumQueuedFrames,
    bool CorrectionSeen, WasapiDriftObservationWindow? LongestWindow)
{
    public long RollingWindowSpan100ns => LastRollingCut100ns - FirstRollingCut100ns ?? 0;
    public bool RollingCoverageConfirmed => CoherentCuts >= 60 && RollingWindows >= 10 &&
        RollingWindowSpan100ns >= (long)WasapiDriftObservation.MinimumOverlap100ns;
    public WasapiDriftObservationStatus Status => !RollingCoverageConfirmed || LongestWindow is not { } window
        ? WasapiDriftObservationStatus.InsufficientEvidence
        : window.Interval.Minimum > 0 ? WasapiDriftObservationStatus.Positive :
            window.Interval.Maximum < 0 ? WasapiDriftObservationStatus.Negative : WasapiDriftObservationStatus.IncludesZero;
    public bool WithinControllerWidth => RollingCoverageConfirmed &&
        LongestWindow is { Interval.Width: <= WasapiDriftControl.MaximumWidthPpm };

    public bool CheckpointCoverageConfirmed(long actualDuration100ns)
    {
        long terminalGap = actualDuration100ns - (LastCoherentCut100ns - Origin100ns);
        return CoherentCuts >= 60 && HistoryLosses == 0 && MaximumCutGap100ns < WasapiCheckpointLedger.MaxAcknowledgmentAge100ns &&
            terminalGap >= 0 && terminalGap < WasapiCheckpointLedger.MaxAcknowledgmentAge100ns;
    }
}

// Registra a cobertura inteira e conserva a janela corroborada mais longa de uma época contínua.
// O orçamento por ponta é o mesmo da janela curta. Nenhuma média de janelas presume erros independentes.
internal sealed class WasapiDriftObservation(int inputRate, int outputRate, WasapiTimingUncertainty uncertainty, long origin)
{
    public const ulong MinimumOverlap100ns = 40_000_000;
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(120);
    private readonly long startedAt = origin;
    private (long Cut, WasapiDriftPair Pair)? anchor;
    private long lastEligibleCut, coherentCuts, lastCoherentCut = origin, maxCutGap, historyLosses;
    private long rollingWindows, segments, rejected;
    private ulong longestEligibleOverlap;
    private long? firstRollingCut, lastRollingCut, minimumQueued, maximumQueued;
    private bool correctionSeen;
    private WasapiDriftObservationWindow? longest;

    public void Observe(WasapiDriftSample sample, WasapiDriftPair? eligible)
    {
        long cut = sample.Cut100ns;
        if (sample.CheckpointStatus == WasapiCheckpointStatus.Available)
        {
            coherentCuts++;
            maxCutGap = Math.Max(maxCutGap, cut - lastCoherentCut);
            lastCoherentCut = cut;
            if (sample.Capture.Evidence.FifoPrimingFrames > 0)
            {
                minimumQueued = Math.Min(minimumQueued ?? sample.QueuedFrames, sample.QueuedFrames);
                maximumQueued = Math.Max(maximumQueued ?? sample.QueuedFrames, sample.QueuedFrames);
            }
        }
        if (sample.CorrectionInterval is not null)
        {
            rollingWindows++;
            firstRollingCut ??= cut;
            lastRollingCut = cut;
        }
        correctionSeen |= sample.RequestedPpm is not null || sample.Capture.Evidence.AppliedPpm != 0 ||
            sample.Capture.Evidence.AppliedCommand100ns != 0 || sample.Capture.Evidence.AppliedAt100ns != 0;
        if (sample.Reason == WasapiDriftReason.HistoryLost) historyLosses++;
        if (sample.Reason is WasapiDriftReason.HistoryLost or WasapiDriftReason.PhaseChanged or WasapiDriftReason.EvidenceExpired ||
            cut - lastEligibleCut > WasapiDriftMath.MaxAge100ns)
            anchor = null;
        if (eligible is not { } pair || !sample.Capture.Evidence.CaptureWindows) return;

        lastEligibleCut = cut;
        if (anchor is not { } start || start.Pair.CaptureEpoch != pair.CaptureEpoch || start.Pair.RenderEpoch != pair.RenderEpoch)
        {
            anchor = (cut, pair);
            segments++;
            return;
        }

        ulong overlap = WasapiDriftMath.Overlap100ns(start.Pair, pair);
        longestEligibleOverlap = Math.Max(longestEligibleOverlap, overlap);
        if (overlap < MinimumOverlap100ns) return;
        if (WasapiDriftMath.EstimateObservation(start.Pair, pair, inputRate, outputRate, uncertainty) is not { } interval)
        {
            rejected++;
            anchor = null;
            return;
        }
        if (longest is null || overlap > longest.Value.Overlap100ns)
            longest = new(start.Cut, cut, start.Pair, pair, overlap, interval);
    }

    public WasapiDriftObservationReport Snapshot() => new(startedAt, coherentCuts, lastCoherentCut, maxCutGap, historyLosses,
        rollingWindows, firstRollingCut, lastRollingCut, segments, longestEligibleOverlap, rejected,
        minimumQueued, maximumQueued, correctionSeen, longest);
}
