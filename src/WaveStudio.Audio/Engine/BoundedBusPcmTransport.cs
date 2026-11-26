namespace WaveStudio.Audio.Engine;

// Ponte limitada e sem dispositivos: callbacks de captura entregam blocos PCM identificados;
// o mixer os consome e oferece dois barramentos a consumidores independentes.
// Um único produtor e um único consumidor por fila. O backend nativo futuro precisa provar
// alinhamento dos blocos ao mesmo relógio antes de chamar TryOfferCapture.
internal sealed class BoundedBusPcmTransport : IBusPcmTransport, IDisposable
{
    public const int DefaultCapacityBlocks = 8;
    private readonly long generation;
    private readonly PcmFloatFormat outputFormat;
    private readonly SourceLane[] sources;
    private readonly FrameLane personal;
    private readonly FrameLane stream;
    private readonly FrameLane? external;
    private readonly MixExternalDestination? destination;
    private MixDeliveryTicket? publishedTicket;
    private int closed;

    public BoundedBusPcmTransport(long generation, PcmFloatFormat outputFormat,
        ReadOnlySpan<MixSource> sources, MixExternalDestination? externalDestination = null,
        int capacityBlocks = DefaultCapacityBlocks)
    {
        ArgumentNullException.ThrowIfNull(outputFormat);
        if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
        RequireSupported(outputFormat);
        if (capacityBlocks is < 2 or > 64 || (capacityBlocks & (capacityBlocks - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(capacityBlocks), "A capacidade deve ser potência de dois entre 2 e 64.");
        if (sources.Length > BusMixSession.MaximumSources)
            throw new ArgumentOutOfRangeException(nameof(sources));
        if (externalDestination is not null && (string.IsNullOrWhiteSpace(externalDestination.Id) ||
            externalDestination.CallId is { Length: 0 }))
            throw new ArgumentException("O destino externo precisa de identidade válida.", nameof(externalDestination));

        this.generation = generation;
        this.outputFormat = outputFormat;
        destination = externalDestination;
        this.sources = new SourceLane[sources.Length];

        for (int i = 0; i < sources.Length; i++)
        {
            MixSource source = sources[i];
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(source.Format);
            source.Endpoint.Validate();
            RequireSupported(source.Format);
            if (source.Endpoint.Generation != generation || source.Endpoint.Direction != EndpointDirection.Capture)
                throw new ArgumentException("Uma fonte deve pertencer à geração de captura ativa.", nameof(sources));
            for (int prior = 0; prior < i; prior++)
                if (StringComparer.Ordinal.Equals(this.sources[prior].Definition.Endpoint.Id, source.Endpoint.Id))
                    throw new ArgumentException("Fonte de captura repetida.", nameof(sources));
            this.sources[i] = new(source, new FrameLane(SamplesPerBlock(source.Format), capacityBlocks));
        }

        int outputSamples = SamplesPerBlock(outputFormat);
        personal = new(outputSamples, capacityBlocks);
        stream = new(outputSamples, capacityBlocks);
        external = externalDestination is null ? null : new(outputSamples, capacityBlocks);
    }

    // A validação de vínculo ocorre uma vez, antes de adquirir os owners.
    internal bool MatchesOwnedBus(long epoch, PcmFloatFormat format, ReadOnlySpan<MixSource> configured)
    {
        if (Volatile.Read(ref closed) != 0 || generation != epoch || outputFormat != format ||
            destination is not null || configured.Length != sources.Length) return false;
        foreach (var entry in sources)
        {
            if (!configured.Contains(entry.Definition)) return false;
        }
        return true;
    }

    // Cada fonte deve ter exatamente um produtor. Recusamos blocos repetidos, antigos, fora
    // de formato e sobrecarga; nunca substituímos dados ainda não consumidos.
    public bool TryOfferCapture(TransportEndpoint endpoint, long block, ReadOnlySpan<float> pcm)
    {
        if (Volatile.Read(ref closed) != 0 || endpoint.Generation != generation ||
            endpoint.Direction != EndpointDirection.Capture) return false;
        foreach (var source in sources)
        {
            if (source.Definition.Endpoint != endpoint) continue;
            bool accepted = source.Frames.TryWrite(block, pcm, null);
            return accepted && Volatile.Read(ref closed) == 0;
        }
        return false;
    }

    public MixReadResult Read(MixSource source, long block, Span<float> pcm)
    {
        pcm.Clear();
        if (Volatile.Read(ref closed) != 0 || source.Endpoint.Generation != generation) return default;
        foreach (var entry in sources)
        {
            if (entry.Definition.Endpoint != source.Endpoint || entry.Definition.Format != source.Format) continue;
            if (!entry.Frames.TryRead(block, pcm, requireTicket: false, out _)) return default;
            if (Volatile.Read(ref closed) != 0) { pcm.Clear(); return default; }
            return new(source.Endpoint, block, source.Format.SampleRate / 100);
        }
        return default;
    }

    // Nenhuma entrega interna é aceita se faltar capacidade em algum barramento.
    // Os dois consumidores mantêm filas independentes e podem usar ritmos diferentes.
    public bool Submit(MixDeliveryTicket ticket, long block, ReadOnlySpan<float> personalPcm,
        ReadOnlySpan<float> streamPcm, BusMeters meters)
    {
        if (!Valid(ticket) || block < 0 || personalPcm.Length != personal.BlockSamples ||
            streamPcm.Length != stream.BlockSamples || !personal.HasRoom || !stream.HasRoom)
        {
            ticket.Invalidate();
            return false;
        }
        // Fechar a ponte deve invalidar também tickets obtidos antes do fechamento.
        Volatile.Write(ref publishedTicket, ticket);
        if (!Valid(ticket))
        {
            // O fechamento pode ocorrer entre publicar o ticket e confirmar a sessão.
            ticket.Invalidate();
            return false;
        }
        bool ok = personal.TryWrite(block, personalPcm, ticket) && stream.TryWrite(block, streamPcm, ticket);
        if (!ok || !Valid(ticket)) { ticket.Invalidate(); return false; }
        return true;
    }

    // O destino externo continua sem consumidor até a autorização explícita de uma rota.
    // Este método apenas guarda PCM; não abre WASAPI nem envia nada ao OBS/Discord.
    public bool SubmitExternal(MixDeliveryTicket ticket, long block, ReadOnlySpan<float> streamPcm)
    {
        if (!Valid(ticket) || external is null || destination != ticket.External ||
            block < 0 || streamPcm.Length != external.BlockSamples ||
            !external.TryWrite(block, streamPcm, ticket))
        {
            ticket.Invalidate();
            return false;
        }
        return Valid(ticket);
    }

    // Os consumidores precisam conferir o ticket novamente no ponto real de publicação.
    // Se o fechamento ou a troca de geração acontecer durante a cópia, a saída é apagada.
    public bool TryReadOutput(MixBus bus, long block, Span<float> pcm, out MixDeliveryTicket? ticket)
    {
        ticket = null;
        if (Volatile.Read(ref closed) != 0) { pcm.Clear(); return false; }
        FrameLane? lane = bus switch
        {
            MixBus.Personal => personal,
            MixBus.Stream => stream,
            _ => null
        };
        if (lane is null || !lane.TryRead(block, pcm, requireTicket: true, out ticket) ||
            Volatile.Read(ref closed) != 0 || ticket is not { IsValid: true })
        {
            pcm.Clear();
            ticket = null;
            return false;
        }
        return true;
    }

    public bool TryReadExternal(MixExternalDestination expected, long block, Span<float> pcm,
        out MixDeliveryTicket? ticket)
    {
        ticket = null;
        if (Volatile.Read(ref closed) != 0 || destination != expected || external is null ||
            !external.TryRead(block, pcm, requireTicket: true, out ticket) ||
            Volatile.Read(ref closed) != 0 || ticket is not { IsValid: true })
        {
            pcm.Clear();
            ticket = null;
            return false;
        }
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref closed, 1) == 0)
            Volatile.Read(ref publishedTicket)?.Invalidate();
    }

