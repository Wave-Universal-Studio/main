using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiSilentTests
{
    internal const int Invalidated = unchecked((int)0x88890004);
    internal sealed class Calls
    {
        public readonly Role Input, Output;
        public readonly ConcurrentQueue<(bool Input, string Step, int Thread)> Log = new();
        public Calls(TransportRoute route) { Input = new(this, route.Capture, true); Output = new(this, route.Playback, false); }
        public IWasapiPreparationDevice Open(TransportEndpoint endpoint) => (endpoint.Direction == EndpointDirection.Capture ? Input : Output).Open(endpoint);
    }
    internal sealed class Role(Calls calls, WasapiEndpointInfo info, bool input)
    {
        public string? FailStep;
        public Action<string>? OnStep;
        public Action<uint, uint>? OnRenderSubmitted;
        public Func<int, WasapiClockReading>? ReadClockOverride;
        public Func<uint>? PacketSizeOverride, RenderPaddingOverride;
        public Func<WasapiCaptureBuffer>? CaptureBufferOverride;
        public uint Capacity = 1056, CaptureFlags = 2;
        public uint? PacketFrames, AdvertisedFrames, Padding, PaddingAfterStart, StartupFlags;
        public int PacketLimit = int.MaxValue;
        public bool EmptyBeforeFirst, NominalPacketCadence;
        public ulong SecondPacketOffset;
        public readonly ConcurrentQueue<uint> CaptureReleases = new();
        private int packetCount;
        public ulong? PacketQpc;
        public readonly ConcurrentQueue<uint> RenderRequests = new();
        public int ClockResult, ReleaseResult, StopResult, StartResult;
        public bool NullData, EmptyPacket, Stagnant, RegressClock, ChangedFormat;
        public int Owner, Stops, DisposeCalls;
        public bool BufferHeld;
        public long StartTime, AcquiredFrames, ReleasedFrames;
        public bool Started;
        private ulong packetPosition;
        private int clockSamples;
        private bool packetDue = true;
        private uint borrowedCaptureFrames;
        private GCHandle memory;
        public readonly byte[] Data = Enumerable.Repeat((byte)0x5a, 4096 * 8).ToArray();
        private void Step(string step)
        {
            if (Owner == 0) Owner = Environment.CurrentManagedThreadId;
            Assert.Equal(Owner, Environment.CurrentManagedThreadId); Assert.Equal(ApartmentState.MTA, Thread.CurrentThread.GetApartmentState());
            calls.Log.Enqueue((input, step, Owner)); OnStep?.Invoke(step);
            if (step == FailStep) throw new WasapiCallException(step, Invalidated);
        }
        public IWasapiPreparationDevice Open(TransportEndpoint endpoint)
        {
            Step("Open"); Assert.Equal(info.Endpoint, endpoint); memory = GCHandle.Alloc(Data, GCHandleType.Pinned); return new Device(this, info);
        }
        private sealed class Device(Role role, WasapiEndpointInfo info) : IWasapiPreparationDevice
        {
            public void ValidateIdentity(TransportEndpoint endpoint) { role.Step("Identity"); Assert.Equal(info.Endpoint, endpoint); }
            public IWasapiPreparationClient Activate() { role.Step("Activate"); return new Client(role, info); }
            public void Dispose() { role.Step("DisposeDevice"); role.memory.Free(); }
        }
        private sealed class Client(Role role, WasapiEndpointInfo info) : IWasapiStreamClient
        {
            public WasapiPcm ReadMixFormat() { role.Step("Mix"); return info.Mix!; }
            public void RequireExactSupport() => role.Step("Support");
            public (long, long) ReadPeriods() { role.Step("Periods"); return (100000, 30000); }
            public void Initialize() => role.Step("Initialize");
            public (uint, long) ReadBufferInfo() { role.Step("Capacity"); return (role.Capacity, 100000); }
            public uint ReadPadding()
            {
                role.Step("Padding");
                if (role.Started && role.RenderPaddingOverride is { } scripted) return scripted();
                return (role.Started ? role.PaddingAfterStart : null) ?? role.Padding ?? 0;
            }
            public IWasapiPreparationClock OpenClock() { role.Step("Clock"); return new Clock(role, info); }
            public IWasapiCapturePackets OpenCapture() { role.Step("CaptureService"); return new Capture(role); }
            public IWasapiRenderPackets OpenRender() { role.Step("RenderService"); return new Render(role); }
            public void RevalidateFormat(WasapiPcm expected)
            { role.Step("RevalidateFormat"); Assert.Equal(info.Mix, expected); if (role.ChangedFormat) throw new InvalidOperationException("Formato alterado"); }
            public int Start() { role.Step("Start"); role.Started = true; role.StartTime = WasapiTime.Now100ns(); return role.StartResult; }
            public int Stop() { role.Stops++; role.Step("Stop"); Assert.False(role.BufferHeld && role.ReleaseResult == 0); return role.StopResult; }
            public void Dispose() { role.DisposeCalls++; role.Step("DisposeClient"); Assert.False(role.BufferHeld); }
        }
        private sealed class Clock(Role role, WasapiEndpointInfo info) : IWasapiPreparationClock
        {
            public WasapiClockReading Read()
            {
                role.Step("ReadClock"); Assert.True(role.Started);
                if (role.ReadClockOverride is { } scripted) return scripted(role.clockSamples++);
                ulong qpc = (ulong)WasapiTime.Now100ns();
                ulong frequency = (ulong)info.Mix!.AverageBytesPerSecond;
                ulong position = (ulong)((UInt128)(qpc - (ulong)role.StartTime) * frequency / 10000000);
                if (role.Stagnant) position = 0;
                if (role.RegressClock && ++role.clockSamples > 1) position = 1;
                return new(0, frequency, role.ClockResult, position, qpc);
            }
            public void Dispose() => role.Step("DisposeClock");
        }
        private sealed class Capture(Role role) : IWasapiCapturePackets
        {
            public uint NextPacketSize()
            {
                role.Step("Next");
                if (role.PacketSizeOverride is { } scripted) return scripted();
                if (role.NominalPacketCadence)
                    return role.packetCount < role.PacketLimit && WasapiTime.Now100ns() >= role.StartTime + (role.packetCount + 1L) * 100000
                        ? role.PacketFrames ?? 480
                        : 0;
                if (!role.packetDue) { role.packetDue = true; return 0; }
                if (role.packetCount >= role.PacketLimit) return 0;
                role.packetDue = false;
                return role.AdvertisedFrames ?? role.PacketFrames ?? 48;
            }
            public WasapiCaptureBuffer GetBuffer()
            {
                role.Step("GetCapture"); Assert.False(role.BufferHeld);
                if (role.EmptyPacket || role.EmptyBeforeFirst)
                {
                    role.EmptyBeforeFirst = false;
                    return new(0x08890001, 0, 0, 0, 0, 0);
                }
                role.BufferHeld = true;
                if (role.CaptureBufferOverride is { } scripted)
                {
                    var buffer = scripted();
                    role.borrowedCaptureFrames = buffer.Frames;
                    role.AcquiredFrames += buffer.Frames;
                    return buffer with { Data = role.memory.AddrOfPinnedObject() };
                }
                uint frames = role.borrowedCaptureFrames = role.PacketFrames ?? 48;
                role.AcquiredFrames += frames;
                var packet = new WasapiCaptureBuffer(0, role.NullData ? 0 : role.memory.AddrOfPinnedObject(), frames,
                    role.packetCount++ == 0 ? role.StartupFlags ?? role.CaptureFlags : role.CaptureFlags,
                    role.packetPosition + (role.packetCount == 2 ? role.SecondPacketOffset : 0),
                    role.PacketQpc ?? (role.NominalPacketCadence ? (ulong)role.StartTime + (ulong)role.packetCount * 100000 : (ulong)WasapiTime.Now100ns()));
                role.packetPosition += frames;
                return packet;
            }
            public int ReleaseBuffer(uint framesRead)
            {
                role.Step("ReleaseCapture"); Assert.True(role.BufferHeld); Assert.True(framesRead == 0 || framesRead == role.borrowedCaptureFrames);
                role.CaptureReleases.Enqueue(framesRead);
                if (role.ReleaseResult == 0) role.BufferHeld = false; role.ReleasedFrames += framesRead; return role.ReleaseResult;
            }
            public void Dispose() { role.Step("DisposeCapture"); Assert.False(role.BufferHeld); }
        }
        private sealed class Render(Role role) : IWasapiRenderPackets
        {
            public nint GetBuffer(uint frames)
            {
                role.Step("GetRender"); Assert.False(role.BufferHeld); Assert.InRange(frames, 1u, role.Capacity);
                role.RenderRequests.Enqueue(frames);
                role.BufferHeld = true; role.AcquiredFrames += frames; return role.NullData ? 0 : role.memory.AddrOfPinnedObject();
            }
            public int ReleaseBuffer(uint framesWritten, uint flags)
            {
                role.Step("ReleaseRender"); Assert.True(role.BufferHeld);
                Assert.InRange(framesWritten, 1u, role.Capacity);
                if (role.OnRenderSubmitted is { } submitted) submitted(framesWritten, flags);
                else
                {
                    Assert.Equal(2u, flags);
                    Assert.Equal(-1, role.Data.AsSpan().IndexOfAnyExcept((byte)0x5a)); // As provas existentes não gravam amostras na saída WASAPI.
                }
                if (role.ReleaseResult == 0) role.BufferHeld = false; role.ReleasedFrames += framesWritten; return role.ReleaseResult;
            }
            public void Dispose() { role.Step("DisposeRender"); Assert.False(role.BufferHeld); }
        }
    }
    private static TimeSpan Duration => TimeSpan.FromMilliseconds(350);
    [Fact]
    public async Task SilentTransportPrimesBeforeRenderThenStartsCaptureOnStableOwnersAndReleasesInOrder()
    {
        var route = WasapiContractTests.Route(); var calls = new Calls(route); var guard = new TransportActivityGuard();
        await using var session = new WasapiSilentSession(route, guard, calls.Open);
        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);
        Assert.True(result.Completed); Assert.False(result.ResourcesRetained); Assert.Null(result.Failure); Assert.True(result.MovementProven); Assert.True(result.TransportObserved);
        Assert.True(result.AlwaysSilent); Assert.False(result.DriftControlEnabled); Assert.Equal(4096, result.FifoCapacityFrames);
        Assert.Equal(1056, result.Playback.PrimedFrames); Assert.True(result.ProducedFrames > 0); Assert.True(result.Playback.FramesReleased > 0);
        Assert.Equal(result.Capture.PacketsAcquired, result.Capture.PacketsReleased);
        Assert.Equal(result.Capture.PacketsReleased, result.Capture.PacketsAccepted);
        Assert.Equal(0, result.Capture.StartupDiscardedPackets);
        Assert.Equal(result.Playback.PacketsAcquired, result.Playback.PacketsReleased);
        Assert.NotEqual(calls.Input.Owner, calls.Output.Owner);
        var log = calls.Log.ToArray(); int prime = Array.FindIndex(log, c => !c.Input && c.Step == "ReleaseRender");
        int startRender = Array.FindIndex(log, c => !c.Input && c.Step == "Start"); int startCapture = Array.FindIndex(log, c => c.Input && c.Step == "Start");
        Assert.True(prime < startRender && startRender < startCapture);
        foreach (bool input in new[] { false, true })
        {
            var steps = log.Where(c => c.Input == input).Select(c => c.Step).ToArray();
            Assert.True(Array.IndexOf(steps, "ReadClock") > Array.IndexOf(steps, "Start"));
            Assert.Equal(new[] { input ? "DisposeCapture" : "DisposeRender", "DisposeClock", "DisposeClient", "DisposeDevice" }, steps.TakeLast(4));
        }
        using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic); Assert.NotNull(lease);
    }
    [Theory]
    [InlineData("Open")] [InlineData("Identity")] [InlineData("Activate")] [InlineData("Mix")] [InlineData("Support")]
    [InlineData("Periods")] [InlineData("Initialize")] [InlineData("Capacity")] [InlineData("Clock")]
    [InlineData("CaptureService")] [InlineData("RevalidateFormat")] [InlineData("Start")] [InlineData("Next")] [InlineData("GetCapture")]
    public async Task PartialCaptureFailureStopsBothAndReleasesOnTheirOwners(string step)
    {
        var route = WasapiContractTests.Route(); var calls = new Calls(route); calls.Input.FailStep = step;
        await using var session = new WasapiSilentSession(route, new(), calls.Open);
        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);
        Assert.True(result.Completed); Assert.False(result.ResourcesRetained); Assert.NotNull(result.Failure); Assert.Equal(Invalidated, result.HResult);
        Assert.False(result.MovementProven); if (result.Playback.StartAttempted) Assert.True(result.Playback.StopConfirmed);
        Assert.False(calls.Input.BufferHeld); Assert.False(calls.Output.BufferHeld);
    }
    [Theory]
    [InlineData(4u)] [InlineData(8u)] [InlineData(5u)] [InlineData(7u)] [InlineData(9u)]
    public async Task BadCaptureFlagsArePreservedAndPacketIsReleasedWithoutPayload(uint flags)
    {
        var route = WasapiContractTests.Route(); var calls = new Calls(route); calls.Input.CaptureFlags = flags; calls.Input.NullData = true;
        await using var session = new WasapiSilentSession(route, new(), calls.Open);
        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);
        Assert.NotNull(result.Failure); Assert.Equal(flags, result.Capture.PacketFlags); Assert.True(result.Completed);
        Assert.Equal(1, result.Capture.PacketsAcquired); Assert.Equal(1, result.Capture.PacketsReleased); Assert.Equal(0, result.Capture.FramesReleased);
        var rejection = Assert.IsType<WasapiCapturePacketRejection>(result.Capture.PacketRejection);
        Assert.Equal(WasapiCapturePacketRejectionReason.InvalidFlags, rejection.Reason);
        Assert.Equal(flags, rejection.Flags);
    }
    [Theory]
    [InlineData(1u, false)]
    [InlineData(3u, true)]
    public async Task DiscardsOnlyFirstMarkedPacketAndObservesSubsequentTransport(uint flags, bool afterEmpty)
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        calls.Input.StartupFlags = flags;
        calls.Input.NullData = flags == 3;
        calls.Input.EmptyBeforeFirst = afterEmpty;
        calls.Input.PacketFrames = 480;
        await using var session = new WasapiSilentSession(route, new(), calls.Open);

        var result = await session.RunAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        Assert.True(result.TransportObserved);
        Assert.Equal(1, result.Capture.StartupDiscardedPackets);
        Assert.Equal(480, result.Capture.StartupDiscardedFrames);
        Assert.Equal(flags, result.Capture.StartupDiscardFlags);
        Assert.Equal(flags | 2, result.Capture.PacketFlags);
        Assert.True(result.Capture.StartupDiscardQpc100ns > 0);
        Assert.Equal(result.Capture.PacketsAccepted + 1, result.Capture.PacketsReleased);
        Assert.Equal(result.Capture.FramesAccepted + 480, result.Capture.FramesReleased);
        Assert.Equal(result.Capture.PacketsReleased, calls.Input.CaptureReleases.Count);
        Assert.All(calls.Input.CaptureReleases, frames => Assert.Equal(480u, frames));
        Assert.InRange(result.ProducedFrames + result.DroppedFrames, 1, result.Capture.FramesAccepted);
    }

    [Theory]
    [InlineData(1u, 1u)]
    [InlineData(3u, 3u)]
    [InlineData(0u, 1u)]
    [InlineData(2u, 3u)]
    public async Task ASecondDiscontinuityFailsEvenInsideStartupWindow(uint first, uint second)
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        calls.Input.StartupFlags = first;
        calls.Input.CaptureFlags = second;
        calls.Input.PacketFrames = 480;
        await using var session = new WasapiSilentSession(route, new(), calls.Open);

        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);

        Assert.Equal("Capture", result.Failure);
        Assert.True(result.Completed);
        Assert.False(result.TransportObserved);
        Assert.Equal(first | second, result.Capture.PacketFlags);
        var rejection = Assert.IsType<WasapiCapturePacketRejection>(result.Capture.PacketRejection);
        Assert.Equal(WasapiCapturePacketRejectionReason.UnexpectedDiscontinuity, rejection.Reason);
        Assert.Equal(second, rejection.Flags);
        Assert.Equal(new uint[] { 480, 0 }, calls.Input.CaptureReleases.ToArray());
        Assert.Equal((first & 1) != 0 ? 1 : 0, result.Capture.StartupDiscardedPackets);
        Assert.Equal((first & 1) != 0 ? 0 : 1, result.Capture.PacketsAccepted);
        Assert.Equal((first & 1) != 0, result.ProducedFrames == 0);
    }

    [Theory]
    [InlineData("late")]
    [InlineData("mismatched-size")]
    [InlineData("oversized")]
    [InlineData("zero-size")]
    [InlineData("bad-pointer")]
    [InlineData("missing-time")]
    [InlineData("stale-time")]
    [InlineData("future-time")]
    public async Task InvalidInitialDiscontinuityReleasesZeroAndStops(string fault)
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        calls.Input.StartupFlags = 1;
        if (fault == "late") calls.Input.OnStep = step => { if (step == "GetCapture") Thread.Sleep(550); };
        if (fault == "mismatched-size") calls.Input.AdvertisedFrames = 49;
        if (fault == "oversized") calls.Input.PacketFrames = 2000;
        if (fault == "zero-size") { calls.Input.AdvertisedFrames = 48; calls.Input.PacketFrames = 0; }
        if (fault == "bad-pointer") calls.Input.NullData = true;
        if (fault == "missing-time") calls.Input.PacketQpc = 0;
        if (fault == "stale-time") calls.Input.PacketQpc = 1;
        if (fault == "future-time") calls.Input.PacketQpc = ulong.MaxValue;
        await using var session = new WasapiSilentSession(route, new(), calls.Open);

        var result = await session.RunAsync(TimeSpan.FromMilliseconds(900), TestContext.Current.CancellationToken);

        Assert.Equal("Capture", result.Failure);
        Assert.True(result.Completed);
        Assert.Equal(1u, result.Capture.PacketFlags);
        Assert.Equal(0u, Assert.Single(calls.Input.CaptureReleases));
        Assert.Equal(0, result.Capture.StartupDiscardedPackets);
        Assert.Equal(0, result.Capture.PacketsAccepted);
        Assert.Equal(0, result.ProducedFrames);
        var rejection = Assert.IsType<WasapiCapturePacketRejection>(result.Capture.PacketRejection);
        var expected = fault switch
        {
            "late" => WasapiCapturePacketRejectionReason.UnexpectedDiscontinuity,
            "mismatched-size" or "zero-size" => WasapiCapturePacketRejectionReason.SizeMismatch,
            "oversized" => WasapiCapturePacketRejectionReason.CapacityExceeded,
            "bad-pointer" => WasapiCapturePacketRejectionReason.MissingData,
            "missing-time" => WasapiCapturePacketRejectionReason.MissingTimestamp,
            "stale-time" => WasapiCapturePacketRejectionReason.StaleTimestamp,
            _ => WasapiCapturePacketRejectionReason.FutureTimestamp
        };
        Assert.Equal(expected, rejection.Reason);
        Assert.Equal(calls.Input.AdvertisedFrames ?? calls.Input.PacketFrames ?? 48u, rejection.AnnouncedFrames);
        Assert.Equal(calls.Input.PacketFrames ?? 48u, rejection.AcquiredFrames);
        Assert.Equal(1u, rejection.Flags);
        Assert.Equal(calls.Input.PacketQpc ?? result.Capture.PacketQpc100ns, rejection.Qpc100ns);
        Assert.True(rejection.CheckedAt100ns > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscardWithoutAcceptedPacketsCannotProveTransport(bool uncertainClock)
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        calls.Input.StartupFlags = 1;
        calls.Input.PacketFrames = 480;
        calls.Input.PacketLimit = 1;
        calls.Input.ClockResult = uncertainClock ? 1 : 0;
        await using var session = new WasapiSilentSession(route, new(), calls.Open);

        var result = await session.RunAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        Assert.True(result.Completed);
        Assert.Equal(480u, Assert.Single(calls.Input.CaptureReleases));
        Assert.Equal(1, result.Capture.StartupDiscardedPackets);
        Assert.Equal(0, result.Capture.PacketsAccepted);
        Assert.Equal(0, result.ProducedFrames);
        Assert.Equal(0, result.ReadFrames);
        Assert.False(result.TransportObserved);
        Assert.Equal(!uncertainClock, result.DurationReached);
        if (uncertainClock) Assert.Equal(1, result.Capture.LastClock!.Value.PositionHResult);
        else Assert.True(result.MovementProven);
    }

    [Theory]
    [InlineData("GetCapture", 0u)]
    [InlineData("ReleaseCapture", 480u)]
    public async Task CancellationPreservesFullOrZeroReleaseForInitialDiscontinuity(string step, uint expectedRelease)
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        calls.Input.StartupFlags = 1;
        calls.Input.PacketFrames = 480;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        calls.Input.OnStep = current => { if (current == step) cancel.Cancel(); };
        await using var session = new WasapiSilentSession(route, new(), calls.Open);

        var result = await session.RunAsync(Duration, cancel.Token);

        Assert.True(result.Completed);
        Assert.False(result.TransportObserved);
        Assert.Equal(expectedRelease, Assert.Single(calls.Input.CaptureReleases));
        Assert.Equal(expectedRelease, result.Capture.StartupDiscardedFrames);
        Assert.Equal(0, result.Capture.PacketsAccepted);
        Assert.Equal(0, result.ProducedFrames);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task SilentNullCaptureAndEmptyPacketRespectDifferentReleaseContracts(bool empty)
    {
        var route = WasapiContractTests.Route(); var calls = new Calls(route); calls.Input.NullData = true; calls.Input.EmptyPacket = empty;
        await using var session = new WasapiSilentSession(route, new(), calls.Open);
        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);
        Assert.Null(result.Failure); Assert.True(result.Completed);
        Assert.Equal(empty ? 0 : result.Capture.PacketsAcquired, result.Capture.PacketsReleased);
        Assert.Equal(empty ? 0 : result.Capture.FramesAcquired, result.Capture.FramesReleased);
        Assert.Equal(empty, result.Capture.PacketsAcquired == 0);
        if (empty) Assert.False(result.TransportObserved);
    }
    [Fact]
    public async Task CapturePayloadStaysInMemoryAndRenderUsesOnlyAvailableSpace()
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        calls.Input.CaptureFlags = 0;
        calls.Output.PaddingAfterStart = 480;
        await using var session = new WasapiSilentSession(route, new(), calls.Open);

        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);

        Assert.Null(result.Failure);
        Assert.True(result.Completed);
        Assert.True(result.ProducedFrames > 0);
        var requests = calls.Output.RenderRequests.ToArray();
        Assert.Equal(1056u, requests[0]);
        Assert.NotEmpty(requests.Skip(1));
        Assert.All(requests.Skip(1), frames => Assert.Equal(576u, frames));
    }

    [Theory]
    [InlineData("packet")] [InlineData("nullCapture")] [InlineData("nullRender")] [InlineData("padding")] [InlineData("capacity")] [InlineData("format")] [InlineData("timestamp")]
    public async Task InvalidBuffersFormatAndPaddingFailSafely(string fault)
    {
        var route = WasapiContractTests.Route(); var calls = new Calls(route);
        if (fault == "packet") calls.Input.PacketFrames = 2000;
        if (fault == "nullCapture") { calls.Input.CaptureFlags = 0; calls.Input.NullData = true; }
        if (fault == "nullRender") calls.Output.NullData = true;
        if (fault == "padding") calls.Output.Padding = 2000;
        if (fault == "capacity") calls.Output.Capacity = 5000;
        if (fault == "format") calls.Input.ChangedFormat = true;
        if (fault == "timestamp") calls.Input.PacketQpc = 1;
        await using var session = new WasapiSilentSession(route, new(), calls.Open);
        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);
        Assert.True(result.Completed); Assert.NotNull(result.Failure); Assert.False(calls.Input.BufferHeld); Assert.False(calls.Output.BufferHeld);
    }
    [Theory]
    [InlineData("uncertain")] [InlineData("failed")] [InlineData("stagnant")] [InlineData("regress")]
    public async Task InvalidMovingClockDoesNotBecomeAccurateTransport(string fault)
    {
        var route = WasapiContractTests.Route(); var calls = new Calls(route);
        calls.Input.ClockResult = fault == "uncertain" ? 1 : fault == "failed" ? Invalidated : 0;
        calls.Input.Stagnant = fault == "stagnant"; calls.Input.RegressClock = fault == "regress";
        await using var session = new WasapiSilentSession(route, new(), calls.Open);
        var result = await session.RunAsync(TimeSpan.FromMilliseconds(700), TestContext.Current.CancellationToken);
        Assert.NotNull(result.Failure); Assert.False(result.MovementProven); Assert.True(result.Completed);
    }
    [Theory]
    [InlineData("PositionRegressed")]
    [InlineData("AdvanceOutsideTolerance")]
    public async Task TerminalReportDistinguishesClockRejectionsThatShareInvalidPosition(string reason)
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        calls.Input.ReadClockOverride = index =>
        {
            // A leitura simulada deve respeitar o relógio monotônico real do teste.
            // Sem aguardar o instante previsto, um runner rápido rejeita o QPC futuro
            // como InvalidTime antes de verificar a falha de posição pretendida.
            long instantePrevisto = calls.Input.StartTime + (long)index * 1_000_000;
            Assert.True(
                SpinWait.SpinUntil(() => WasapiTime.Now100ns() >= instantePrevisto, TimeSpan.FromSeconds(1)),
                "O relógio do teste não alcançou a leitura simulada no prazo.");
            return new(0, 192000, 0, index switch
            {
                0 => 19200,
                1 => 38400,
                _ => reason == "PositionRegressed" ? 1u : 60000u
            }, (ulong)instantePrevisto);
        };
        await using var session = new WasapiSilentSession(route, new(), calls.Open);

        var result = await session.RunAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.True(result.Completed);
        Assert.False(result.TransportObserved);
        Assert.Equal(NativeClockResult.InvalidPosition, result.Capture.ClockStatus);
        string json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);
        var capture = document.RootElement.GetProperty("Capture");
        Assert.True(capture.TryGetProperty("ClockHistory", out var history), "A rejeição precisa de histórico numérico no relatório terminal.");
        Assert.Equal(reason, history.GetProperty("Rejection").GetProperty("Validation").GetProperty("Reason").GetString());
        Assert.Equal(38400UL, history.GetProperty("LastAccepted").GetProperty("Reading").GetProperty("Position").GetUInt64());
        Assert.True(document.RootElement.GetProperty("Playback").GetProperty("ClockHistory").GetProperty("Readings").GetArrayLength() > 0);
        Assert.DoesNotContain(route.Capture.Endpoint.Id, json);
        Assert.DoesNotContain(route.Playback.Endpoint.Id, json);
    }

    [Fact]
    public async Task CancellationAfterRenderAcquireStillReleasesSilentAndStopsBeforeCaptureStart()
    {
        var route = WasapiContractTests.Route(); var calls = new Calls(route);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        calls.Output.OnStep = step => { if (step == "GetRender") cancellation.Cancel(); };
        await using var session = new WasapiSilentSession(route, new(), calls.Open);
        var result = await session.RunAsync(Duration, cancellation.Token);
        Assert.True(result.Completed); Assert.Equal(1, result.Playback.PacketsReleased); Assert.False(result.Capture.StartAttempted);
    }
    [Fact]
    public async Task CaptureStartFailureStopsBothRolesAndAcceptsAlreadyStopped()
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        calls.Input.StartResult = Invalidated;
        calls.Input.StopResult = 1;
        await using var session = new WasapiSilentSession(route, new(), calls.Open);

        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);

        Assert.True(result.Playback.Started);
        Assert.True(result.Capture.StartAttempted);
        Assert.False(result.Capture.Started);
        Assert.Equal(Invalidated, result.Capture.StartHResult);
        Assert.Equal(1, result.Capture.StopHResult);
        Assert.True(result.Capture.StopConfirmed);
        Assert.True(result.Playback.StopConfirmed);
        Assert.True(result.Completed);
        Assert.False(result.TransportObserved);
    }

    [Fact]
    public async Task FailedStopRetainsLeaseUntilExplicitRetryOnSameOwner()
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        var guard = new TransportActivityGuard();
        calls.Output.StopResult = Invalidated;
        var session = new WasapiSilentSession(route, guard, calls.Open, TimeSpan.FromSeconds(1));
        try
        {
            var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);

            Assert.False(result.Completed);
            Assert.True(result.ResourcesRetained);
            Assert.True(result.Capture.Released);
            Assert.False(result.Playback.StopConfirmed);
            Assert.Equal(Invalidated, result.Playback.StopHResult);
            Assert.Equal(0, calls.Output.DisposeCalls);
            Assert.Null(guard.TryAcquire(AudioActivity.ShortDiagnostic));
        }
        finally
        {
            calls.Output.StopResult = 0;
            await session.DisposeAsync();
        }
        Assert.Equal(2, calls.Output.Stops);
        Assert.Equal(1, calls.Output.DisposeCalls);
        using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic);
        Assert.NotNull(lease);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task FailedReleaseRetainsLeaseAndNeverRetriesPacketOrDisposesItsParents(bool startupDiscard, bool windowed, bool checkpoints)
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        var guard = new TransportActivityGuard();
        if (startupDiscard) calls.Input.StartupFlags = 1;
        calls.Input.ReleaseResult = Invalidated;
        var session = new WasapiSilentSession(route, guard, calls.Open, TimeSpan.FromSeconds(1), simulateCaptureTiming: windowed, observeCheckpoints: checkpoints);

        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);

        Assert.True(result.ResourcesRetained);
        Assert.False(result.Completed);
        if (checkpoints) Assert.Equal(WasapiCheckpointStatus.Inactive, session.ReadCheckpoint(route.Capture.Endpoint.Generation).Status);
        Assert.True(result.Capture.BufferInFlight);
        Assert.Null(result.Capture.CaptureTiming);
        Assert.True(result.Capture.StopConfirmed);
        Assert.Equal(Invalidated, result.Capture.ReleaseHResult);
        Assert.Equal(48u, Assert.Single(calls.Input.CaptureReleases));
        Assert.Equal(0, result.Capture.StartupDiscardedPackets);
        Assert.Equal(0, result.Capture.PacketsAccepted);
        Assert.NotNull(result.Capture.CleanupError);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask());
        Assert.Null(guard.TryAcquire(AudioActivity.ShortDiagnostic));
        Assert.Equal(0, calls.Input.DisposeCalls);
        Assert.Single(calls.Log, step => step.Input && step.Step == "ReleaseCapture");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CancelWhileNativeCaptureIsInFlightRetainsLeaseUntilOwnerReturns(bool windowed, bool checkpoints)
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        var guard = new TransportActivityGuard();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        calls.Input.OnStep = step =>
        {
            if (step != "GetCapture") return;
            entered.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(8)));
        };
        var session = new WasapiSilentSession(route, guard, calls.Open, TimeSpan.FromSeconds(1), simulateCaptureTiming: windowed, observeCheckpoints: checkpoints);
        try
        {
            var running = session.RunAsync(TimeSpan.FromSeconds(3), cancel.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            if (checkpoints)
                Assert.Equal(WasapiCheckpointStatus.OwnerBusy, session.ReadCheckpoint(route.Capture.Endpoint.Generation).Status);
            cancel.Cancel();
            var result = await running;
            if (checkpoints)
                Assert.Equal(WasapiCheckpointStatus.Inactive, session.ReadCheckpoint(route.Capture.Endpoint.Generation).Status);

            Assert.True(result.ResourcesRetained);
            Assert.False(result.DurationReached);
            Assert.Null(result.Capture.ClockHistory);
            Assert.Null(result.Playback.ClockHistory);
            Assert.Null(guard.TryAcquire(AudioActivity.NativePreparation));
        }
        finally
        {
            release.Set();
            await session.DisposeAsync();
        }
        Assert.Equal(1, calls.Input.DisposeCalls);
        Assert.Single(calls.Log, step => step.Input && step.Step == "ReleaseCapture");
        Assert.Equal(0, calls.Input.ReleasedFrames);
        using var lease = guard.TryAcquire(AudioActivity.NativePreparation);
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task CurrentGenerationInvalidatesBeforeCaptureStart()
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        await using var session = new WasapiSilentSession(route, new(), calls.Open);
        calls.Output.OnStep = step =>
        {
            if (step == "Start") session.Invalidate(route.Capture.Endpoint.Generation);
        };

        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);

        Assert.Equal("SelectionInvalidated", result.Failure);
        Assert.True(result.Completed);
        Assert.False(result.Capture.StartAttempted);
        Assert.True(result.Playback.StopConfirmed);
        Assert.False(result.TransportObserved);
    }

    [Theory]
    [InlineData(250, false)]
    [InlineData(2600, true)]
    public async Task WindowedSessionRequiresSustainedCorroboratedCapture(int milliseconds, bool expected)
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        calls.Input.PacketFrames = 480;
        calls.Input.NominalPacketCadence = true;
        calls.Input.StartupFlags = 3;
        await using var session = new WasapiSilentSession(route, new(), calls.Open, simulateCaptureTiming: true);

        var result = await session.RunAsync(TimeSpan.FromMilliseconds(milliseconds), TestContext.Current.CancellationToken);

        Assert.True(result.Completed);
        Assert.True(result.DurationReached);
        Assert.Null(result.Failure);
        Assert.Equal(expected, result.TransportObserved);
        Assert.Equal(expected, result.Capture.CaptureTiming!.Confirmed);
        Assert.Equal(result.Capture.PacketsAccepted, result.Capture.CaptureTiming.ContinuousPackets);
        Assert.Equal(1, result.Capture.StartupDiscardedPackets);
        Assert.Null(result.Playback.CaptureTiming);
    }

    [Fact]
    public async Task WindowedSessionRejectsGapWithZeroReleaseAndKeepsTheGuardUntilCleanup()
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        var guard = new TransportActivityGuard();
        calls.Input.PacketFrames = 480;
        calls.Input.NominalPacketCadence = true;
        calls.Input.SecondPacketOffset = 1;
        calls.Input.OnStep = step =>
        {
            if (step == "ReleaseCapture") Assert.Null(guard.TryAcquire(AudioActivity.ShortDiagnostic));
        };
        await using var session = new WasapiSilentSession(route, guard, calls.Open, simulateCaptureTiming: true);

        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);

        Assert.True(result.Completed);
        Assert.False(result.TransportObserved);
        Assert.Equal("Capture", result.Failure);
        Assert.Equal("PacketPositionNotContiguous", result.Capture.CaptureTiming!.Failure);
        Assert.Equal(new uint[] { 480, 0 }, calls.Input.CaptureReleases.ToArray());
        Assert.Equal(1, result.Capture.PacketsAccepted);
        using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic);
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task OldGenerationCannotInvalidateAndConcurrentDisposeIsSafe()
    {
        var route = WasapiContractTests.Route(); var calls = new Calls(route);
        var session = new WasapiSilentSession(route, new(), calls.Open);
        var running = session.RunAsync(Duration, TestContext.Current.CancellationToken);
        session.Invalidate(route.Capture.Endpoint.Generation - 1);
        var result = await running; Assert.Null(result.Failure);
        await Task.WhenAll(session.DisposeAsync().AsTask(), session.DisposeAsync().AsTask());
        Assert.Equal(1, calls.Input.DisposeCalls); Assert.Equal(1, calls.Output.DisposeCalls);
    }
}
