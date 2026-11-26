namespace WaveStudio.Audio.Engine;

internal enum TransportState { Closed, Preparing, Priming, Running, Stopping, Faulted }
internal enum TransportFault { None, Canceled, Backend, Clock, Discontinuity, Protocol, Boundary, QuiescenceTimeout, SnapshotUnavailable }
internal enum AudioActivity { ShortDiagnostic = 1, IsolatedTransport = 2, NativePreparation = 3 }

// O diagnóstico físico e a preparação compartilham este controle de exclusão no processo. Outros processos exigem isolamento próprio.
internal sealed class TransportActivityGuard
{
    public static TransportActivityGuard Shared { get; } = new();
    private int owner;
    private long lastOwnedBusGeneration;
    public IDisposable? TryAcquire(AudioActivity activity)
    {
        if (activity is not (AudioActivity.ShortDiagnostic or AudioActivity.IsolatedTransport or AudioActivity.NativePreparation)) throw new ArgumentOutOfRangeException(nameof(activity));
        return Interlocked.CompareExchange(ref owner, (int)activity, 0) == 0 ? new Lease(this) : null;
    }
    // O lease exclusivo também serializa a validação da geração, fora dos passos de áudio.
    public IDisposable? TryAcquireOwnedBus(long generation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        var lease = TryAcquire(AudioActivity.IsolatedTransport);
        if (lease is null) return null;
        if (generation <= lastOwnedBusGeneration)
        {
            lease.Dispose();
            return null;
        }
        lastOwnedBusGeneration = generation;
        return lease;
    }

    private sealed class Lease(TransportActivityGuard guard) : IDisposable
    {
        private TransportActivityGuard? current = guard;
        public void Dispose() { var previous = Interlocked.Exchange(ref current, null); if (previous is not null) Volatile.Write(ref previous.owner, 0); }
    }
}

// O ticket é invalidado antes de Stop aguardar o worker e os callbacks. Submit deve conferi-lo no ponto de entrega.
internal sealed class DeliveryTicket(TransportRoute route)
{
    private int valid;
    public TransportEndpoint Capture { get; } = route.Capture.Endpoint;
    public TransportEndpoint Playback { get; } = route.Playback.Endpoint;
    public bool IsValid => Volatile.Read(ref valid) == 1;
    internal void Enable() => Interlocked.CompareExchange(ref valid, 1, 0);
    internal void Invalidate() => Interlocked.Exchange(ref valid, -1);
}

internal delegate void CapturePacket(ReadOnlySpan<byte> bytes, int frames, NativeClockSample clock, long arrival100ns);
internal delegate void RenderPacket(Span<byte> bytes, int frames, NativeClockSample clock, long arrival100ns);

// Exclusivo da simulação. A prova nativa silenciosa gerencia seu próprio ciclo de vida de pacotes.
// Os métodos de controle pertencem a um worker MTA. Cada callback tem uma thread proprietária distinta e estável.
// Os spans emprestados expiram no retorno. Submit é limitado, não bloqueia e deve revalidar o ticket.
internal interface ISimulatedAudioTransport : IDisposable
{
    void PrepareCapture(TransportRoute route, CapturePacket capture);
    void PreparePlayback(TransportRoute route, RenderPacket render);
    void StartPlayback();
    void StartCapture();
    void RequestStop();
    bool Submit(DeliveryTicket ticket, ReadOnlySpan<byte> bytes);
    // Nenhum callback com chegada anterior ou igual a este marco temporal permanece em andamento ou pode chegar depois.
    long ClosedThrough100ns { get; }
}

internal sealed record TransportSnapshot(TransportState State, long Generation, TransportFault Fault,
    long PrimedFrames, long ProducedFrames, long ReadFrames, long DroppedFrames, long UnderrunFrames,
    long SubmittedFrames, long MutedFrames, long DiscardedQueuedFrames, double? MeanOccupancy,
    double TargetPpm, double AppliedPpm, DriftControlState ControlState, int OwnerThreadId, bool ResourcesRetained,
    bool BufferSnapshotAvailable = true);

// Um escritor por registro, com leituras seqlock limitadas no worker de controle. Integra somente intervalos fechados.
internal sealed class TransportLedger
{
    private int sequence;
    private long count, time;
    private UInt128 area;
    private ClockObservation observation;
    private bool observed;
    public TransportLedger(long origin100ns) => time = origin100ns;
    public void Add(int frames, long at, ClockObservation clock)
    {
        Interlocked.Increment(ref sequence);
        area += (UInt128)(ulong)count * (ulong)(at - time);
        count += frames; time = at; observation = clock; observed = true;
        Interlocked.Increment(ref sequence);
    }
    public bool Read(long now, out LedgerSnapshot snapshot)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int before = Volatile.Read(ref sequence);
            if ((before & 1) != 0) continue;
            long c = count, t = time; UInt128 a = area; var o = observation; bool has = observed;
            Thread.MemoryBarrier();
            if (before != Volatile.Read(ref sequence) || now < t) continue;
            snapshot = new(c, a + (UInt128)(ulong)c * (ulong)(now - t), o, has); return true;
        }
        snapshot = default; return false;
    }
}
internal readonly record struct LedgerSnapshot(long Frames, UInt128 Area, ClockObservation Clock, bool Observed);
