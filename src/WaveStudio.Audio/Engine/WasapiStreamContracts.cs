using System.Diagnostics;

namespace WaveStudio.Audio.Engine;

// Interface de chamadas nativas que admite um substituto de teste. Cada objeto é adquirido, usado e liberado por uma thread MTA proprietária e estável.
internal interface IWasapiStreamClient : IWasapiPreparationClient
{
    int Start();
    int Stop();
    void RevalidateFormat(WasapiPcm expected);
    uint ReadPadding();
    IWasapiCapturePackets OpenCapture();
    IWasapiRenderPackets OpenRender();
}
internal interface IWasapiBoundStreamClient : IWasapiStreamClient
{
    TransportEndpoint Endpoint { get; }
    WasapiPcm PreparedFormat { get; }
    IDisposable BorrowBusServices();
}

internal interface IWasapiNativeBoundStreamClient : IWasapiBoundStreamClient { }
internal interface IWasapiNativePacketService { }
internal interface IWasapiBusOwner : IDisposable
{
    bool ResourcesRetained { get; }
}

internal readonly record struct WasapiCaptureBuffer(int HResult, nint Data, uint Frames, uint Flags, ulong Position, ulong Qpc100ns);
internal interface IWasapiCapturePackets : IDisposable
{
    uint NextPacketSize();
    WasapiCaptureBuffer GetBuffer();
    int ReleaseBuffer(uint framesRead);
}
internal interface IWasapiRenderPackets : IDisposable
{
    nint GetBuffer(uint frames);
    int ReleaseBuffer(uint framesWritten, uint flags);
}
internal static class WasapiTime
{
    // O QPC do Windows é convertido para unidades de 100 ns e usado apenas para idade e controle. Não substitui a posição do áudio.
    public static long Now100ns() => checked((long)((UInt128)(ulong)Stopwatch.GetTimestamp() * 10000000 / (ulong)Stopwatch.Frequency));
}

internal sealed class WasapiRunningClock(TransportEndpoint endpoint, int rate, long started100ns,
    WasapiCaptureTiming? captureTiming = null)
{
    private const int HistoryCapacity = 64;
    private readonly WasapiClockAdapter adapter = new(endpoint, rate, captureTiming);
    private readonly NativeClockTrace[] readings = new NativeClockTrace[HistoryCapacity];
    private long readingCount;
    private NativeClockTrace? lastAccepted, rejection;
    private ulong position;
    private bool anchored;
    public long MovementSamples { get; private set; }
    public NativeClockResult Observe(WasapiClockReading reading, long now100ns)
    {
        var sample = reading.ToSample(endpoint);
        NativeClockResult result;
        NativeClockValidation validation;
        if (reading.Quality == NativeClockQuality.Accurate && !anchored && (reading.Position == 0 || reading.Qpc100ns == 0))
        {
            bool inStartupWindow = now100ns - started100ns <= 5000000;
            result = inStartupWindow ? NativeClockResult.Stalled : NativeClockResult.InvalidPosition;
            validation = new(sample.Unit, sample.Quality, sample.Flags, now100ns, WasapiClockAdapter.SampleAge(sample.Qpc100ns, now100ns), null)
            {
                Reason = !inStartupWindow ? NativeClockReason.StartupExpired
                    : reading.Position == 0 ? NativeClockReason.StartupWaiting
                    : NativeClockReason.StartupMissingTimestamp
            };
        }
        else result = adapter.Convert(sample, now100ns, out _, out validation);

        var entry = new NativeClockTrace(readingCount + 1, (Int128)now100ns - started100ns, reading, result, validation);
        readings[(int)(readingCount % HistoryCapacity)] = entry;
        readingCount++;

        if (result == NativeClockResult.Valid)
        {
            if (anchored && reading.Position > position) MovementSamples++;
            anchored = true;
            position = reading.Position;
            lastAccepted = entry;
        }
        else if (result == NativeClockResult.Provisional) position = reading.Position;
        else if (result == NativeClockResult.NominalEvidenceUnavailable)
        {
            position = reading.Position;
            rejection ??= entry;
        }
        else if (validation.Reason != NativeClockReason.StartupWaiting) rejection ??= entry;
        return result;
    }

    // O coordenador chama este método após o término da thread proprietária.
    public NativeClockHistory Snapshot()
    {
        int length = (int)Math.Min(readingCount, HistoryCapacity);
        long overwritten = readingCount - length;
        var ordered = new NativeClockTrace[length];
        for (int i = 0; i < length; i++)
            ordered[i] = readings[(int)((overwritten + i) % HistoryCapacity)];
        return new(readingCount, overwritten, Array.AsReadOnly(ordered), lastAccepted, rejection);
    }
}

internal sealed record WasapiRoleSnapshot(bool Initialized, bool StartAttempted, bool Started, bool StopConfirmed,
    bool Released, int OwnerThreadId, uint CapacityFrames, long DefaultPeriod100ns, long Latency100ns,
    long PacketsAcquired, long PacketsReleased, long FramesAcquired, long FramesReleased, long PrimedFrames,
    uint Padding, long MovementSamples, WasapiClockReading? LastClock, uint PacketFlags,
    ulong PacketPosition, ulong PacketQpc100ns, bool BufferInFlight, string? Error, string? CleanupError,
    int? StartHResult, int? StopHResult, int? AcquireHResult, int? ReleaseHResult, NativeClockResult? ClockStatus,
    long PacketsAccepted, long FramesAccepted, long StartupDiscardedPackets, long StartupDiscardedFrames,
    uint StartupDiscardFlags, ulong StartupDiscardPosition, ulong StartupDiscardQpc100ns, NativeClockHistory? ClockHistory,
    WasapiCaptureTimingSnapshot? CaptureTiming = null);

internal sealed record WasapiSilentSnapshot(WasapiRoleSnapshot Capture, WasapiRoleSnapshot Playback,
    bool Completed, bool ResourcesRetained, string? Failure, int? HResult,
    int FifoCapacityFrames, long ProducedFrames, long ReadFrames, long DroppedFrames, long UnderrunFrames, long Observation100ns, bool DurationReached)
{
    [System.Text.Json.Serialization.JsonIgnore] public bool NominalMode { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public bool PcmRenderSelected { get; init; }
    public bool AlwaysSilent => !PcmRenderSelected;
    public bool DriftControlEnabled => false;
    public bool MovementProven => Completed && Failure is null && Capture.MovementSamples >= 2 && Playback.MovementSamples >= 2;
    public bool TransportObserved => !NominalMode && DurationReached && MovementProven && (Capture.CaptureTiming is null or { Confirmed: true } &&
        (Capture.CaptureTiming is null || Capture.CaptureTiming.ContinuousPackets == Capture.PacketsAccepted)) &&
        Capture.Started && Playback.Started &&
        Capture.StopConfirmed && Playback.StopConfirmed && Capture.Released && Playback.Released &&
        Capture.PacketsAccepted > 0 && Capture.FramesAccepted > 0 && ProducedFrames > 0 && ReadFrames > 0 &&
        Capture.PacketsAccepted + Capture.StartupDiscardedPackets == Capture.PacketsReleased &&
        Capture.FramesAccepted + Capture.StartupDiscardedFrames == Capture.FramesReleased &&
        Playback.PrimedFrames == Playback.CapacityFrames &&
        Playback.FramesReleased > Playback.PrimedFrames &&
        Capture.PacketsAcquired == Capture.PacketsReleased && Playback.PacketsAcquired == Playback.PacketsReleased &&
        Capture.FramesAcquired == Capture.FramesReleased && Playback.FramesAcquired == Playback.FramesReleased;
}
