using System.Buffers.Binary;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiBusPacketOwnerStepsTests
{
    private static readonly PcmFloatFormat Mono = new(48000, 1);
    private static readonly MixLevel On = new(1);

    [Fact]
    public void CaptureOwnerReadsCopiesAndReleasesNativePacket()
    {
        RunMta(() =>
        {
            var game = Source("game");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [game]);
            using var capture = new WasapiCaptureBlockAdapter(bridge, game.Endpoint, Mono, Pcm16());
            var packets = new CapturePackets(8192);
            using var owner = new WasapiBusCaptureOwnerStep(packets, capture, game.Endpoint, Mono);
            var mix = new BusMixSession(bridge);
            mix.Start(1, Mono, [game]);

            Assert.True(owner.TryCapture(0, 1_000_000, TestContext.Current.CancellationToken));
            Assert.Equal(1, packets.Releases);
            Assert.Equal(480u, packets.ReleasedFrames);
            Assert.True(mix.Process(1, 0));
            float[] output = new float[480];
            Assert.True(bridge.TryReadOutput(MixBus.Personal, 0, output, out _));
            Assert.All(output, sample => Assert.Equal(.25f, sample));
        });
    }

    [Fact]
    public void FutureTimestampIsRejectedButAcquiredBufferAlwaysReleased()
    {
        RunMta(() =>
        {
            var game = Source("game");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [game]);
            using var capture = new WasapiCaptureBlockAdapter(bridge, game.Endpoint, Mono, Pcm16());
            var packets = new CapturePackets(8192) { Qpc100ns = 9_000_000 };
            using var owner = new WasapiBusCaptureOwnerStep(packets, capture, game.Endpoint, Mono);
            Assert.False(owner.TryCapture(0, 1_000_000, TestContext.Current.CancellationToken));
            Assert.Equal(1, packets.Releases);
            Assert.False(bridge.TryOfferCapture(game.Endpoint, 0, new float[479]));
        });
    }

    [Fact]
    public void NativeTimestampFlagAndShortFrameAreRejectedAndReleased()
    {
        RunMta(() =>
        {
            var game = Source("game");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [game]);
            using var capture = new WasapiCaptureBlockAdapter(bridge, game.Endpoint, Mono, Pcm16());
            var packets = new CapturePackets(8192) { Flags = (uint)TransportPacketFlags.TimestampError };
            using var owner = new WasapiBusCaptureOwnerStep(packets, capture, game.Endpoint, Mono);
            Assert.False(owner.TryCapture(0, 1_000_000, TestContext.Current.CancellationToken));
            Assert.Equal(1, packets.Releases);
            packets.Flags = 0;
            packets.Frames = 479;
            packets.Released = false;
            Assert.False(owner.TryCapture(1, 1_000_000, TestContext.Current.CancellationToken));
            Assert.Equal(2, packets.Releases);
        });
    }

    [Fact]
    public void FailedCaptureReleaseRetainsOwnerAndInvalidatesBridge()
    {
        RunMta(() =>
        {
            var game = Source("game");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [game]);
            using var capture = new WasapiCaptureBlockAdapter(bridge, game.Endpoint, Mono, Pcm16());
            var packets = new CapturePackets(8192) { ReleaseResult = unchecked((int)0x88890004) };
            var owner = new WasapiBusCaptureOwnerStep(packets, capture, game.Endpoint, Mono);
            Assert.Throws<WasapiCallException>(() => owner.TryCapture(0, 1_000_000, TestContext.Current.CancellationToken));
            Assert.True(owner.ResourcesRetained);
            Assert.False(bridge.TryOfferCapture(game.Endpoint, 1, new float[480]));
            owner.Dispose();
            Assert.False(packets.Disposed); // Buffer não confirmado: não liberar COM prematuramente.
            packets.Dispose();
        });
    }

    [Fact]
    public void RenderOwnerSubmitsSilenceWhenNoValidBlockExists()
    {
        RunMta(() =>
        {
            var game = Source("game");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [game]);
            using var render = new WasapiRenderBlockAdapter(bridge, Mono, Pcm16());
            var packets = new RenderPackets();
            using var owner = new WasapiBusRenderOwnerStep(packets, render, Mono, Pcm16());
            Assert.False(owner.TryRender(MixBus.Personal, 0, TestContext.Current.CancellationToken));
            Assert.Equal(2u, packets.ReleaseFlags);
            Assert.Equal(1, packets.Releases);
            Assert.All(packets.Last, x => Assert.Equal((byte)0, x));
        });
    }

    [Fact]
    public void RenderOwnerEncodesPcm16BeforeConfirmedRelease()
    {
        RunMta(() =>
        {
            var game = Source("game");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [game]);
            using var render = new WasapiRenderBlockAdapter(bridge, Mono, Pcm16());
            var packets = new RenderPackets();
            using var owner = new WasapiBusRenderOwnerStep(packets, render, Mono, Pcm16());
            var mix = new BusMixSession(bridge);
            mix.Start(1, Mono, [game]);
            float[] input = new float[480];
            Array.Fill(input, .25f);
            Assert.True(bridge.TryOfferCapture(game.Endpoint, 0, input));
            Assert.True(mix.Process(1, 0));
            Assert.True(owner.TryRender(MixBus.Personal, 0, TestContext.Current.CancellationToken));
            Assert.Equal(0u, packets.ReleaseFlags);
            Assert.Equal(1, packets.Releases);
            Assert.Equal((short)8192, BinaryPrimitives.ReadInt16LittleEndian(packets.Last));
        });
    }

    [Fact]
    public void ReleaseFailureInRenderQuarantinesBuffersAndRetainsResource()
    {
        RunMta(() =>
        {
            var game = Source("game");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [game]);
            using var render = new WasapiRenderBlockAdapter(bridge, Mono, Pcm16());
            var packets = new RenderPackets { ReleaseResult = unchecked((int)0x88890004) };
            var owner = new WasapiBusRenderOwnerStep(packets, render, Mono, Pcm16());
            Assert.Throws<WasapiCallException>(() => owner.TryRender(MixBus.Personal, 0, TestContext.Current.CancellationToken));
            Assert.True(owner.ResourcesRetained);
            Assert.False(bridge.TryOfferCapture(game.Endpoint, 0, new float[480]));
            owner.Dispose();
            Assert.False(packets.Disposed);
            packets.Dispose();
        });
    }

    [Fact]
    public void StopAndCancellationPreventUnwantedRender()
    {
        RunMta(() =>
        {
            var game = Source("game");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [game]);
            using var render = new WasapiRenderBlockAdapter(bridge, Mono, Pcm16());
            var packets = new RenderPackets();
            using var owner = new WasapiBusRenderOwnerStep(packets, render, Mono, Pcm16());
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.False(owner.TryRender(MixBus.Personal, 0, canceled.Token));
            Assert.Equal(0, packets.Releases);
            bridge.Dispose();
            Assert.False(owner.TryRender(MixBus.Personal, 0, TestContext.Current.CancellationToken));
            Assert.Equal(1, packets.Releases);
            Assert.Equal(2u, packets.ReleaseFlags);
        });
    }

    private static MixSource Source(string id) =>
        new(new(id, EndpointDirection.Capture, 1), Mono, MixSourceKind.Game,
            MixOrigin.Discrete, ChannelPolicy.Preserve, On);

    private static WasapiPcm Pcm16() =>
        new(1, 48000, 1, 16, 16, 2, 96000, 0, WasapiPcm.PcmSubFormat, null);

    private static void RunMta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { failure = e; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "O teste MTA não terminou no prazo.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class CapturePackets : IWasapiCapturePackets
    {
        private nint memory;
        public uint Frames { get; set; } = 480;
        public uint Flags { get; set; }
        public ulong Position { get; set; } = 480;
        public ulong Qpc100ns { get; set; } = 999_000;
        public int ReleaseResult { get; set; }
        public int Releases { get; private set; }
        public uint ReleasedFrames { get; private set; }
        public bool Released { get; set; }
        public bool Disposed { get; private set; }

        public CapturePackets(short amplitude)
        {
            byte[] bytes = new byte[960];
            for (int i = 0; i < 480; i++)
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), amplitude);
            memory = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, memory, bytes.Length);
        }
        public uint NextPacketSize() => Disposed || Released ? 0 : Frames;
        public WasapiCaptureBuffer GetBuffer() => new(0, memory, Frames, Flags, Position, Qpc100ns);
        public int ReleaseBuffer(uint framesRead)
        {
            Releases++;
            ReleasedFrames = framesRead;
            Released = true;
            return ReleaseResult;
        }
        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            Marshal.FreeHGlobal(memory);
            memory = 0;
        }
    }

    private sealed class RenderPackets : IWasapiRenderPackets
    {
        private nint memory;
        public int Releases { get; private set; }
        public int ReleaseResult { get; set; }
        public uint ReleaseFlags { get; private set; }
        public bool Disposed { get; private set; }
        public byte[] Last { get; private set; } = [];
        public nint GetBuffer(uint frames)
        {
            if (memory != 0) throw new InvalidOperationException("Buffer anterior ainda aberto.");
            memory = Marshal.AllocHGlobal(checked((int)frames * 2));
            byte[] nonzero = Enumerable.Repeat((byte)0xA5, checked((int)frames * 2)).ToArray();
            Marshal.Copy(nonzero, 0, memory, nonzero.Length);
            return memory;
        }
        public int ReleaseBuffer(uint framesWritten, uint flags)
        {
            Last = new byte[checked((int)framesWritten * 2)];
            Marshal.Copy(memory, Last, 0, Last.Length);
            ReleaseFlags = flags;
            Releases++;
            return ReleaseResult;
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
