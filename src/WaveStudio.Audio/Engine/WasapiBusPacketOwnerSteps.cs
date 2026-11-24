using WaveStudio.Audio;

namespace WaveStudio.Audio.Engine;

// O owner da captura é criado/usado/descartado em uma única thread MTA.
// Não ativa IAudioClient, não chama Start e não cria tarefas: apenas consome
// um pacote já disponibilizado por um cliente WASAPI explicitamente autorizado.
internal sealed class WasapiBusCaptureOwnerStep : IDisposable
{
    private readonly IWasapiCapturePackets packets;
    private readonly WasapiCaptureBlockAdapter adapter;
    private readonly TransportEndpoint endpoint;
    private readonly int threadId;
    private readonly int expectedFrames;
    private long lastQpc, lastPosition = -1;
    private bool disposed, retained;

    public bool ResourcesRetained => retained;

    public WasapiBusCaptureOwnerStep(IWasapiCapturePackets packets, WasapiCaptureBlockAdapter adapter,
        TransportEndpoint endpoint, PcmFloatFormat format)
    {
        RequireMta();
        this.packets = packets ?? throw new ArgumentNullException(nameof(packets));
        this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        ArgumentNullException.ThrowIfNull(format);
        endpoint.Validate();
        if (endpoint.Direction != EndpointDirection.Capture)
            throw new ArgumentException("A fonte do owner deve ser captura.", nameof(endpoint));
        this.endpoint = endpoint;
        threadId = Environment.CurrentManagedThreadId;
        expectedFrames = format.SampleRate / 100;
    }

    // O agendador/owner externo é responsável por mapear os relógios dos fluxos
    // para o mesmo índice de bloco. O filtro de QPC aqui impede apenas timestamp
    // futuro, antigo, não monotônico ou posição regredida; não comprova drift.
    public unsafe bool TryCapture(long block, long now100ns, CancellationToken cancellation = default)
    {
        CheckOwner();
        if (disposed || retained || cancellation.IsCancellationRequested || block < 0) return false;
        if (packets.NextPacketSize() == 0) return false;
        var packet = packets.GetBuffer();
        if (packet.HResult != 0) throw new WasapiCallException("IAudioCaptureClient.GetBuffer", packet.HResult);

        bool accepted = false;
        int releaseHresult;
        try
        {
            bool goodClock = packet.Qpc100ns is > 0 and <= long.MaxValue &&
                now100ns > 0 && (long)packet.Qpc100ns > lastQpc &&
                (long)packet.Qpc100ns <= now100ns + 250_000 &&
                now100ns - (long)packet.Qpc100ns <= 20_000_000 &&
                packet.Position <= long.MaxValue && (long)packet.Position > lastPosition;
            bool validFlags = (packet.Flags & ~((uint)TransportPacketFlags.Silent)) == 0;
            if (goodClock && validFlags && packet.Frames == expectedFrames && !cancellation.IsCancellationRequested)
            {
                int count = checked((int)packet.Frames);
                var flags = (TransportPacketFlags)packet.Flags;
                int bytesPerFrame = adapter.BytesPerBlock / expectedFrames;
                ReadOnlySpan<byte> samples = packet.Data == 0 || flags == TransportPacketFlags.Silent
                    ? ReadOnlySpan<byte>.Empty
                    : new ReadOnlySpan<byte>((void*)packet.Data, checked(count * bytesPerFrame));
                if (packet.Data != 0 || flags == TransportPacketFlags.Silent)
                    accepted = adapter.TryOffer(endpoint, block, count, samples, flags);
                if (accepted)
                {
                    lastQpc = (long)packet.Qpc100ns;
                    lastPosition = (long)packet.Position;
                }
            }
        }
        finally
        {
            // ReleaseBuffer é obrigatório após GetBuffer bem-sucedido, inclusive
            // quando o conteúdo é inválido ou não será publicado no barramento.
            releaseHresult = packets.ReleaseBuffer(packet.Frames);
            if (releaseHresult != 0)
            {
                retained = true;
                adapter.FailClosed();
            }
        }
        if (releaseHresult != 0)
            throw new WasapiCallException("IAudioCaptureClient.ReleaseBuffer", releaseHresult);
        return accepted && !disposed && !cancellation.IsCancellationRequested;
    }

