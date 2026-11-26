using System.Buffers.Binary;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed partial class OwnedBusSchedulerTests
{
    private static readonly PcmFloatFormat Mono = new(48000, 1);

    [Fact]
    public void IndependentClocksAdmitTwoSourcesAndSeparatePersonalAndStream()
    {
        RunMta(() =>
        {
            var game = Source("game", MixSourceKind.Game, stream: new(1));
            var mic = Source("mic", MixSourceKind.Microphone, stream: new(1));
            using var bridge = new BoundedBusPcmTransport(1, Mono, [game, mic]);
            using var gameAdapter = new WasapiCaptureBlockAdapter(bridge, game.Endpoint, Mono, Pcm16());
            using var micAdapter = new WasapiCaptureBlockAdapter(bridge, mic.Endpoint, Mono, Pcm16());
            using var personalAdapter = new WasapiRenderBlockAdapter(bridge, Mono, Pcm16());
            using var streamAdapter = new WasapiRenderBlockAdapter(bridge, Mono, Pcm16());
            var gamePackets = new CapturePackets(8192);
            var micPackets = new CapturePackets(4096);
            var personalPackets = new RenderPackets();
            var streamPackets = new RenderPackets();
            using var gameOwner = new WasapiBusCaptureOwnerStep(gamePackets, gameAdapter, game.Endpoint, Mono);
            using var micOwner = new WasapiBusCaptureOwnerStep(micPackets, micAdapter, mic.Endpoint, Mono);
            using var personalOwner = new WasapiBusRenderOwnerStep(personalPackets, personalAdapter, OutputEndpoint("headset"), Mono, Pcm16());
            using var streamOwner = new WasapiBusRenderOwnerStep(streamPackets, streamAdapter, OutputEndpoint("stream"), Mono, Pcm16());
            var guard = new TransportActivityGuard();

            using var scheduler = new OwnedBusScheduler(1, Mono, [game, mic], bridge,
                [new(game, gameOwner), new(mic, micOwner)],
                [new(new("headset", EndpointDirection.Playback, 1), MixBus.Personal, personalOwner),
                 new(new("stream", EndpointDirection.Playback, 1), MixBus.Stream, streamOwner)],
                guard, TestContext.Current.CancellationToken);
            TransportEndpoint[] endpoints = [game.Endpoint, mic.Endpoint, new("headset", EndpointDirection.Playback, 1), new("stream", EndpointDirection.Playback, 1)];
            Assert.True(Step(scheduler, 0, 900_000, endpoints));
            Assert.True(Step(scheduler, 1, 1_000_000, endpoints));
            Assert.Equal(1, gamePackets.Releases);
            Assert.Equal(1, micPackets.Releases);
            Assert.Equal(0u, personalPackets.LastFlags);
            Assert.Equal(0u, streamPackets.LastFlags);
            Assert.Equal((short)12288, BinaryPrimitives.ReadInt16LittleEndian(personalPackets.Last.AsSpan(958)));
            Assert.Equal((short)8192, BinaryPrimitives.ReadInt16LittleEndian(streamPackets.Last.AsSpan(958)));
            Assert.True(Step(scheduler, 2, 1_100_000, endpoints));
            Assert.Equal(2, personalPackets.Releases);
            Assert.Equal(2, streamPackets.Releases);
            scheduler.Stop();
            scheduler.Stop();
            using var next = guard.TryAcquire(AudioActivity.IsolatedTransport);
            Assert.NotNull(next);
        });
    }

    [Fact]
    public void InvalidCaptureTimestampStopsBeforeRenderingAndReleasesActivity()
    {
        RunMta(() =>
        {
            var game = Source("game");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [game]);
            using var capAdapter = new WasapiCaptureBlockAdapter(bridge, game.Endpoint, Mono, Pcm16());
            using var renderAdapter = new WasapiRenderBlockAdapter(bridge, Mono, Pcm16());
            var capturePackets = new CapturePackets(8192) { Clock = 10_000_000 };
            var renderPackets = new RenderPackets();
            using var capture = new WasapiBusCaptureOwnerStep(capturePackets, capAdapter, game.Endpoint, Mono);
            using var render = new WasapiBusRenderOwnerStep(renderPackets, renderAdapter, OutputEndpoint("out"), Mono, Pcm16());
            var guard = new TransportActivityGuard();
            using var scheduler = new OwnedBusScheduler(1, Mono, [game], bridge,
                [new(game, capture)],
                [new(new("out", EndpointDirection.Playback, 1), MixBus.Personal, render)],
                guard, TestContext.Current.CancellationToken);
            TransportEndpoint[] endpoints = [game.Endpoint, new("out", EndpointDirection.Playback, 1)];
            Assert.True(Step(scheduler, 0, 900_000, endpoints));
            Assert.False(Step(scheduler, 1, 1_000_000, endpoints));
            Assert.False(scheduler.IsRunning);
            Assert.Equal(1, capturePackets.Releases);
            Assert.Equal(0, renderPackets.Releases);
            Assert.False(bridge.TryOfferCapture(game.Endpoint, 0, new float[480]));
            using var lease = guard.TryAcquire(AudioActivity.IsolatedTransport);
            Assert.NotNull(lease);
        });
    }

    [Fact]
    public void ClockGapFailsClosedAndDiscardsUnusedBus()
    {
        RunMta(() =>
        {
            var game = Source("game", stream: new(1));
            using var bridge = new BoundedBusPcmTransport(1, Mono, [game], capacityBlocks: 2);
            using var capAdapter = new WasapiCaptureBlockAdapter(bridge, game.Endpoint, Mono, Pcm16());
            using var renderAdapter = new WasapiRenderBlockAdapter(bridge, Mono, Pcm16());
            var capturePackets = new CapturePackets(8192);
            var renderPackets = new RenderPackets();
            using var capture = new WasapiBusCaptureOwnerStep(capturePackets, capAdapter, game.Endpoint, Mono);
            using var render = new WasapiBusRenderOwnerStep(renderPackets, renderAdapter, OutputEndpoint("out"), Mono, Pcm16());
            using var scheduler = new OwnedBusScheduler(1, Mono, [game], bridge,
                [new(game, capture)],
                [new(new("out", EndpointDirection.Playback, 1), MixBus.Personal, render)],
                new TransportActivityGuard(), TestContext.Current.CancellationToken);
            TransportEndpoint[] endpoints = [game.Endpoint, new("out", EndpointDirection.Playback, 1)];
            Assert.True(Step(scheduler, 0, 900_000, endpoints));
            Assert.True(Step(scheduler, 1, 1_000_000, endpoints));
            Assert.True(Step(scheduler, 2, 1_100_000, endpoints));
            Assert.Equal(2, renderPackets.Releases);
            Assert.False(Step(scheduler, 4, 1_300_000, endpoints));
            Assert.Equal(2, capturePackets.Releases);
            Assert.Equal(2, renderPackets.Releases);
            Assert.False(scheduler.IsRunning);
        });
    }

    [Fact]
    public void CancellationAndRepeatedStopCannotSendNativePackets()
    {
        RunMta(() =>
        {
            var game = Source("game");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [game]);
            using var capAdapter = new WasapiCaptureBlockAdapter(bridge, game.Endpoint, Mono, Pcm16());
            using var renderAdapter = new WasapiRenderBlockAdapter(bridge, Mono, Pcm16());
            var capturePackets = new CapturePackets(8192);
            var renderPackets = new RenderPackets();
            using var capture = new WasapiBusCaptureOwnerStep(capturePackets, capAdapter, game.Endpoint, Mono);
            using var render = new WasapiBusRenderOwnerStep(renderPackets, renderAdapter, OutputEndpoint("out"), Mono, Pcm16());
            using var cancel = new CancellationTokenSource();
            using var scheduler = new OwnedBusScheduler(1, Mono, [game], bridge,
                [new(game, capture)],
                [new(new("out", EndpointDirection.Playback, 1), MixBus.Personal, render)],
                new TransportActivityGuard(), cancel.Token);
            cancel.Cancel();
            Assert.False(scheduler.Tick(0, 1_000_000, []));
            scheduler.Stop();
            Assert.Equal(0, capturePackets.Releases);
            Assert.Equal(0, renderPackets.Releases);
        });
    }

    [Fact]
    public void DuplicateOutputBindingIsRejectedBeforeGuardAcquisition()
    {
        RunMta(() =>
        {
            var game = Source("game");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [game]);
            using var capAdapter = new WasapiCaptureBlockAdapter(bridge, game.Endpoint, Mono, Pcm16());
            using var renderAdapter = new WasapiRenderBlockAdapter(bridge, Mono, Pcm16());
            using var capture = new WasapiBusCaptureOwnerStep(new CapturePackets(8192), capAdapter, game.Endpoint, Mono);
            using var render = new WasapiBusRenderOwnerStep(new RenderPackets(), renderAdapter, OutputEndpoint("out1"), Mono, Pcm16());
            var guard = new TransportActivityGuard();
            Assert.Throws<ArgumentException>(() => new OwnedBusScheduler(1, Mono, [game], bridge,
                [new(game, capture)],
                [new(new("out1", EndpointDirection.Playback, 1), MixBus.Personal, render),
                 new(new("out2", EndpointDirection.Playback, 1), MixBus.Personal, render)],
                guard, TestContext.Current.CancellationToken));
            using var lease = guard.TryAcquire(AudioActivity.IsolatedTransport);
            Assert.NotNull(lease);
        });
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("quality")]
    [InlineData("future")]
    [InlineData("stale")]
    [InlineData("frequency")]
    [InlineData("regression")]
    [InlineData("identity")]
    [InlineData("discontinuity")]
    [InlineData("timestamp")]
    public void RejectsFaultsFromAnyOwnerBeforeSendingAnotherBlock(string fault)
    {
        RunMta(() =>
        {
            using var fixture = new SingleRoute();
            var scheduler = fixture.Scheduler;
            Assert.True(scheduler.Tick(0, 900_000, fixture.Readings(900_000)));
            Assert.False(scheduler.ClockAdmitted);
            Assert.Equal(0, fixture.Capture.Releases);
            Assert.Equal(0, fixture.Render.Releases);
            Assert.True(scheduler.Tick(1, 1_000_000, fixture.Readings(1_000_000)));
            Assert.True(scheduler.ClockAdmitted);

            const long now = 1_100_000;
            var samples = fixture.Readings(now);
            samples[1] = fault switch
            {
                "quality" => samples[1] with { Quality = NativeClockQuality.Unavailable },
                "future" => samples[1] with { Qpc100ns = now + 1 },
                "stale" => samples[1] with { Qpc100ns = now - 500_001 },
                "frequency" => samples[1] with { Frequency = 47999 },
                "regression" => samples[1] with { Position = 1 },
                "identity" => samples[1] with { Endpoint = fixture.Output with { Generation = 2 } },
                "discontinuity" => samples[1] with { Flags = TransportPacketFlags.Discontinuity },
                "timestamp" => samples[1] with { Flags = TransportPacketFlags.TimestampError },
                _ => samples[1]
            };
            Assert.False(scheduler.Tick(2, now, samples.AsSpan(0, fault == "missing" ? 1 : 2)));
            Assert.False(scheduler.IsRunning);
            Assert.Equal(1, fixture.Capture.Releases);
            Assert.Equal(1, fixture.Render.Releases);
            Assert.False(scheduler.Tick(2, now, fixture.Readings(now)));
            Assert.Null(fixture.Guard.TryAcquireOwnedBus(1));
            using var next = fixture.Guard.TryAcquireOwnedBus(2);
            Assert.NotNull(next);
        });
    }

    [Fact]
    public void FreshButMisalignedAnchorsDoNotAcquireAnyBuffer()
    {
        RunMta(() =>
        {
            using var fixture = new SingleRoute();
            var samples = fixture.Readings(900_000);
            samples[1] = samples[1] with { Qpc100ns = 750_000 };
            Assert.False(fixture.Scheduler.Tick(0, 900_000, samples));
            Assert.Equal(OwnedBusClockFailure.CutSkew, fixture.Scheduler.ClockFailure);
            Assert.Equal(0, fixture.Capture.Releases);
            Assert.Equal(0, fixture.Render.Releases);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PacketGapOrReleaseFailureStopsBeforeAnotherRender(bool failRelease)
    {
        RunMta(() =>
        {
            using var fixture = new SingleRoute();
            var scheduler = fixture.Scheduler;
            Assert.True(scheduler.Tick(0, 900_000, fixture.Readings(900_000)));
            Assert.True(scheduler.Tick(1, 1_000_000, fixture.Readings(1_000_000)));
            if (failRelease) fixture.Capture.ReleaseError = unchecked((int)0x80004005);
            else fixture.Capture.PositionOffset = 480;

            if (failRelease)
                Assert.Throws<WasapiCallException>(() => scheduler.Tick(2, 1_100_000, fixture.Readings(1_100_000)));
            else Assert.False(scheduler.Tick(2, 1_100_000, fixture.Readings(1_100_000)));
            Assert.False(scheduler.IsRunning);
            Assert.Equal(2, fixture.Capture.Releases);
            Assert.Equal(1, fixture.Render.Releases);
            using var next = fixture.Guard.TryAcquireOwnedBus(2);
            Assert.Equal(!failRelease, next is not null);
        });
    }

    [Theory]
    [InlineData("different-owner")]
    [InlineData("different-bridge")]
    public void RejectsForeignRenderOwnerBeforeAcquiringLease(string fault)
    {
        RunMta(() =>
        {
            var source = Source("game");
            var output = OutputEndpoint("private");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [source]);
            using var unrelated = new BoundedBusPcmTransport(1, Mono, [source]);
            using var captureAdapter = new WasapiCaptureBlockAdapter(bridge, source.Endpoint, Mono, Pcm16());
            using var renderAdapter = new WasapiRenderBlockAdapter(fault == "different-bridge" ? unrelated : bridge, Mono, Pcm16());
            using var capture = new WasapiBusCaptureOwnerStep(new CapturePackets(8192), captureAdapter, source.Endpoint, Mono);
            using var render = new WasapiBusRenderOwnerStep(new RenderPackets(), renderAdapter,
                fault == "different-owner" ? OutputEndpoint("stream") : output, Mono, Pcm16());
            var guard = new TransportActivityGuard();
            Assert.Throws<ArgumentException>(() => new OwnedBusScheduler(1, Mono, [source], bridge,
                [new(source, capture)], [new(output, MixBus.Personal, render)], guard));
            using var free = guard.TryAcquireOwnedBus(1);
            Assert.NotNull(free);
        });
    }

    [Fact]
    public void RepeatedSmallClockDriftFailsAnAccumulatedNominalWindow()
    {
        RunMta(() =>
        {
            using var route = new SingleRoute();
            bool Tick(int index, long now)
            {
                var cuts = route.Readings(now);
                cuts[1] = cuts[1] with { Position = cuts[1].Position - (ulong)(index * 12) };
                return route.Scheduler.Tick(index, now, cuts);
            }

            Assert.True(Tick(0, 900_000));
            Assert.True(Tick(1, 1_000_000));
            Assert.True(Tick(2, 1_100_000));
            Assert.True(Tick(3, 1_200_000));
            Assert.False(Tick(4, 1_300_000));
            Assert.Equal(OwnedBusClockFailure.NominalRate, route.Scheduler.ClockFailure);
            Assert.False(route.Scheduler.IsRunning);
            Assert.Equal(3, route.Render.Releases);
        });
    }

    [Theory]
    [InlineData("late-packet")]
    [InlineData("missing-packet")]
    [InlineData("release-capture")]
    [InlineData("release-render")]
    public void PacketBoundaryOrExceptionPreventsFurtherDelivery(string fault)
    {
        RunMta(() =>
        {
            using var route = new SingleRoute();
            Assert.True(route.Scheduler.Tick(0, 900_000, route.Readings(900_000)));
            if (fault == "late-packet") route.Capture.Clock = 890_000;
            if (fault == "missing-packet") route.Capture.Missing = true;
            if (fault == "release-capture") route.Capture.ThrowOnRelease = true;
            if (fault == "release-render") route.Render.ThrowOnRelease = true;

            bool releaseUnconfirmed = fault.StartsWith("release-", StringComparison.Ordinal);
            if (releaseUnconfirmed)
                Assert.Throws<InvalidOperationException>(() => route.Scheduler.Tick(1, 1_000_000, route.Readings(1_000_000)));
            else Assert.False(route.Scheduler.Tick(1, 1_000_000, route.Readings(1_000_000)));
            Assert.False(route.Scheduler.IsRunning);
            Assert.Equal(0, route.Render.Releases);
            using var next = route.Guard.TryAcquireOwnedBus(2);
            Assert.Equal(!releaseUnconfirmed, next is not null);
        });
    }

    [Fact]
    public void NewGenerationNeedsItsOwnAnchors()
    {
        RunMta(() =>
        {
            var guard = new TransportActivityGuard();
            using var original = new SingleRoute(1, guard);
            Assert.True(original.Scheduler.Tick(0, 900_000, original.Readings(900_000)));
            Assert.True(original.Scheduler.Tick(1, 1_000_000, original.Readings(1_000_000)));
            Assert.False(original.Scheduler.Tick(2, 1_100_000, []));
            Assert.Null(guard.TryAcquireOwnedBus(1));

            using var retired = new SingleRoute(2, guard);
            Assert.False(retired.Scheduler.Tick(0, 900_000, original.Readings(900_000)));
            Assert.Equal(0, retired.Render.Releases);

            using var latest = new SingleRoute(3, guard);
            Assert.True(latest.Scheduler.Tick(0, 900_000, latest.Readings(900_000)));
            Assert.False(latest.Scheduler.ClockAdmitted);
            Assert.Equal(0, latest.Render.Releases);
            Assert.True(latest.Scheduler.Tick(1, 1_000_000, latest.Readings(1_000_000)));
            Assert.Equal(1, latest.Render.Releases);
        });
    }

    private sealed class SingleRoute : IDisposable
    {
        private readonly MixSource game;
        private readonly BoundedBusPcmTransport bridge;
        private readonly WasapiCaptureBlockAdapter capAdapter;
        private readonly WasapiRenderBlockAdapter renderAdapter;

        public readonly TransportEndpoint Output;
        public readonly CapturePackets Capture = new(8192);
        public readonly RenderPackets Render = new();
        public readonly TransportActivityGuard Guard;
        public OwnedBusScheduler Scheduler { get; }

        public SingleRoute(long generation = 1, TransportActivityGuard? guard = null)
        {
            Guard = guard ?? new();
            game = Source("game") with { Endpoint = new("game", EndpointDirection.Capture, generation) };
            Output = new("out", EndpointDirection.Playback, generation);
            bridge = new(generation, Mono, [game]);
            capAdapter = new(bridge, game.Endpoint, Mono, Pcm16());
            renderAdapter = new(bridge, Mono, Pcm16());
            var source = new WasapiBusCaptureOwnerStep(Capture, capAdapter, game.Endpoint, Mono);
            var target = new WasapiBusRenderOwnerStep(Render, renderAdapter, Output, Mono, Pcm16());
            Scheduler = new(generation, Mono, [game], bridge, [new(game, source)], [new(Output, MixBus.Personal, target)], Guard);
        }

        public NativeClockSample[] Readings(long now) => ClockReadings(now, game.Endpoint, Output);

        public void Dispose()
        {
            Scheduler.Dispose();
            Capture.Dispose();
            Render.Dispose();
            capAdapter.Dispose();
            renderAdapter.Dispose();
            bridge.Dispose();
        }
    }

    private static TransportEndpoint OutputEndpoint(string id) => new(id, EndpointDirection.Playback, 1);

    private static NativeClockSample[] ClockReadings(long now, params TransportEndpoint[] endpoints) =>
        endpoints.Select((endpoint, index) =>
        {
            ulong position = (ulong)(now / 100_000 * 480 + index * 5031);
            return index % 2 == 0
                ? new NativeClockSample(endpoint, position, 48_000, (ulong)(now - 1000), NativePositionUnit.Frames, NativeClockQuality.Accurate)
                : new NativeClockSample(endpoint, position * 4, 192_000, (ulong)(now - 1100), NativePositionUnit.DeviceUnits, NativeClockQuality.Accurate);
        }).ToArray();

    private static bool Step(OwnedBusScheduler s, long block, long now, params TransportEndpoint[] endpoints) =>
        s.Tick(block, now, ClockReadings(now, endpoints));

    private static MixSource Source(string id, MixSourceKind kind = MixSourceKind.Game, MixLevel? stream = null) =>
        new(new(id, EndpointDirection.Capture, 1), Mono, kind, MixOrigin.Discrete,
            ChannelPolicy.Preserve, new(1), stream);

    private static WasapiPcm Pcm16() => new(1, 48000, 1, 16, 16, 2, 96000, 0, WasapiPcm.PcmSubFormat, null);

    private static void RunMta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { error = e; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)));
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed class CapturePackets(short value) : IWasapiCapturePackets
    {
        private nint memory = Create(value);
        private uint position = 480;
        public ulong Clock { get; set; } = 990_000;
        public uint PositionOffset { get; set; }
        public int ReleaseError { get; set; }
        public bool ThrowOnRelease { get; set; }
        public bool Missing { get; set; }
        public int Releases { get; private set; }
        public bool Disposed { get; private set; }

        public uint NextPacketSize() => Disposed || Missing ? 0u : 480u;
        public WasapiCaptureBuffer GetBuffer() =>
            new(0, memory, 480, 0, position + PositionOffset, Clock);
        public int ReleaseBuffer(uint frames)
        {
            if (ThrowOnRelease) throw new InvalidOperationException("Falha simulada ao liberar a captura.");
            if (frames != 480) throw new InvalidOperationException("Tamanho de captura divergente.");
            Releases++;
            position += frames;
            Clock += 100_000;
            return ReleaseError;
        }
        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            Marshal.FreeHGlobal(memory);
            memory = 0;
        }
        private static nint Create(short value)
        {
            var bytes = new byte[960];
            for (int i = 0; i < 480; i++)
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), value);
            nint memory = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            return memory;
        }
    }

    private sealed class RenderPackets : IWasapiRenderPackets
    {
        private nint memory;
        public byte[] Last { get; private set; } = [];
        public uint LastFlags { get; private set; }
        public bool ThrowOnRelease { get; set; }
        public int Releases { get; private set; }
        public bool Disposed { get; private set; }
        public nint GetBuffer(uint frames)
        {
            if (memory != 0 || frames != 480) throw new InvalidOperationException("Buffer de render inválido.");
            memory = Marshal.AllocHGlobal(960);
            return memory;
        }
        public int ReleaseBuffer(uint frames, uint flags)
        {
            if (ThrowOnRelease) throw new InvalidOperationException("Falha simulada ao liberar o render.");
            Last = new byte[checked((int)frames * 2)];
            Marshal.Copy(memory, Last, 0, Last.Length);
            Marshal.FreeHGlobal(memory);
            memory = 0;
            LastFlags = flags;
            Releases++;
            return 0;
        }
        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            if (memory != 0) Marshal.FreeHGlobal(memory);
            memory = 0;
        }
    }
}
