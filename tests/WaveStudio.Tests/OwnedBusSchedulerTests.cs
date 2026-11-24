using System.Buffers.Binary;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class OwnedBusSchedulerTests
{
    private static readonly PcmFloatFormat Mono = new(48000, 1);

    [Fact]
    public void OneMtaClockMixesTwoSourcesIntoIndependentPersonalAndStreamBuffers()
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
            using var personalOwner = new WasapiBusRenderOwnerStep(personalPackets, personalAdapter, Mono, Pcm16());
            using var streamOwner = new WasapiBusRenderOwnerStep(streamPackets, streamAdapter, Mono, Pcm16());
            var guard = new TransportActivityGuard();

            using var scheduler = new OwnedBusScheduler(1, Mono, [game, mic], bridge,
                [new(game, gameOwner), new(mic, micOwner)],
                [new(new("headset", EndpointDirection.Playback, 1), MixBus.Personal, personalOwner),
                 new(new("stream", EndpointDirection.Playback, 1), MixBus.Stream, streamOwner)],
                guard, TestContext.Current.CancellationToken);
            Assert.True(scheduler.Tick(0, 1_000_000));
            Assert.Equal(1, gamePackets.Releases);
            Assert.Equal(1, micPackets.Releases);
            Assert.Equal(0u, personalPackets.LastFlags);
            Assert.Equal(0u, streamPackets.LastFlags);
            Assert.Equal((short)12288, BinaryPrimitives.ReadInt16LittleEndian(personalPackets.Last));
            Assert.Equal((short)8192, BinaryPrimitives.ReadInt16LittleEndian(streamPackets.Last));
            Assert.True(scheduler.Tick(1, 1_100_000));
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
            using var render = new WasapiBusRenderOwnerStep(renderPackets, renderAdapter, Mono, Pcm16());
            var guard = new TransportActivityGuard();
            using var scheduler = new OwnedBusScheduler(1, Mono, [game], bridge,
                [new(game, capture)],
                [new(new("out", EndpointDirection.Playback, 1), MixBus.Personal, render)],
                guard, TestContext.Current.CancellationToken);
            Assert.False(scheduler.Tick(0, 1_000_000));
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
            using var render = new WasapiBusRenderOwnerStep(renderPackets, renderAdapter, Mono, Pcm16());
            using var scheduler = new OwnedBusScheduler(1, Mono, [game], bridge,
                [new(game, capture)],
                [new(new("out", EndpointDirection.Playback, 1), MixBus.Personal, render)],
                new TransportActivityGuard(), TestContext.Current.CancellationToken);
            Assert.True(scheduler.Tick(0, 1_000_000));
            Assert.True(scheduler.Tick(1, 1_100_000));
            Assert.Equal(2, renderPackets.Releases);
            Assert.False(scheduler.Tick(3, 1_300_000));
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
            using var render = new WasapiBusRenderOwnerStep(renderPackets, renderAdapter, Mono, Pcm16());
            using var cancel = new CancellationTokenSource();
            using var scheduler = new OwnedBusScheduler(1, Mono, [game], bridge,
                [new(game, capture)],
                [new(new("out", EndpointDirection.Playback, 1), MixBus.Personal, render)],
                new TransportActivityGuard(), cancel.Token);
            cancel.Cancel();
            Assert.False(scheduler.Tick(0, 1_000_000));
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
            using var render = new WasapiBusRenderOwnerStep(new RenderPackets(), renderAdapter, Mono, Pcm16());
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
        public int Releases { get; private set; }
        public bool Disposed { get; private set; }

        public uint NextPacketSize() => Disposed ? 0 : 480;
        public WasapiCaptureBuffer GetBuffer() =>
            new(0, memory, 480, 0, position, Clock);
        public int ReleaseBuffer(uint frames)
        {
            if (frames != 480) throw new InvalidOperationException("Tamanho de captura divergente.");
            Releases++;
            position += frames;
            Clock += 100_000;
            return 0;
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
