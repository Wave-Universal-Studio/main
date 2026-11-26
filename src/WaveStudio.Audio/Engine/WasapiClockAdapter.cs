namespace WaveStudio.Audio.Engine;

[Flags]
internal enum TransportPacketFlags { None = 0, Discontinuity = 1, Silent = 2, TimestampError = 4 }
internal enum NativeClockQuality { Unavailable, Accurate, Uncertain }
internal enum NativePositionUnit { Frames, DeviceUnits }
internal readonly record struct NativeClockSample(TransportEndpoint Endpoint, ulong Position, ulong Frequency,
    ulong Qpc100ns, NativePositionUnit Unit, NativeClockQuality Quality, TransportPacketFlags Flags = TransportPacketFlags.None);
internal enum NativeClockResult { Valid, InvalidIdentity, Unavailable, TimestampError, Discontinuity, InvalidTime, InvalidPosition, Stalled, Overflow, RequiresReset, Provisional, NominalEvidenceUnavailable }

// Não faz chamadas nativas de clock. O chamador deve fornecer evidências verificadas de precisão e unidade.
internal sealed class WasapiClockAdapter
{
    private readonly TransportEndpoint endpoint;
    private readonly int rate;
    private readonly AudioClock clock;
    private readonly WasapiCaptureTiming? captureTiming;
    private bool anchored;
    private ulong nativePosition, nativeFrequency, qpc, lastProgressQpc, remainder;
    private NativePositionUnit unit;
    private long frames;
    public bool RequiresReset { get; private set; }
    public AudioClock Clock => clock;
    public WasapiClockAdapter(TransportEndpoint endpoint, int rate, WasapiCaptureTiming? captureTiming = null)
    {
        endpoint.Validate();
        if (rate is not (44100 or 48000)) throw new ArgumentOutOfRangeException(nameof(rate));
        if (captureTiming is not null && endpoint.Direction != EndpointDirection.Capture)
            throw new ArgumentException("A política temporal é exclusiva da captura.", nameof(captureTiming));
        this.endpoint = endpoint; this.rate = rate; this.captureTiming = captureTiming;
        // Identidade de tamanho limitado para o estimador. A identidade nativa é verificada por inteiro antes da conversão.
        string identity = ConvertIdentity(endpoint);
        clock = new(identity, endpoint.Generation, rate, "WASAPI-QPC-100ns", 10000000);
    }
    public NativeClockResult Convert(NativeClockSample sample, long now100ns, out ClockObservation observation) =>
        Convert(sample, now100ns, out observation, out _);

