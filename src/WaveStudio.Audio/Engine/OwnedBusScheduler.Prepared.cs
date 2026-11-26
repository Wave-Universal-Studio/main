namespace WaveStudio.Audio.Engine;

internal readonly record struct PreparedBusCaptureSlot(MixSource Source, IWasapiBoundStreamClient Client, WasapiCaptureBlockAdapter Adapter);
internal readonly record struct PreparedBusRenderSlot(TransportEndpoint Endpoint, MixBus Bus,
    IWasapiBoundStreamClient Client, WasapiRenderBlockAdapter Adapter);

internal sealed partial class OwnedBusScheduler
{
    // Reserva a geração antes dos GetService. O chamador já preparou os clientes na mesma MTA.
    public static OwnedBusScheduler CreatePrepared(long generation, PcmFloatFormat format, BoundedBusPcmTransport bridge,
        ReadOnlySpan<PreparedBusCaptureSlot> inputs, ReadOnlySpan<PreparedBusRenderSlot> outputs,
        TransportActivityGuard guard, CancellationToken cancellation = default, Func<long>? readReferenceTime = null) =>
        CreatePreparedCore(generation, format, bridge, inputs, outputs, guard, cancellation, readReferenceTime, null);

    internal static OwnedBusScheduler CreateForLifecycle(long generation, PcmFloatFormat format, BoundedBusPcmTransport bridge,
        ReadOnlySpan<PreparedBusCaptureSlot> inputs, ReadOnlySpan<PreparedBusRenderSlot> outputs,
        TransportActivityGuard guard, TransportActivityGuard.OwnedBusLease lifecycleLease,
        CancellationToken cancellation, Func<long>? readReferenceTime)
    {
        if (!lifecycleLease.Authorizes(guard, generation))
            throw new InvalidOperationException("O ciclo de vida precisa reservar a geração antes de preparar os clientes.");
        return CreatePreparedCore(generation, format, bridge, inputs, outputs, guard, cancellation, readReferenceTime, lifecycleLease);
    }

    private static OwnedBusScheduler CreatePreparedCore(long generation, PcmFloatFormat format, BoundedBusPcmTransport bridge,
        ReadOnlySpan<PreparedBusCaptureSlot> inputs, ReadOnlySpan<PreparedBusRenderSlot> outputs,
        TransportActivityGuard guard, CancellationToken cancellation, Func<long>? readReferenceTime,
        TransportActivityGuard.OwnedBusLease? lifecycleLease)
    {
        RequireMta();
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(guard);
        if (generation <= 0 || inputs.Length is < 1 or > BusMixSession.MaximumSources || outputs.Length is < 1 or > 2)
            throw new ArgumentException("A sessão exige fontes e destinos preparados, em uma geração explícita.");
        cancellation.ThrowIfCancellationRequested();

        var sources = new MixSource[inputs.Length];
        for (int i = 0; i < sources.Length; i++)
        {
            sources[i] = inputs[i].Source;
            CheckReferenceTime(inputs[i].Client, readReferenceTime);
        }
        foreach (var output in outputs) CheckReferenceTime(output.Client, readReferenceTime);
        if (!bridge.MatchesOwnedBus(generation, format, sources))
            throw new ArgumentException("A ponte precisa coincidir com a geração e os formatos preparados.", nameof(bridge));

        var captures = new OwnedBusCaptureSlot[inputs.Length];
        var renders = new OwnedBusRenderSlot[outputs.Length];
        var lease = lifecycleLease ?? guard.TryAcquireOwnedBus(generation)
            ?? throw new InvalidOperationException("Atividade ocupada ou geração do barramento já utilizada.");
        try
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var input = inputs[i];
                captures[i] = new(input.Source, WasapiBusCaptureOwnerStep.Attach(input.Client, input.Adapter,
                    input.Source.Endpoint, input.Source.Format));
            }
            for (int i = 0; i < outputs.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var output = outputs[i];
                renders[i] = new(output.Endpoint, output.Bus, WasapiBusRenderOwnerStep.Attach(output.Client, output.Adapter,
                    output.Endpoint, format, output.Client.PreparedFormat));
            }
            return new(generation, format, sources, bridge, captures, renders, guard, cancellation, lease,
                readReferenceTime ?? WasapiTime.Now100ns, lifecycleLease is not null);
        }
        catch (Exception error)
        {
            bool retained = ReleaseOwners(captures, renders, out var cleanup);
            if (lifecycleLease is null)
            {
                if (retained || error is WasapiBusBindingCleanupException) WasapiResourceScope.Retain(lease);
                else lease.Dispose();
            }
            if (cleanup is not null) throw new WasapiBusBindingCleanupException(new AggregateException(error, cleanup));
            throw;
        }
    }

    private static void CheckReferenceTime(IWasapiBoundStreamClient client, Func<long>? readReferenceTime)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (readReferenceTime is not null && client is IWasapiNativeBoundStreamClient)
            throw new ArgumentException("Os clientes do hardware exigem o QPC real.", nameof(readReferenceTime));
    }

    private static bool ReleaseOwners(ReadOnlySpan<OwnedBusCaptureSlot> inputs, ReadOnlySpan<OwnedBusRenderSlot> outputs,
        out Exception? failure)
    {
        failure = null;
        bool retained = false;
        foreach (var input in inputs) ReleaseOwner(input.Owner, ref retained, ref failure);
        foreach (var output in outputs) ReleaseOwner(output.Owner, ref retained, ref failure);
        return retained;
    }

    private static void ReleaseOwner(IWasapiBusOwner? owner, ref bool retained, ref Exception? failure)
    {
        if (owner is null) return;
        try { owner.Dispose(); }
        catch (Exception error) { failure ??= error; retained = true; }
        if (!owner.ResourcesRetained) return;
        retained = true;
        WasapiResourceScope.Retain(owner);
    }
}
