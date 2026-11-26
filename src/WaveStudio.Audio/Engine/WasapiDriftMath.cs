namespace WaveStudio.Audio.Engine;

internal readonly record struct WasapiDriftPoint(ulong Position, ulong Frequency, ulong Qpc100ns);
internal readonly record struct WasapiDriftPair(WasapiDriftPoint Capture, WasapiDriftPoint Render,
    WasapiDriftPoint Packet, long CaptureEpoch, long RenderEpoch);
internal readonly record struct WasapiTimingUncertainty(double CaptureFrames, double RenderFrames, double PacketFrames);
internal readonly record struct PpmInterval(double Minimum, double Maximum)
{
    public double Width => Maximum - Minimum;
    public double NearestZero => Minimum > 0 ? Minimum : Maximum < 0 ? Maximum : 0;
}

internal static class WasapiDriftMath
{
    public const long Window100ns = 30_000_000;
    public const long MaxAge100ns = 5_000_000;

    public static PpmInterval? Estimate(WasapiDriftPair first, WasapiDriftPair last,
        int inputRate, int outputRate, WasapiTimingUncertainty uncertainty) =>
        Estimate(first, last, inputRate, outputRate, uncertainty, Window100ns - MaxAge100ns, Window100ns + 2 * MaxAge100ns);

    public static PpmInterval? EstimateObservation(WasapiDriftPair first, WasapiDriftPair last,
        int inputRate, int outputRate, WasapiTimingUncertainty uncertainty) =>
        Estimate(first, last, inputRate, outputRate, uncertainty, WasapiDriftObservation.MinimumOverlap100ns,
            (ulong)(WasapiDriftObservation.MaximumDuration.Ticks + MaxAge100ns));

    public static ulong Overlap100ns(WasapiDriftPair first, WasapiDriftPair last)
    {
        ulong overlapStart = Math.Max(Math.Max(first.Capture.Qpc100ns, first.Render.Qpc100ns), first.Packet.Qpc100ns);
        ulong overlapEnd = Math.Min(Math.Min(last.Capture.Qpc100ns, last.Render.Qpc100ns), last.Packet.Qpc100ns);
        return overlapEnd > overlapStart ? overlapEnd - overlapStart : 0;
    }

    private static PpmInterval? Estimate(WasapiDriftPair first, WasapiDriftPair last,
        int inputRate, int outputRate, WasapiTimingUncertainty uncertainty, ulong minimumSpan, ulong maximumSpan)
    {
        if (first.CaptureEpoch != last.CaptureEpoch || first.RenderEpoch != last.RenderEpoch ||
            Overlap100ns(first, last) < minimumSpan) return null;

        var capture = RelativeRate(first.Capture, last.Capture, inputRate, uncertainty.CaptureFrames, minimumSpan, maximumSpan);
        var packet = RelativeRate(first.Packet, last.Packet, inputRate, uncertainty.PacketFrames, minimumSpan, maximumSpan);
        var render = RelativeRate(first.Render, last.Render, outputRate, uncertainty.RenderFrames, minimumSpan, maximumSpan);
        if (capture is not { } c || packet is not { } p || render is not { } r ||
            c.Minimum > p.Maximum || p.Minimum > c.Maximum) return null;

        // O pacote corrobora a captura. Mantemos o intervalo do clock sem supor erros independentes.
        return new((r.Minimum / c.Maximum - 1) * 1e6, (r.Maximum / c.Minimum - 1) * 1e6);
    }

    private static PpmInterval? RelativeRate(WasapiDriftPoint first, WasapiDriftPoint last, int rate, double uncertainty,
        ulong minimumSpan, ulong maximumSpan)
    {
        if (first.Qpc100ns == 0 || last.Qpc100ns <= first.Qpc100ns || last.Position <= first.Position ||
            first.Frequency == 0 || first.Frequency != last.Frequency ||
            !double.IsFinite(uncertainty) || uncertainty < 0) return null;
        ulong duration = last.Qpc100ns - first.Qpc100ns;
        if (duration < minimumSpan || duration > maximumSpan) return null;
        double frames = (double)((UInt128)(last.Position - first.Position) * (uint)rate) / first.Frequency;
        double expected = rate * (duration / 10_000_000d);
        double error = 2 * uncertainty;
        return frames > error ? new((frames - error) / expected, (frames + error) / expected) : null;
    }
}
