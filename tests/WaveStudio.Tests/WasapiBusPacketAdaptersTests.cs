using System.Buffers.Binary;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiBusPacketAdaptersTests
{
    private static readonly PcmFloatFormat Mono = new(48000, 1);
    private static readonly MixLevel On = new(1);

    [Fact]
    public void Pcm16CaptureReachesIndependentFloatRenderBuffers()
    {
        var game = Source("game", Mono, stream: On);
        using var bridge = new BoundedBusPcmTransport(1, Mono, [game]);
        using var capture = new WasapiCaptureBlockAdapter(bridge, game.Endpoint, Mono, Pcm16(48000, 1));
        using var render = new WasapiRenderBlockAdapter(bridge, Mono, Float32(48000, 1));
        var mix = new BusMixSession(bridge);
        mix.Start(1, Mono, [game], cancellation: TestContext.Current.CancellationToken);

        byte[] native = new byte[960];
        for (int i = 0; i < 480; i++)
            BinaryPrimitives.WriteInt16LittleEndian(native.AsSpan(i * 2), 8192);
        Assert.True(capture.TryOffer(game.Endpoint, 0, 480, native));
        Assert.True(mix.Process(1, 0));

        byte[] personal = new byte[1920], stream = new byte[1920];
        Assert.True(render.TryEncode(MixBus.Personal, 0, personal, out var first));
        Assert.True(render.TryEncode(MixBus.Stream, 0, stream, out var second));
        Assert.Same(first, second);
        Assert.Equal(personal, stream);
        for (int i = 0; i < 480; i++)
            Assert.Equal(.25f, BinaryPrimitives.ReadSingleLittleEndian(personal.AsSpan(i * 4)));
    }

    [Fact]
    public void NativeFloatInputSanitizesNanInfinityAndOutOfRange()
    {
        var game = Source("game", Mono);
        using var bridge = new BoundedBusPcmTransport(1, Mono, [game]);
        using var capture = new WasapiCaptureBlockAdapter(bridge, game.Endpoint, Mono, Float32(48000, 1));
        using var render = new WasapiRenderBlockAdapter(bridge, Mono, Float32(48000, 1));
        var mix = new BusMixSession(bridge);
        mix.Start(1, Mono, [game], cancellation: TestContext.Current.CancellationToken);

        byte[] bytes = new byte[1920];
        for (int i = 0; i < 480; i++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), .5f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0), float.NaN);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), float.PositiveInfinity);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8), -2f);
        Assert.True(capture.TryOffer(game.Endpoint, 0, 480, bytes));
        Assert.True(mix.Process(1, 0));
        byte[] output = new byte[1920];
        Assert.True(render.TryEncode(MixBus.Personal, 0, output, out _));
        Assert.Equal(0f, BinaryPrimitives.ReadSingleLittleEndian(output));
        Assert.Equal(0f, BinaryPrimitives.ReadSingleLittleEndian(output.AsSpan(4)));
        Assert.Equal(-1f, BinaryPrimitives.ReadSingleLittleEndian(output.AsSpan(8)));
        Assert.Equal(.5f, BinaryPrimitives.ReadSingleLittleEndian(output.AsSpan(16)));
    }

    [Fact]
    public void SilentFlagAcceptsNullPayloadButRejectsCorruptNativeFlags()
    {
        var mic = Source("mic", Mono, MixSourceKind.Microphone);
        using var bridge = new BoundedBusPcmTransport(1, Mono, [mic]);
        using var capture = new WasapiCaptureBlockAdapter(bridge, mic.Endpoint, Mono, Pcm16(48000, 1));
        using var render = new WasapiRenderBlockAdapter(bridge, Mono, Pcm16(48000, 1));
        var mix = new BusMixSession(bridge);
        mix.Start(1, Mono, [mic], cancellation: TestContext.Current.CancellationToken);

        Assert.False(capture.TryOffer(mic.Endpoint, 0, 480, [], TransportPacketFlags.Discontinuity));
        Assert.False(capture.TryOffer(mic.Endpoint, 0, 480, [], TransportPacketFlags.TimestampError));
        Assert.True(capture.TryOffer(mic.Endpoint, 0, 480, [], TransportPacketFlags.Silent));
        Assert.True(mix.Process(1, 0));
        byte[] outBytes = new byte[960];
        Assert.True(render.TryEncode(MixBus.Personal, 0, outBytes, out _));
        Assert.All(outBytes, value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void CaptureValidatesEndpointFrameSizeAndBytesBeforeQueueing()
    {
        var game = Source("game", Mono);
        using var bridge = new BoundedBusPcmTransport(1, Mono, [game]);
        using var capture = new WasapiCaptureBlockAdapter(bridge, game.Endpoint, Mono, Pcm16(48000, 1));
        var packet = new byte[960];
        Assert.False(capture.TryOffer(game.Endpoint with { Generation = 2 }, 0, 480, packet));
        Assert.False(capture.TryOffer(new("other", EndpointDirection.Capture, 1), 0, 480, packet));
        Assert.False(capture.TryOffer(game.Endpoint, 0, 479, packet));
        Assert.False(capture.TryOffer(game.Endpoint, 0, 480, packet.AsSpan(1)));
        Assert.False(capture.TryOffer(game.Endpoint, 0, 480, packet, (TransportPacketFlags)8));
        Assert.True(capture.TryOffer(game.Endpoint, 0, 480, packet));
        Assert.False(capture.TryOffer(game.Endpoint, 0, 480, packet));
    }

    [Fact]
    public void Pcm16OutputQuantizesClippedMixedSamplesSafely()
    {
        var game = Source("game", Mono);
        var aux = Source("aux", Mono, MixSourceKind.Auxiliary);
        using var bridge = new BoundedBusPcmTransport(1, Mono, [game, aux]);
        using var render = new WasapiRenderBlockAdapter(bridge, Mono, Pcm16(48000, 1));
        var mix = new BusMixSession(bridge);
        mix.Start(1, Mono, [game, aux], cancellation: TestContext.Current.CancellationToken);
        Assert.True(bridge.TryOfferCapture(game.Endpoint, 0, Repeat(.75f)));
        Assert.True(bridge.TryOfferCapture(aux.Endpoint, 0, Repeat(.75f)));
        Assert.True(mix.Process(1, 0));

        byte[] output = new byte[960];
        Assert.True(render.TryEncode(MixBus.Personal, 0, output, out _));
        Assert.All(Enumerable.Range(0, 480), index =>
            Assert.Equal(short.MaxValue, BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(index * 2))));
    }

    [Fact]
    public void ObsoleteTicketsAndClosedBridgeNeverDeliverNativeBytes()
    {
        var game = Source("game", Mono);
        var bridge = new BoundedBusPcmTransport(1, Mono, [game]);
        using var capture = new WasapiCaptureBlockAdapter(bridge, game.Endpoint, Mono, Pcm16(48000, 1));
        using var render = new WasapiRenderBlockAdapter(bridge, Mono, Pcm16(48000, 1));
        var mix = new BusMixSession(bridge);
        mix.Start(1, Mono, [game], cancellation: TestContext.Current.CancellationToken);
        Assert.True(capture.TryOffer(game.Endpoint, 0, 480, new byte[960]));
        Assert.True(mix.Process(1, 0));
        bridge.Dispose();
        byte[] output = Enumerable.Repeat((byte)0x5A, 960).ToArray();
        Assert.False(render.TryEncode(MixBus.Personal, 0, output, out var ticket));
        Assert.Null(ticket);
        Assert.All(output, x => Assert.Equal((byte)0, x));
        Assert.False(capture.TryOffer(game.Endpoint, 1, 480, new byte[960]));
    }

    [Fact]
    public void ExternalNativeOutputPreservesSensitiveAudioPolicy()
    {
        var mic = Source("mic", Mono, MixSourceKind.Microphone, stream: On);
        var destination = new MixExternalDestination("obs");
        using var bridge = new BoundedBusPcmTransport(1, Mono, [mic], destination);
        using var render = new WasapiRenderBlockAdapter(bridge, Mono, Float32(48000, 1));
        var mix = new BusMixSession(bridge);
        mix.Start(1, Mono, [mic], destination, TestContext.Current.CancellationToken);
        Assert.True(bridge.TryOfferCapture(mic.Endpoint, 0, Repeat(.5f)));
        Assert.True(mix.Process(1, 0));

        byte[] output = new byte[1920];
        Assert.False(render.TryEncodeExternal(new("outro"), 0, output, out _));
        Assert.True(render.TryEncodeExternal(destination, 0, output, out _));
        Assert.All(output, x => Assert.Equal((byte)0, x));
    }

    [Fact]
    public void NativeFormatNegotiationRefusesUnsupportedRateOrLayout()
    {
        var game = Source("game", Mono);
        using var bridge = new BoundedBusPcmTransport(1, Mono, [game]);
        Assert.Throws<NotSupportedException>(() =>
            new WasapiCaptureBlockAdapter(bridge, game.Endpoint, Mono, Pcm16(44100, 1)));
        Assert.Throws<NotSupportedException>(() =>
            new WasapiRenderBlockAdapter(bridge, Mono, Float32(48000, 2)));
        Assert.Throws<NotSupportedException>(() =>
            new WasapiRenderBlockAdapter(bridge, Mono, Pcm16(96000, 1)));
    }

    private static MixSource Source(string id, PcmFloatFormat format, MixSourceKind kind = MixSourceKind.Game,
        MixLevel? stream = null) =>
        new(new(id, EndpointDirection.Capture, 1), format, kind, MixOrigin.Discrete,
            ChannelPolicy.Preserve, On, stream);

    private static float[] Repeat(float value)
    {
        var result = new float[480];
        Array.Fill(result, value);
        return result;
    }

    private static WasapiPcm Pcm16(int rate, int channels)
    {
        int align = channels * 2;
        return new(1, rate, channels, 16, 16, align, rate * align, 0,
            WasapiPcm.PcmSubFormat, null);
    }

    private static WasapiPcm Float32(int rate, int channels)
    {
        int align = channels * 4;
        return new(3, rate, channels, 32, 32, align, rate * align, 0,
            WasapiPcm.FloatSubFormat, null);
    }
}