    public void Dispose()
    {
        CheckOwner();
        if (disposed) return;
        disposed = true;
        // Sem confirmação de ReleaseBuffer não é seguro liberar o owner COM.
        if (!retained) packets.Dispose();
    }

    private void CheckOwner()
    {
        RequireMta();
        if (Environment.CurrentManagedThreadId != threadId)
            throw new InvalidOperationException("O buffer WASAPI pertence à thread MTA de captura.");
    }

    private static void RequireMta()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA)
            throw new InvalidOperationException("Owner WASAPI exige MTA.");
    }
}

// O render conserva a mesma regra de propriedade de thread e de buffer.
// Somente prepara um pacote para IAudioRenderClient já iniciado com consentimento;
// não altera volume, dispositivo padrão ou destino virtual no Windows.
internal sealed class WasapiBusRenderOwnerStep : IDisposable
{
    private readonly IWasapiRenderPackets packets;
    private readonly WasapiRenderBlockAdapter adapter;
    private readonly int threadId;
    private readonly uint frames;
    private readonly int bytesPerBlock;
    private bool disposed, retained;

    public bool ResourcesRetained => retained;

    public WasapiBusRenderOwnerStep(IWasapiRenderPackets packets,
        WasapiRenderBlockAdapter adapter, PcmFloatFormat format, WasapiPcm native)
    {
        RequireMta();
        this.packets = packets ?? throw new ArgumentNullException(nameof(packets));
        this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(native);
        native.Validate();
        if (native.FloatFormat != format)
            throw new NotSupportedException("O formato do owner render precisa coincidir com o barramento.");
        threadId = Environment.CurrentManagedThreadId;
        frames = (uint)(format.SampleRate / 100);
        bytesPerBlock = checked((int)frames * native.BlockAlign);
    }

    public unsafe bool TryRender(MixBus bus, long block, CancellationToken cancellation = default)
    {
        CheckOwner();
        if (disposed || retained || cancellation.IsCancellationRequested || block < 0) return false;
        nint ptr = packets.GetBuffer(frames);
        bool valid = false;
        int releaseHr;
        try
        {
            if (ptr != 0 && !cancellation.IsCancellationRequested)
            {
                Span<byte> bytes = new((void*)ptr, bytesPerBlock);
                valid = adapter.TryEncode(bus, block, bytes, out MixDeliveryTicket? ticket) &&
                    ticket is { IsValid: true } && !cancellation.IsCancellationRequested;
                if (!valid) bytes.Clear();
            }
        }
        finally
        {
            // Se a sessão foi invalidada, solta o pacote como silêncio, nunca
            // como conteúdo anterior da fila. O owner nativo não reusa buffer.
            releaseHr = packets.ReleaseBuffer(frames, valid && !cancellation.IsCancellationRequested ? 0u : 2u);
            if (releaseHr != 0)
            {
                retained = true;
                adapter.FailClosed();
            }
        }
        if (releaseHr != 0)
            throw new WasapiCallException("IAudioRenderClient.ReleaseBuffer", releaseHr);
        return valid && !disposed && !cancellation.IsCancellationRequested;
    }

    public void Dispose()
    {
        CheckOwner();
        if (disposed) return;
        disposed = true;
        if (!retained) packets.Dispose();
    }

    private void CheckOwner()
    {
        RequireMta();
        if (Environment.CurrentManagedThreadId != threadId)
            throw new InvalidOperationException("O buffer WASAPI pertence à thread MTA de render.");
    }

    private static void RequireMta()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA)
            throw new InvalidOperationException("Owner WASAPI exige MTA.");
    }
}
