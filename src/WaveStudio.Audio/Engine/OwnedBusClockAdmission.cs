namespace WaveStudio.Audio.Engine;

internal enum OwnedBusClockFailure { None, IncompleteCut, MissingTime, FutureTime, StaleTime, SampleGap, UnconfirmedProgress, CutSkew, InvalidReading, NominalRate }

// Cada posição pertence ao seu dispositivo. Somente o QPC permite comparar a idade das leituras.
internal sealed class OwnedBusClockAdmission
{
    internal const long MaximumGap100ns = 500_000;
    internal const ulong Block100ns = 100_000;
    private readonly Lane[] lanes;
    private bool anchored;

    internal bool Admitted { get; private set; }
    internal OwnedBusClockFailure Failure { get; private set; }
    internal NativeClockReason NativeReason { get; private set; }

    internal OwnedBusClockAdmission(ReadOnlySpan<OwnedBusCaptureSlot> captures, ReadOnlySpan<OwnedBusRenderSlot> renders, int renderRate)
    {
        lanes = new Lane[captures.Length + renders.Length];
        int index = 0;
        foreach (var capture in captures)
            lanes[index++] = new(capture.Source.Endpoint, capture.Source.Format.SampleRate);
        foreach (var render in renders)
            lanes[index++] = new(render.Endpoint, renderRate);
    }

    internal bool Observe(ReadOnlySpan<NativeClockSample> samples, long now100ns)
    {
        if (Failure != OwnedBusClockFailure.None) return false;
        if (samples.Length != lanes.Length) return Reject(OwnedBusClockFailure.IncompleteCut);
        ulong oldest = ulong.MaxValue, newest = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];
            ref var lane = ref lanes[i];
            if (sample.Qpc100ns == 0) return Reject(OwnedBusClockFailure.MissingTime);
            if (sample.Qpc100ns > (ulong)now100ns) return Reject(OwnedBusClockFailure.FutureTime);
            if ((ulong)now100ns - sample.Qpc100ns > MaximumGap100ns)
                return Reject(OwnedBusClockFailure.StaleTime);
            if (anchored && sample.Qpc100ns > lane.Timestamp && sample.Qpc100ns - lane.Timestamp > MaximumGap100ns)
                return Reject(OwnedBusClockFailure.SampleGap);

            var result = lane.Adapter.Convert(sample, now100ns, out var observation, out var validation);
            if (result != NativeClockResult.Valid)
            {
                NativeReason = validation.Reason;
                return Reject(OwnedBusClockFailure.InvalidReading);
            }
            if (anchored && observation.FramePosition <= lane.Frames)
                return Reject(OwnedBusClockFailure.UnconfirmedProgress);

            if (anchored)
            {
                double expectedFrames = lane.Adapter.Clock.NominalRate * ((sample.Qpc100ns - lane.Anchor) / 10_000_000d);
                // A margem fixa vale uma vez para o acumulado, sem reaplicá-la a cada bloco.
                if (Math.Abs(observation.FramePosition - expectedFrames) > WasapiClockAdapter.NominalFrameTolerance(expectedFrames))
                    return Reject(OwnedBusClockFailure.NominalRate);
            }
            else lane.Anchor = sample.Qpc100ns;

            lane.Timestamp = sample.Qpc100ns;
            lane.Frames = observation.FramePosition;
            oldest = Math.Min(oldest, sample.Qpc100ns);
            newest = Math.Max(newest, sample.Qpc100ns);
        }
        if (newest - oldest > Block100ns) return Reject(OwnedBusClockFailure.CutSkew);
        Admitted = anchored;
        anchored = true;
        return true;
    }

    private bool Reject(OwnedBusClockFailure failure)
    {
        Failure = failure;
        Admitted = false;
        return false;
    }

    private struct Lane(TransportEndpoint endpoint, int rate)
    {
        internal readonly WasapiClockAdapter Adapter = new(endpoint, rate);
        internal ulong Anchor, Timestamp;
        internal long Frames;
    }
}