    public NativeClockResult Convert(NativeClockSample sample, long now100ns, out ClockObservation observation,
        out NativeClockValidation validation)
    {
        observation = default;
        validation = new(sample.Unit, sample.Quality, sample.Flags, now100ns, SampleAge(sample.Qpc100ns, now100ns),
            anchored ? new(unit, nativeFrequency, nativePosition, qpc, lastProgressQpc, frames, remainder) : null);
        if (sample.Endpoint != endpoint)
        {
            validation.Reason = NativeClockReason.IdentityMismatch;
            return NativeClockResult.InvalidIdentity;
        }
        if (RequiresReset)
        {
            validation.Reason = NativeClockReason.ResetRequired;
            return NativeClockResult.RequiresReset;
        }
        if ((sample.Flags & TransportPacketFlags.Discontinuity) != 0)
            return Reject(NativeClockResult.Discontinuity, NativeClockReason.PacketDiscontinuity, ref validation);
        if ((sample.Flags & TransportPacketFlags.TimestampError) != 0)
            return Reject(NativeClockResult.TimestampError, NativeClockReason.PacketTimestampError, ref validation);
        if ((sample.Flags & ~(TransportPacketFlags.Discontinuity | TransportPacketFlags.TimestampError | TransportPacketFlags.Silent)) != 0)
            return Reject(NativeClockResult.Unavailable, NativeClockReason.UnknownFlags, ref validation);
        if (sample.Quality != NativeClockQuality.Accurate)
            return Reject(NativeClockResult.Unavailable, NativeClockReason.InaccurateReading, ref validation);
        if (sample.Frequency == 0)
            return Reject(NativeClockResult.Unavailable, NativeClockReason.ZeroFrequency, ref validation);
        if (sample.Unit is not (NativePositionUnit.Frames or NativePositionUnit.DeviceUnits))
            return Reject(NativeClockResult.Unavailable, NativeClockReason.UnsupportedUnit, ref validation);
        if (sample.Qpc100ns > long.MaxValue)
            return Reject(NativeClockResult.InvalidTime, NativeClockReason.QpcOutOfRange, ref validation);
        if (now100ns < 0)
            return Reject(NativeClockResult.InvalidTime, NativeClockReason.NegativeNow, ref validation);
        if (sample.Qpc100ns > (ulong)now100ns)
            return Reject(NativeClockResult.InvalidTime, NativeClockReason.FutureTimestamp, ref validation);
        if ((ulong)now100ns - sample.Qpc100ns > 5000000)
            return Reject(NativeClockResult.InvalidTime, NativeClockReason.StaleTimestamp, ref validation);
        if (anchored)
        {
            validation.DeltaQpc100ns = (long)sample.Qpc100ns - (long)qpc;
            if (sample.Qpc100ns <= qpc)
                return Reject(NativeClockResult.InvalidTime, NativeClockReason.NonIncreasingTimestamp, ref validation);
        }
        ulong frequency = sample.Unit == NativePositionUnit.Frames ? (ulong)rate : sample.Frequency;
        if (sample.Unit == NativePositionUnit.Frames && sample.Frequency != (ulong)rate)
            return Reject(NativeClockResult.Unavailable, NativeClockReason.FrameFrequencyMismatch, ref validation);
        if (anchored)
        {
            if (sample.Unit != unit)
                return Reject(NativeClockResult.InvalidPosition, NativeClockReason.UnitChanged, ref validation);
            if (frequency != nativeFrequency)
                return Reject(NativeClockResult.InvalidPosition, NativeClockReason.FrequencyChanged, ref validation);
            validation.DeltaPosition = (Int128)sample.Position - nativePosition;
            if (sample.Position < nativePosition)
                return Reject(NativeClockResult.InvalidPosition, NativeClockReason.PositionRegressed, ref validation);
            if (sample.Position == nativePosition && sample.Qpc100ns - lastProgressQpc > 5000000)
                return Reject(NativeClockResult.Stalled, NativeClockReason.ProgressExpired, ref validation);

            UInt128 delta = (UInt128)(sample.Position - nativePosition) * (uint)rate + remainder;
            UInt128 advance = delta / frequency;
            validation.AdvancedFrames = advance;
            if (advance > long.MaxValue)
                return Reject(NativeClockResult.Overflow, NativeClockReason.AdvanceOverflow, ref validation);
            if (advance > (UInt128)(ulong)(long.MaxValue - frames))
                return Reject(NativeClockResult.Overflow, NativeClockReason.TotalFramesOverflow, ref validation);
            double seconds = (sample.Qpc100ns - qpc) / 10000000d;
            validation.IntervalSeconds = seconds;
            if (seconds > 2)
                return Reject(NativeClockResult.InvalidPosition, NativeClockReason.IntervalTooLong, ref validation);
            double expected = rate * seconds;
            double error = Math.Abs((double)advance - expected);
            double limit = NominalFrameTolerance(expected);
            validation.ExpectedFrames = expected;
            validation.AbsoluteErrorFrames = error;
            validation.ErrorLimitFrames = limit;
            if (captureTiming is null)
            {
                if (error > limit)
                    return Reject(NativeClockResult.InvalidPosition, NativeClockReason.AdvanceOutsideTolerance, ref validation);
            }
            else
            {
                var reason = captureTiming.ObserveClock(sample.Qpc100ns, (ulong)(frames + (long)advance), ref validation);
                if (reason != NativeClockReason.None)
                    return Reject(NativeClockResult.InvalidPosition, reason, ref validation);
            }
            frames += (long)advance;
            remainder = (ulong)(delta % frequency);
            validation.RemainderAfter = remainder;
            if (sample.Position != nativePosition) lastProgressQpc = sample.Qpc100ns;
        }
        else
        {
            captureTiming?.ObserveClock(sample.Qpc100ns, 0, ref validation);
            anchored = true;
            lastProgressQpc = sample.Qpc100ns;
        }
        nativePosition = sample.Position; nativeFrequency = frequency; qpc = sample.Qpc100ns; unit = sample.Unit;
        // A evidência de taxa segue rejeitada. A observação bruta só passou pelas regras do modo nominal.
        if (validation.Reason == NativeClockReason.WindowRateOutsideTolerance) return NativeClockResult.NominalEvidenceUnavailable;
        if (validation.TemporalProvisional) return NativeClockResult.Provisional;
        if (captureTiming is null) observation = new(clock, frames, (long)qpc, ClockQuality.Monotonic);
        return NativeClockResult.Valid;
    }
    private NativeClockResult Reject(NativeClockResult result, NativeClockReason reason, ref NativeClockValidation validation)
    {
        validation.Reason = reason;
        RequiresReset = true;
        return result;
    }

    internal static double NominalFrameTolerance(double expectedFrames) => expectedFrames * .005 + 2;

    internal static long? SampleAge(ulong timestamp, long now100ns) =>
        now100ns >= 0 && timestamp <= long.MaxValue ? now100ns - (long)timestamp : null;

    public NativeClockResult Convert(TransportEndpoint source, WasapiClockReading reading, long now100ns,
        out ClockObservation observation, TransportPacketFlags flags = 0) => Convert(reading.ToSample(source, flags), now100ns, out observation);
    private static string ConvertIdentity(TransportEndpoint endpoint) =>
        System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{endpoint.Direction}:{endpoint.Id}")));
}
