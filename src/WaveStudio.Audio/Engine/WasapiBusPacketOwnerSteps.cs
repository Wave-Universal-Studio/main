using WaveStudio.Audio;

namespace WaveStudio.Audio.Engine;

// O owner da captura é criado/usado/descartado em uma única thread MTA.
// Não ativa IAudioClient, não chama Start e não cria tarefas: apenas consome
// um pacote já disponibilizado por um cliente WASAPI explicitamente autorizado.
internal sealed class WasapiBusCaptureOwnerStep : IWasapiBusOwner
{
    private readonly IWasapiCapturePackets packets;
    private readonly WasapiBusNativeBinding<IWasapiCapturePackets>? nativeBinding;
    private readonly WasapiCaptureBlockAdapter adapter;
    private readonly TransportEndpoint endpoint;
    private readonly int threadId;
    private readonly int expectedFrames;
    private ulong lastQpc, nextPosition;
    internal TransportEndpoint Endpoint => endpoint;
    internal PcmFloatFormat Format { get; }
    internal BoundedBusPcmTransport Bridge => adapter.Bridge;
    internal bool IsAvailableOnOwner => !disposed && !retained && Environment.CurrentManagedThreadId == threadId;
    private bool disposed, retained;

    public bool ResourcesRetained => retained;

    public WasapiBusCaptureOwnerStep(IWasapiCapturePackets packets, WasapiCaptureBlockAdapter adapter,
        TransportEndpoint endpoint, PcmFloatFormat format) : this(packets, adapter, endpoint, format, null) { }

    private WasapiBusCaptureOwnerStep(IWasapiCapturePackets packets, WasapiCaptureBlockAdapter adapter,
        TransportEndpoint endpoint, PcmFloatFormat format, WasapiBusNativeBinding<IWasapiCapturePackets>? binding)
    {
        RequireMta();
        if (binding is null && packets is IWasapiNativePacketService)
            throw new ArgumentException("Captura nativa exige o vínculo do clock do mesmo cliente.", nameof(packets));
        nativeBinding = binding;
        this.packets = packets ?? throw new ArgumentNullException(nameof(packets));
        this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        ArgumentNullException.ThrowIfNull(format);
        endpoint.Validate();
        if (endpoint.Direction != EndpointDirection.Capture || adapter.Endpoint != endpoint)
            throw new ArgumentException("O adaptador precisa corresponder ao endpoint de captura do owner.", nameof(endpoint));
        if (adapter.Format != format)
            throw new ArgumentException("O adaptador precisa corresponder ao formato da fonte.", nameof(format));
        this.endpoint = endpoint;
        threadId = Environment.CurrentManagedThreadId;
        Format = format;
        expectedFrames = format.SampleRate / 100;
    }

    public static WasapiBusCaptureOwnerStep Attach(IWasapiBoundStreamClient client, WasapiCaptureBlockAdapter adapter,
        TransportEndpoint endpoint, PcmFloatFormat format)
    {
        RequireMta();
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(adapter);
        endpoint.Validate();
        if (client.Endpoint != endpoint || endpoint.Direction != EndpointDirection.Capture ||
            adapter.Endpoint != endpoint || adapter.Format != format || client.PreparedFormat != adapter.NativeFormat)
            throw new ArgumentException("Cliente, adaptador, endpoint e formato de captura precisam coincidir.");

        var binding = WasapiBusNativeBinding<IWasapiCapturePackets>.Open(client, static ready => ready.OpenCapture());
        try { return new(binding.Packets, adapter, endpoint, format, binding); }
        catch { binding.Dispose(); throw; }
    }

    internal bool HasNativeClock => nativeBinding is not null;

    internal NativeClockSample ReadNativeClock()
    {
        CheckOwner();
        if (!IsAvailableOnOwner || nativeBinding is null) throw new InvalidOperationException("Clock de captura indisponível.");
        return nativeBinding.ReadClock();
    }

