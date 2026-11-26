namespace WaveStudio.Audio.Engine;

// Com endpoint nulo, distingue seguir o padrão de uma fonte desativada.
internal readonly record struct ExternalSelection(string ClientId, string? EndpointId, bool Accessible, bool EndpointPresent,
    bool Disabled = false)
{
    public ExternalSelection SelectEndpoint(string endpointId) => this with { EndpointId = endpointId, Disabled = false };
}
internal readonly record struct ExternalSelections(ExternalSelection Browser, ExternalSelection Consumer);
internal enum ExternalRestoreState { AlreadyRestored, Restore, Conflict, Unavailable }
internal readonly record struct ExternalRestoreReport(ExternalRestoreState Browser, ExternalRestoreState Consumer)
{
    public bool Complete => Browser == ExternalRestoreState.AlreadyRestored && Consumer == ExternalRestoreState.AlreadyRestored;
}

// Nenhuma API de escrita. Quem opera fornece as leituras antes, depois de aplicar e depois de restaurar.
internal sealed class ExternalRouteConfiguration
{
    private readonly ExternalRouteTopology topology;
    public ExternalSelections Before { get; }
    public ExternalSelections Applied { get; }

    public ExternalRouteConfiguration(ExternalRouteTopology topology, ExternalSelections before)
    {
        topology.Validate();
        if (!CanRead(before.Browser) || before.Browser.Disabled || !CanRead(before.Consumer) ||
            StringComparer.OrdinalIgnoreCase.Equals(before.Browser.ClientId, before.Consumer.ClientId))
            throw new ArgumentException("Observe as duas configurações iniciais e suas identidades antes de aplicar.");
        this.topology = topology;
        Before = before;
        Applied = new(before.Browser.SelectEndpoint(topology.BrowserCable.Render.Endpoint.Id),
            before.Consumer.SelectEndpoint(topology.MicCable.Capture.Endpoint.Id));
    }

    // Antes de cada troca manual, compara novamente o observado com o valor temporário ou original.
    public ExternalRestoreReport PlanRestore(ExternalSelections current, bool originalBrowserPresent = true, bool originalConsumerPresent = true) =>
        new(Classify(Before.Browser, Applied.Browser, current.Browser, originalBrowserPresent),
            Classify(Before.Consumer, Applied.Consumer, current.Consumer, originalConsumerPresent));

    public ExternalRouteConsent Confirm(ExternalSelections current, bool isolated, bool lowLevelHeadset, CancellationToken stop)
    {
        if (!isolated || !lowLevelHeadset || !stop.CanBeCanceled ||
            !Matches(Applied.Browser, current.Browser) || !Matches(Applied.Consumer, current.Consumer))
            throw new InvalidOperationException("A rota exige configuração conferida, isolamento, headset em nível baixo e parada.");
        stop.ThrowIfCancellationRequested();
        return new(topology, stop);
    }

    internal static ExternalRestoreState Classify(ExternalSelection before, ExternalSelection applied, ExternalSelection current, bool originalPresent)
    {
        if (!CanRead(current) || !originalPresent) return ExternalRestoreState.Unavailable;
        if (Matches(before, current)) return ExternalRestoreState.AlreadyRestored;
        return Matches(applied, current) ? ExternalRestoreState.Restore : ExternalRestoreState.Conflict;
    }

    internal static bool CanRead(ExternalSelection value) =>
        value.Accessible && value.EndpointPresent && !string.IsNullOrWhiteSpace(value.ClientId) &&
        (value.EndpointId is null || (!value.Disabled && !string.IsNullOrWhiteSpace(value.EndpointId)));

    internal static bool Matches(ExternalSelection expected, ExternalSelection current) =>
        CanRead(current) && expected.Disabled == current.Disabled && StringComparer.Ordinal.Equals(expected.ClientId, current.ClientId) &&
        StringComparer.OrdinalIgnoreCase.Equals(expected.EndpointId, current.EndpointId);
}

internal sealed class ExternalRouteConsent(ExternalRouteTopology topology, CancellationToken stop)
{
    private int used;
    public CancellationToken Stop => stop;

    public void Consume(ExternalRouteTopology selected, ExternalFlow flow)
    {
        Stop.ThrowIfCancellationRequested();
        int bit = flow switch
        {
            ExternalFlow.Browser => 1,
            ExternalFlow.Microphone => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(flow))
        };
        if (selected != topology || (Interlocked.Or(ref used, bit) & bit) != 0)
            throw new InvalidOperationException("Consentimento incompatível ou rota já tentada.");
    }
}