    private bool Valid(MixDeliveryTicket ticket) =>
        Volatile.Read(ref closed) == 0 && ticket.Generation == generation &&
        ticket.Format == outputFormat && ticket.IsValid &&
        (ticket.External is null || ticket.External == destination);

    private static void RequireSupported(PcmFloatFormat format)
    {
        if (format.SampleRate is not (44100 or 48000))
            throw new NotSupportedException("A ponte PCM aceita somente 44,1/48 kHz.");
    }

    private static int SamplesPerBlock(PcmFloatFormat format) => format.SampleRate / 100 * format.Channels;

    private sealed record SourceLane(MixSource Definition, FrameLane Frames);

    // Ring SPSC de blocos completos. As cópias ficam protegidas por publicações
    // release/acquire; índices antigos ou tickets invalidados nunca são reproduzidos.
    private sealed class FrameLane
    {
        private readonly float[] samples;
        private readonly long[] blocks;
        private readonly MixDeliveryTicket?[] tickets;
        private readonly uint mask;
        private readonly uint capacity;
        private uint written;
        private uint consumed;
        private long lastWritten = -1;
        private int writing, reading;

        public int BlockSamples { get; }
        public bool HasRoom => unchecked(Volatile.Read(ref written) - Volatile.Read(ref consumed)) < capacity;

