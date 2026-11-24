using System.Buffers.Binary;
using WaveStudio.Audio;

namespace WaveStudio.Audio.Engine;

// Adapta pacotes WASAPI já adquiridos por um owner autorizado para os blocos do mixer.
// Não abre IAudioClient, não verifica relógio nativo e não usa ponteiro COM: o chamador
// deve verificar timestamp/identidade e liberar o buffer no seu próprio owner MTA.
internal sealed class WasapiCaptureBlockAdapter : IDisposable
{
    private readonly BoundedBusPcmTransport bridge;
    private readonly TransportEndpoint endpoint;
    private readonly PcmFloatFormat format;
    private readonly PcmEncoding encoding;
    private readonly float[] scratch;
    private readonly int framesPerBlock;
    private readonly int bytesPerBlock;
    private int closed, processing;

    internal int BytesPerBlock => bytesPerBlock;
    internal void FailClosed() { Interlocked.Exchange(ref closed, 1); bridge.Dispose(); }

    public WasapiCaptureBlockAdapter(BoundedBusPcmTransport bridge, TransportEndpoint endpoint,
        PcmFloatFormat sourceFormat, WasapiPcm nativeFormat)
    {
        this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        ArgumentNullException.ThrowIfNull(sourceFormat);
        ArgumentNullException.ThrowIfNull(nativeFormat);
        endpoint.Validate();
        if (endpoint.Direction != EndpointDirection.Capture)
            throw new ArgumentException("A fonte deve ser um endpoint de captura.", nameof(endpoint));
        encoding = nativeFormat.Validate();
        if (nativeFormat.FloatFormat != sourceFormat)
            throw new NotSupportedException("O pacote WASAPI deve corresponder ao formato da fonte; converta antes de alimentar a ponte.");
        this.endpoint = endpoint;
        format = sourceFormat;
        framesPerBlock = sourceFormat.SampleRate / 100;
        bytesPerBlock = checked(framesPerBlock * nativeFormat.BlockAlign);
        scratch = new float[checked(framesPerBlock * sourceFormat.Channels)];
    }

    // Um owner por fonte. Um bloco incompleto, descontinuado, com timestamp inválido
    // ou de geração antiga não entra na fila. SILENT representa frames de silêncio
    // mesmo se o driver não disponibilizar endereço para leitura.
    public bool TryOffer(TransportEndpoint observed, long block, int frames, ReadOnlySpan<byte> bytes,
        TransportPacketFlags flags = TransportPacketFlags.None)
    {
        if (Volatile.Read(ref closed) != 0 || observed != endpoint || block < 0 ||
            frames != framesPerBlock || (flags & ~TransportPacketFlags.Silent) != 0 ||
            Interlocked.CompareExchange(ref processing, 1, 0) != 0) return false;
        try
        {
            bool silent = (flags & TransportPacketFlags.Silent) != 0;
            if (silent)
            {
                // Um driver pode expor ponteiro nulo ou uma área de tamanho normal.
                if (bytes.Length != 0 && bytes.Length != bytesPerBlock) return false;
                scratch.AsSpan().Clear();
            }
            else
            {
                if (bytes.Length != bytesPerBlock) return false;
                PcmFloatConverter.Decode(bytes, scratch, format, encoding);
                for (int i = 0; i < scratch.Length; i++)
                {
                    float value = scratch[i];
                    scratch[i] = float.IsNormal(value) ? Math.Clamp(value, -1f, 1f) : 0f;
                }
            }
            return Volatile.Read(ref closed) == 0 && bridge.TryOfferCapture(endpoint, block, scratch);
        }
        finally { Volatile.Write(ref processing, 0); }
    }

    public void Dispose() => Interlocked.Exchange(ref closed, 1);
}

// Traduz blocos Personal/Stream já mixados para o formato WASAPI autorizado.
// Nenhum buffer chega ao dispositivo aqui: um owner MTA futuro deve verificar o
// ticket novamente imediatamente antes de entregar os bytes ao render nativo.
internal sealed class WasapiRenderBlockAdapter : IDisposable
{
    private readonly BoundedBusPcmTransport bridge;
    private readonly PcmEncoding encoding;
    private readonly float[] scratch;
    private readonly int bytesPerBlock;
    private int closed, processing;

    internal void FailClosed() { Interlocked.Exchange(ref closed, 1); bridge.Dispose(); }

    public WasapiRenderBlockAdapter(BoundedBusPcmTransport bridge,
        PcmFloatFormat mixFormat, WasapiPcm nativeFormat)
    {
        this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        ArgumentNullException.ThrowIfNull(mixFormat);
        ArgumentNullException.ThrowIfNull(nativeFormat);
        encoding = nativeFormat.Validate();
        if (nativeFormat.FloatFormat != mixFormat)
            throw new NotSupportedException("A saída WASAPI requer taxa e canais iguais aos barramentos; adapte o formato antes do render.");
        scratch = new float[checked(mixFormat.SampleRate / 100 * mixFormat.Channels)];
        bytesPerBlock = checked(mixFormat.SampleRate / 100 * nativeFormat.BlockAlign);
    }

    public bool TryEncode(MixBus bus, long block, Span<byte> bytes, out MixDeliveryTicket? ticket) =>
        Encode(block, bytes, null, bus, out ticket);

    public bool TryEncodeExternal(MixExternalDestination destination, long block, Span<byte> bytes,
        out MixDeliveryTicket? ticket)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return Encode(block, bytes, destination, null, out ticket);
    }

    private bool Encode(long block, Span<byte> bytes, MixExternalDestination? destination,
        MixBus? bus, out MixDeliveryTicket? ticket)
    {
        ticket = null;
        if (Volatile.Read(ref closed) != 0 || block < 0 || bytes.Length != bytesPerBlock ||
            (bus.HasValue && !Enum.IsDefined(bus.Value)) ||
            Interlocked.CompareExchange(ref processing, 1, 0) != 0)
        {
            bytes.Clear();
            return false;
        }
        try
        {
            bool read = destination is not null
                ? bridge.TryReadExternal(destination, block, scratch, out ticket)
                : bridge.TryReadOutput(bus!.Value, block, scratch, out ticket);
            if (!read || ticket is not { IsValid: true })
            {
                bytes.Clear();
                ticket = null;
                return false;
            }

            for (int i = 0; i < scratch.Length; i++)
            {
                float value = scratch[i];
                float sanitized = float.IsNormal(value) ? Math.Clamp(value, -1f, 1f) : 0f;
                if (encoding == PcmEncoding.Pcm16LittleEndian)
                {
                    int scaled = (int)MathF.Round(sanitized * 32768f);
                    BinaryPrimitives.WriteInt16LittleEndian(bytes[(i * 2)..], (short)Math.Clamp(scaled, short.MinValue, short.MaxValue));
                }
                else
                    BinaryPrimitives.WriteSingleLittleEndian(bytes[(i * 4)..], sanitized);
            }

            if (Volatile.Read(ref closed) != 0 || !ticket.IsValid)
            {
                bytes.Clear();
                ticket = null;
                return false;
            }
            return true;
        }
        finally { Volatile.Write(ref processing, 0); }
    }

    public void Dispose() => Interlocked.Exchange(ref closed, 1);
}