    // A posição do pacote está em frames de captura, separada das unidades do clock do cliente.
    // A continuidade é exigida antes de publicar o bloco para a ponte PCM.
    public unsafe bool TryCapture(long block, long now100ns, CancellationToken cancellation = default,
        ulong maximumPacketAge100ns = (ulong)OwnedBusClockAdmission.MaximumGap100ns)
    {
        CheckOwner();
        if (disposed || retained || cancellation.IsCancellationRequested || block < 0) return false;
        if (packets.NextPacketSize() == 0) return false;
        var packet = packets.GetBuffer();
        if (packet.HResult != 0) throw new WasapiCallException("IAudioCaptureClient.GetBuffer", packet.HResult);

        bool accepted = false;
        int releaseHresult = -1;
        try
        {
            bool goodClock = now100ns > 0 && packet.Qpc100ns > lastQpc &&
                packet.Qpc100ns <= (ulong)now100ns && (ulong)now100ns - packet.Qpc100ns <= maximumPacketAge100ns &&
                packet.Position <= ulong.MaxValue - packet.Frames &&
                (lastQpc == 0 || packet.Position == nextPosition);
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
                    lastQpc = packet.Qpc100ns;
                    nextPosition = packet.Position + packet.Frames;
                }
            }
        }
        finally
        {
            // ReleaseBuffer é obrigatório após GetBuffer bem-sucedido, inclusive
            // quando o conteúdo é inválido ou não será publicado no barramento.
            try { releaseHresult = packets.ReleaseBuffer(packet.Frames); }
            finally
            {
                if (releaseHresult != 0)
                {
                    retained = true;
                    adapter.FailClosed();
                }
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
        // Sem confirmação de ReleaseBuffer os filhos continuam retidos junto do cliente.
        DisposeServices();
    }

    private void DisposeServices()
    {
        if (retained) return;
        try
        {
            if (nativeBinding is null) packets.Dispose();
            else nativeBinding.Dispose();
        }
        catch
        {
            retained = true;
            throw;
        }
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
internal sealed class WasapiBusRenderOwnerStep : IWasapiBusOwner
{
    private readonly IWasapiRenderPackets packets;
    private readonly WasapiBusNativeBinding<IWasapiRenderPackets>? nativeBinding;
    private readonly WasapiRenderBlockAdapter adapter;
    private readonly int threadId;
    private readonly uint frames;
    private readonly int bytesPerBlock;
    private bool disposed, retained;
    internal PcmFloatFormat Format { get; }

    public bool ResourcesRetained => retained;

    internal TransportEndpoint Endpoint { get; }
    internal BoundedBusPcmTransport Bridge => adapter.Bridge;
    internal bool IsAvailableOnOwner => !disposed && !retained && Environment.CurrentManagedThreadId == threadId;

    public WasapiBusRenderOwnerStep(IWasapiRenderPackets packets,
        WasapiRenderBlockAdapter adapter, TransportEndpoint endpoint, PcmFloatFormat format, WasapiPcm native)
        : this(packets, adapter, endpoint, format, native, null) { }

    private WasapiBusRenderOwnerStep(IWasapiRenderPackets packets, WasapiRenderBlockAdapter adapter, TransportEndpoint endpoint,
        PcmFloatFormat format, WasapiPcm native, WasapiBusNativeBinding<IWasapiRenderPackets>? binding)
    {
        RequireMta();
        if (binding is null && packets is IWasapiNativePacketService)
            throw new ArgumentException("Render nativo exige o vínculo do clock do mesmo cliente.", nameof(packets));
        nativeBinding = binding;
        this.packets = packets ?? throw new ArgumentNullException(nameof(packets));
        this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(native);
        endpoint.Validate();
        if (endpoint.Direction != EndpointDirection.Playback)
            throw new ArgumentException("O endpoint de render precisa ser de reprodução.", nameof(endpoint));
        Endpoint = endpoint;
        native.Validate();
        if (adapter.Format != format || native.FloatFormat != format || adapter.NativeFormat != native)
            throw new NotSupportedException("O formato do owner render precisa coincidir com o barramento.");
        threadId = Environment.CurrentManagedThreadId;
        Format = format;
        frames = (uint)(format.SampleRate / 100);
        bytesPerBlock = checked((int)frames * native.BlockAlign);
    }

    public static WasapiBusRenderOwnerStep Attach(IWasapiBoundStreamClient client, WasapiRenderBlockAdapter adapter,
        TransportEndpoint endpoint, PcmFloatFormat format, WasapiPcm native)
    {
        RequireMta();
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(native);
        endpoint.Validate();
        native.Validate();
        if (client.Endpoint != endpoint || endpoint.Direction != EndpointDirection.Playback ||
            adapter.Format != format || native.FloatFormat != format || adapter.NativeFormat != native || client.PreparedFormat != native)
            throw new ArgumentException("Cliente, adaptador, endpoint e formato de render precisam coincidir.");

        var binding = WasapiBusNativeBinding<IWasapiRenderPackets>.Open(client, static ready => ready.OpenRender());
        try { return new(binding.Packets, adapter, endpoint, format, native, binding); }
        catch { binding.Dispose(); throw; }
    }

    internal bool HasNativeClock => nativeBinding is not null;

    internal NativeClockSample ReadNativeClock()
    {
        CheckOwner();
        if (!IsAvailableOnOwner || nativeBinding is null) throw new InvalidOperationException("Clock de render indisponível.");
        return nativeBinding.ReadClock();
    }

    public unsafe bool TryRender(MixBus bus, long block, CancellationToken cancellation = default)
    {
        CheckOwner();
        if (disposed || retained || cancellation.IsCancellationRequested || block < 0) return false;
        nint ptr = packets.GetBuffer(frames);
        bool valid = false;
        int releaseHr = -1;
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
            try { releaseHr = packets.ReleaseBuffer(frames, valid && !cancellation.IsCancellationRequested ? 0u : 2u); }
            finally
            {
                if (releaseHr != 0)
                {
                    retained = true;
                    adapter.FailClosed();
                }
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
        DisposeServices();
    }

    private void DisposeServices()
    {
        if (retained) return;
        try
        {
            if (nativeBinding is null) packets.Dispose();
            else nativeBinding.Dispose();
        }
        catch
        {
            retained = true;
            throw;
        }
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