        public FrameLane(int blockSamples, int capacityBlocks)
        {
            BlockSamples = blockSamples;
            capacity = (uint)capacityBlocks;
            mask = capacity - 1;
            samples = new float[checked(blockSamples * capacityBlocks)];
            blocks = new long[capacityBlocks];
            tickets = new MixDeliveryTicket?[capacityBlocks];
        }

        public bool TryWrite(long block, ReadOnlySpan<float> pcm, MixDeliveryTicket? ticket)
        {
            if (block < 0 || pcm.Length != BlockSamples ||
                Interlocked.CompareExchange(ref writing, 1, 0) != 0) return false;
            try
            {
                uint next = Volatile.Read(ref written);
                if (block <= lastWritten || !HasRoom) return false;
                int slot = (int)(next & mask);
                pcm.CopyTo(samples.AsSpan(slot * BlockSamples, BlockSamples));
                blocks[slot] = block;
                tickets[slot] = ticket;
                lastWritten = block;
                Volatile.Write(ref written, unchecked(next + 1));
                return true;
            }
            finally { Volatile.Write(ref writing, 0); }
        }

        public bool TryRead(long expected, Span<float> output, bool requireTicket,
            out MixDeliveryTicket? ticket)
        {
            ticket = null;
            if (expected < 0 || output.Length != BlockSamples ||
                Interlocked.CompareExchange(ref reading, 1, 0) != 0)
            {
                output.Clear();
                return false;
            }
            try
            {
                for (int attempt = 0; attempt < capacity; attempt++)
                {
                    uint position = Volatile.Read(ref consumed);
                    if (position == Volatile.Read(ref written)) break;
                    int slot = (int)(position & mask);
                    long actualBlock = blocks[slot];
                    MixDeliveryTicket? pending = tickets[slot];
                    if (actualBlock > expected && (!requireTicket || pending is { IsValid: true })) break;
                    if (actualBlock == expected && (!requireTicket || pending is { IsValid: true }))
                    {
                        samples.AsSpan(slot * BlockSamples, BlockSamples).CopyTo(output);
                        tickets[slot] = null;
                        Volatile.Write(ref consumed, unchecked(position + 1));
                        if (!requireTicket || pending is { IsValid: true })
                        {
                            ticket = pending;
                            return true;
                        }
                        output.Clear();
                        return false;
                    }
                    tickets[slot] = null;
                    Volatile.Write(ref consumed, unchecked(position + 1));
                }
                output.Clear();
                return false;
            }
            finally { Volatile.Write(ref reading, 0); }
        }
    }
}
