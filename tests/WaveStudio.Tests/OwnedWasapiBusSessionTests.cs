using System.Buffers.Binary;
using System.Runtime.InteropServices;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class OwnedWasapiBusSessionTests
{
    private static readonly PcmFloatFormat Format = new(48000, 1);
    private static readonly WasapiPcm Pcm = new(1, 48000, 1, 16, 16, 2, 96000, 0, WasapiPcm.PcmSubFormat, null);
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);
    private const int Failure = unchecked((int)0x80004005);

    [Fact]
    public async Task PrimesEveryRenderBeforeStartingAndStopsOnTheSameMtaBeforeReleasingServices()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var rig = new Rig { OnStart = id => { if (id == "mic") cancel.Cancel(); return 0; }, OnStop = _ => 1 };

        var result = await Run(rig, 1, cancel.Token);

        Assert.True(result.Canceled);
        Assert.False(result.ResourcesRetained);
        Assert.Null(result.Failure);
        Assert.Equal([result.OwnerThreadId], rig.Threads.Distinct());
        Assert.All(rig.Apartments, state => Assert.Equal(ApartmentState.MTA, state));
        Assert.Equal(["personal:release-buffer:1056:2", "stream:release-buffer:1056:2",
            "personal:start", "stream:start", "game:start", "mic:start",
            "mic:stop", "game:stop", "stream:stop", "personal:stop"],
            rig.Calls.Where(x => x.Contains(":release-buffer:") || x.EndsWith(":start") || x.EndsWith(":stop")));
        Assert.True(rig.Calls.IndexOf("personal:stop") < rig.Calls.FindIndex(x => x.EndsWith(":dispose-packets")));
        Assert.True(rig.Calls.IndexOf("personal:dispose-packets") < rig.Calls.IndexOf("personal:dispose-client"));
        Assert.True(rig.Calls.IndexOf("personal:dispose-client") < rig.Calls.IndexOf("personal:dispose-device"));
        Assert.DoesNotContain(rig.Calls, x => x.EndsWith(":clock-read"));
        using var available = rig.Guard.TryAcquire(AudioActivity.ShortDiagnostic);
        Assert.NotNull(available);
    }

    [Fact]
    public async Task FailedStartAndStopStillStopEveryAttemptedStreamAndQuarantineTheGuard()
    {
        var rig = new Rig { OnStart = id => id == "mic" ? Failure : 0, OnStop = id => id == "game" ? Failure : 0 };

        var result = await Run(rig, 1, TestContext.Current.CancellationToken);

        var start = Assert.IsType<WasapiCallException>(result.Failure);
        Assert.Equal("IAudioClient.Start", start.Stage);
        Assert.Equal(Failure, start.Result);
        Assert.Equal("IAudioClient.Stop", Assert.IsType<WasapiCallException>(Assert.Single(result.CleanupFailures)).Stage);
        Assert.Equal(["mic:stop", "game:stop", "stream:stop", "personal:stop"], rig.Calls.Where(x => x.EndsWith(":stop")));
        Assert.True(result.ResourcesRetained);
        Assert.DoesNotContain(rig.Calls, x => x.Contains(":dispose-"));
        int count = rig.Calls.Count;
        var refused = await Run(rig, 2, TestContext.Current.CancellationToken);
        Assert.IsType<InvalidOperationException>(refused.Failure);
        Assert.Equal(count, rig.Calls.Count);
    }

    [Fact]
    public async Task FailedPrimeReleaseRetainsBorrowedResourcesWithoutStartingAnyStream()
    {
        var rig = new Rig { OnRelease = (id, _) => id == "stream" ? Failure : 0 };

        var result = await Run(rig, 1, TestContext.Current.CancellationToken);

        Assert.Equal("IAudioRenderClient.ReleaseBuffer", Assert.IsType<WasapiCallException>(result.Failure).Stage);
        Assert.True(result.ResourcesRetained);
        Assert.DoesNotContain(rig.Calls, x => x.EndsWith(":start") || x.EndsWith(":stop"));
        Assert.DoesNotContain("stream:dispose-packets", rig.Calls);
        Assert.DoesNotContain("stream:dispose-client", rig.Calls);
        Assert.Null(rig.Guard.TryAcquire(AudioActivity.ShortDiagnostic));
    }

    [Fact]
    public async Task FaultEndsGenerationAndExplicitRecoveryRevalidatesAndPublishesOnlyFreshPcm()
    {
        var rig = new Rig { Sample = 8192 };
        var first = await Run(rig, 7, TestContext.Current.CancellationToken);

        Assert.Equal(1, first.BlocksProcessed);
        Assert.NotNull(first.Failure);
        Assert.False(first.ResourcesRetained);
        Assert.Equal([8192, 8192], rig.OutputSamples);

        int calls = rig.Calls.Count;
        var stale = await Run(rig, 7, TestContext.Current.CancellationToken);
        Assert.IsType<InvalidOperationException>(stale.Failure);
        Assert.Equal(calls, rig.Calls.Count);

        rig.Sample = 4096;
        rig.DivergentFormat = true;
        var changed = await Run(rig, 8, TestContext.Current.CancellationToken);
        Assert.NotNull(changed.Failure);
        Assert.False(changed.ResourcesRetained);
        Assert.DoesNotContain(rig.Calls.Skip(calls), x => x.EndsWith(":start"));

        rig.DivergentFormat = false;
        rig.ClockGroup = 0;
        var recovered = await Run(rig, 9, TestContext.Current.CancellationToken);
        Assert.Equal(1, recovered.BlocksProcessed);
        Assert.False(recovered.ResourcesRetained);
        Assert.Equal([8192, 8192, 4096, 4096], rig.OutputSamples);
        Assert.Equal([7L, 8L, 9L], rig.OpenedGenerations.Distinct());
    }

    [Fact]
    public async Task CancellationDuringRuntimeStopsClientsBeforeDisposingEvenWhenNoSecondBlockArrives()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var rig = new Rig
        {
            OnRelease = (id, flags) => { if (id == "stream" && flags == 0) cancel.Cancel(); return 0; }
        };

        var result = await Run(rig, 1, cancel.Token);

        Assert.True(result.Canceled);
        Assert.Null(result.Failure);
        Assert.False(result.ResourcesRetained);
        Assert.Equal(["mic:stop", "game:stop", "stream:stop", "personal:stop"], rig.Calls.Where(x => x.EndsWith(":stop")));
        Assert.True(rig.Calls.IndexOf("personal:stop") < rig.Calls.FindIndex(x => x.EndsWith(":dispose-packets")));
    }

    [Fact]
    public async Task RevalidationFailureAfterPrimingRefusesEveryStartAndReleasesPreparedStreams()
    {
        var rig = new Rig { OnRevalidate = id => id == "mic" ? Failure : 0 };

        var result = await Run(rig, 1, TestContext.Current.CancellationToken);

        Assert.Equal("IAudioClient.IsFormatSupported(revalidate)", Assert.IsType<WasapiCallException>(result.Failure).Stage);
        Assert.False(result.ResourcesRetained);
        Assert.Contains("personal:release-buffer:1056:2", rig.Calls);
        Assert.Contains("stream:release-buffer:1056:2", rig.Calls);
        Assert.DoesNotContain(rig.Calls, x => x.EndsWith(":start") || x.EndsWith(":stop"));
        Assert.Equal(4, rig.Calls.Count(x => x.EndsWith(":dispose-client")));
        using var available = rig.Guard.TryAcquire(AudioActivity.ShortDiagnostic);
        Assert.NotNull(available);
    }

    [Fact]
    public async Task DurationShorterThanABlockStopsWithoutTakingAClockCut()
    {
        var rig = new Rig();

        var result = await Run(rig, 1, TestContext.Current.CancellationToken, TimeSpan.FromTicks(9_500));

        Assert.True(result.DurationReached);
        Assert.False(result.Canceled);
        Assert.Null(result.Failure);
        Assert.Equal(0, result.BlocksProcessed);
        Assert.DoesNotContain(rig.Calls, x => x.EndsWith(":clock-read"));
        Assert.Equal(4, rig.Calls.Count(x => x.EndsWith(":stop")));
    }

    [Fact]
    public async Task InvalidatedPacketReadStopsAllClientsAndExposesTheHResultWithoutOpeningAnotherEndpoint()
    {
        const int invalidated = unchecked((int)0x88890004);
        var rig = new Rig { CaptureFailure = invalidated };

        var result = await Run(rig, 1, TestContext.Current.CancellationToken);

        var error = Assert.IsType<WasapiCallException>(result.Failure);
        Assert.Equal("IAudioCaptureClient.GetNextPacketSize", error.Stage);
        Assert.Equal(invalidated, error.Result);
        Assert.Equal(0, result.BlocksProcessed);
        Assert.False(result.ResourcesRetained);
        Assert.Empty(rig.OutputSamples);
        Assert.Equal(4, rig.OpenedGenerations.Count);
        Assert.Equal(4, rig.Calls.Count(x => x.EndsWith(":stop")));
    }

    [Theory]
    [InlineData("clock")]
    [InlineData("capture")]
    [InlineData("render")]
    [InlineData("render-release-fails")]
    public async Task DeadlineCrossedInsideNativeCallDoesNotSubmitLatePcm(string boundary)
    {
        TimeSpan duration = TimeSpan.FromSeconds(1);
        bool delayed = false;
        void CrossDeadline()
        {
            delayed = true;
            Thread.Sleep(duration + TimeSpan.FromMilliseconds(50));
        }

        var rig = new Rig
        {
            OnClockRead = (id, cut) => { if (boundary == "clock" && id == "personal" && cut == 2) CrossDeadline(); },
            OnCaptureAcquire = id => { if (boundary == "capture" && id == "mic") CrossDeadline(); },
            OnRenderAcquire = (id, frames) =>
            {
                if (boundary.StartsWith("render", StringComparison.Ordinal) && id == "stream" && frames == 480)
                    CrossDeadline();
            },
            OnRelease = (id, _) => delayed && id == "stream" && boundary == "render-release-fails" ? Failure : 0
        };

        var result = await Run(rig, 1, TestContext.Current.CancellationToken, duration);

        Assert.True(delayed);
        Assert.Equal(0, result.BlocksProcessed);
        Assert.Equal(4, rig.Calls.Count(x => x.EndsWith(":stop")));
        if (boundary.StartsWith("render", StringComparison.Ordinal))
        {
            Assert.Equal([4096], rig.OutputSamples);
            Assert.Contains("stream:release-buffer:480:2", rig.Calls);
            Assert.DoesNotContain("stream:release-buffer:480:0", rig.Calls);
        }
        else
        {
            Assert.Empty(rig.OutputSamples);
            Assert.DoesNotContain(rig.Calls, x => x.EndsWith(":get-buffer:480"));
            if (boundary == "clock") Assert.Equal(1, rig.Calls.Count(x => x == "stream:clock-read"));
            if (boundary == "capture") Assert.Contains("mic:capture-release:480", rig.Calls);
        }

        if (boundary == "render-release-fails")
        {
            Assert.True(result.ResourcesRetained);
            Assert.False(result.DurationReached);
            Assert.Equal("IAudioRenderClient.ReleaseBuffer", Assert.IsType<WasapiCallException>(result.Failure).Stage);
            Assert.DoesNotContain("stream:dispose-packets", rig.Calls);
            Assert.Null(rig.Guard.TryAcquire(AudioActivity.ShortDiagnostic));
        }
        else
        {
            Assert.False(result.ResourcesRetained);
            Assert.True(result.DurationReached);
            Assert.Null(result.Failure);
        }
    }

    private static Task<OwnedWasapiBusResult> Run(Rig rig, long generation, CancellationToken token, TimeSpan? duration = null)
    {
        MixSource Source(string id) => new(new(id, EndpointDirection.Capture, generation), Format,
            MixSourceKind.Game, MixOrigin.Discrete, ChannelPolicy.Preserve, new(0.5f), new(0.5f));
        TransportEndpoint Output(string id) => new(id, EndpointDirection.Playback, generation);
        return OwnedWasapiBusSession.RunAsync(generation, Format,
            [new(Source("game"), Pcm), new(Source("mic"), Pcm)],
            [new(Output("personal"), MixBus.Personal, Pcm), new(Output("stream"), MixBus.Stream, Pcm)],
            duration ?? Duration, rig.Guard, token, rig.Open, rig.ReadReference);
    }

    private sealed class Rig
    {
        internal TransportActivityGuard Guard { get; } = new();
        internal List<string> Calls { get; } = [];
        internal List<int> Threads { get; } = [];
        internal List<ApartmentState> Apartments { get; } = [];
        internal List<long> OpenedGenerations { get; } = [];
        internal List<int> OutputSamples { get; } = [];
        internal int ClockGroup { get; set; }
        internal int Sample { get; set; } = 4096;
        internal bool DivergentFormat { get; set; }
        internal int CaptureFailure { get; init; }
        internal Func<string, int> OnRevalidate { get; init; } = _ => 0;
        internal Action<string, int> OnClockRead { get; init; } = (_, _) => { };
        internal Action<string> OnCaptureAcquire { get; init; } = _ => { };
        internal Action<string, uint> OnRenderAcquire { get; init; } = (_, _) => { };
        internal Func<string, int> OnStart { get; init; } = _ => 0;
        internal Func<string, int> OnStop { get; init; } = _ => 0;
        internal Func<string, uint, int> OnRelease { get; init; } = (_, _) => 0;
        internal IWasapiPreparationDevice Open(TransportEndpoint endpoint)
        {
            OpenedGenerations.Add(endpoint.Generation);
            return new Device(this, endpoint);
        }
        internal long ReadReference() => 2_000_000 + ClockGroup * 100_000 + 1;
        internal void Record(string call)
        {
            using var competing = Guard.TryAcquire(AudioActivity.ShortDiagnostic);
            Assert.Null(competing);
            Calls.Add(call);
            Threads.Add(Environment.CurrentManagedThreadId);
            Apartments.Add(Thread.CurrentThread.GetApartmentState());
        }
    }

    private sealed class Device(Rig rig, TransportEndpoint endpoint) : IWasapiPreparationDevice
    {
        public void ValidateIdentity(TransportEndpoint actual)
        {
            Assert.Equal(endpoint, actual);
            rig.Record(endpoint.Id + ":identity");
        }
        public IWasapiPreparationClient Activate()
        {
            rig.Record(endpoint.Id + ":activate");
            return new Client(rig, endpoint);
        }
        public void Dispose() => rig.Record(endpoint.Id + ":dispose-device");
    }

    private sealed class Client(Rig rig, TransportEndpoint endpoint) : IWasapiBoundStreamClient
    {
        internal const uint Capacity = 1056;
        private bool borrowed;
        public TransportEndpoint Endpoint => endpoint;
        public WasapiPcm PreparedFormat { get; private set; } = null!;
        public WasapiPcm ReadMixFormat()
        {
            rig.Record(endpoint.Id + ":format");
            return rig.DivergentFormat ? Pcm with { Tag = 65534, ExtraBytes = 22, ChannelMask = 0 } : Pcm;
        }
        public void RequireExactSupport() => rig.Record(endpoint.Id + ":support");
        public (long, long) ReadPeriods() => (100_000, 30_000);
        public void Initialize() { rig.Record(endpoint.Id + ":initialize"); PreparedFormat = Pcm; }
        public (uint, long) ReadBufferInfo() => (Capacity, 0);
        public IWasapiPreparationClock OpenClock() => new Clock(rig, endpoint.Id);
        public int Start() { rig.Record(endpoint.Id + ":start"); return rig.OnStart(endpoint.Id); }
        public int Stop() { rig.Record(endpoint.Id + ":stop"); return rig.OnStop(endpoint.Id); }
        public void RevalidateFormat(WasapiPcm expected)
        {
            Assert.Equal(Pcm, expected);
            rig.Record(endpoint.Id + ":revalidate");
            WasapiCallException.RequireSuccess(rig.OnRevalidate(endpoint.Id), "IAudioClient.IsFormatSupported(revalidate)");
        }
        public uint ReadPadding() => 0;
        public IWasapiCapturePackets OpenCapture() => new Capture(rig, endpoint.Id);
        public IWasapiRenderPackets OpenRender() => new Render(rig, endpoint.Id);
        public IDisposable BorrowBusServices() { Assert.False(borrowed); borrowed = true; return new Borrow(this); }
        public void Dispose() { Assert.False(borrowed); rig.Record(endpoint.Id + ":dispose-client"); }

        private sealed class Borrow(Client client) : IDisposable
        {
            public void Dispose() => client.borrowed = false;
        }
    }

    private sealed class Clock(Rig rig, string id) : IWasapiPreparationClock
    {
        public WasapiClockReading Read()
        {
            rig.Record(id + ":clock-read");
            if (id == "game") rig.ClockGroup++;
            rig.OnClockRead(id, rig.ClockGroup);
            return new(0, 48000, 0, (ulong)(1000 + rig.ClockGroup * 480), (ulong)(rig.ReadReference() - 1));
        }
        public void Dispose() => rig.Record(id + ":dispose-clock");
    }

    private sealed class Capture(Rig rig, string id) : IWasapiCapturePackets
    {
        private nint pointer;
        private bool supplied;
        public uint NextPacketSize()
        {
            WasapiCallException.RequireSuccess(rig.CaptureFailure, "IAudioCaptureClient.GetNextPacketSize");
            return supplied ? 0u : 480u;
        }
        public WasapiCaptureBuffer GetBuffer()
        {
            rig.OnCaptureAcquire(id);
            pointer = Marshal.AllocHGlobal(960);
            for (int offset = 0; offset < 960; offset += 2) Marshal.WriteInt16(pointer, offset, (short)rig.Sample);
            supplied = true;
            return new(0, pointer, 480, 0, 0, (ulong)(rig.ReadReference() - 1));
        }
        public int ReleaseBuffer(uint framesRead)
        {
            rig.Record(id + $":capture-release:{framesRead}");
            Marshal.FreeHGlobal(pointer);
            pointer = 0;
            return 0;
        }
        public void Dispose() { Assert.Equal(0, pointer); rig.Record(id + ":dispose-packets"); }
    }

    private sealed class Render(Rig rig, string id) : IWasapiRenderPackets
    {
        private nint pointer;
        public nint GetBuffer(uint frames)
        {
            rig.Record(id + ":get-buffer:" + frames);
            rig.OnRenderAcquire(id, frames);
            return pointer = Marshal.AllocHGlobal(checked((int)frames * 2));
        }
        public int ReleaseBuffer(uint framesWritten, uint flags)
        {
            rig.Record(id + $":release-buffer:{framesWritten}:{flags}");
            if (flags == 0)
            {
                byte[] sample = new byte[2];
                Marshal.Copy(pointer + ((int)framesWritten - 1) * 2, sample, 0, sample.Length);
                rig.OutputSamples.Add(BinaryPrimitives.ReadInt16LittleEndian(sample));
            }
            Marshal.FreeHGlobal(pointer);
            pointer = 0;
            return rig.OnRelease(id, flags);
        }
        public void Dispose() { Assert.Equal(0, pointer); rig.Record(id + ":dispose-packets"); }
    }
}
