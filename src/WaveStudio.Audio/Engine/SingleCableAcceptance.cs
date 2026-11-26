namespace WaveStudio.Audio.Engine;

internal sealed record SingleCableTopology(ExternalCable Cable, ExternalEndpoint Headset, ExternalEndpoint Microphone)
{
    public long Generation => Cable.Render.Endpoint.Generation;

    public TransportRoute Route(ExternalFlow flow) => flow switch
    {
        ExternalFlow.Browser => new(Cable.Capture.Info, Headset.Info),
        ExternalFlow.Microphone => new(Microphone.Info, Cable.Render.Info),
        _ => throw new ArgumentOutOfRangeException(nameof(flow))
    };

    public void Validate()
    {
        ExternalRouteTopology.ValidatePorts([Cable.Render, Cable.Capture, Headset, Microphone]);
        if (!ExternalRouteTopology.SameAdapter(Cable.Render, Cable.Capture) ||
            ExternalRouteTopology.SameAdapter(Cable.Render, Headset) || ExternalRouteTopology.SameAdapter(Cable.Render, Microphone))
            throw new ArgumentException("O par do cabo precisa compartilhar um adaptador separado dos endpoints físicos.");
        Route(ExternalFlow.Browser).Validate();
        Route(ExternalFlow.Microphone).Validate();
    }

    public SingleCableTopology AtGeneration(long generation)
    {
        ExternalEndpoint Set(ExternalEndpoint port) => port with
        {
            Info = port.Info with { Endpoint = port.Endpoint with { Generation = generation } }
        };
        return new(new(Set(Cable.Render), Set(Cable.Capture)), Set(Headset), Set(Microphone));
    }
}

internal enum SingleCableState { Preparing, Browser, BrowserRestore, CheckingQuiet, MicrophoneReady, Microphone, FinalRestore, Complete, Failed }

// O guard externo pertence ao lote inteiro. Cada stream ou sondagem ainda adquire um lease interno próprio.
internal sealed class SingleCableAcceptance : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly TransportActivityGuard phaseGuard = new();
    private readonly IDisposable batchLease;
    private readonly CancellationTokenSource stop;
    private WasapiExternalRouteSession? active;
    private Task<CableQuietReport>? quietTask;
    private Task? closingFlow;
    private bool disposed, microphoneConfigurationAllowed;
    public bool ResourcesRetained { get; private set; }
    public SingleCableState State { get; private set; }
    public SingleCableTopology BrowserTopology { get; }
    public SingleCableTopology QuietTopology { get; }
    public SingleCableTopology MicrophoneTopology { get; }
    public ExternalSelections Before { get; }
    public ExternalSelections BrowserSelections { get; }
    public ExternalSelections MicrophoneSelections { get; }

    public SingleCableAcceptance(SingleCableTopology topology, ExternalSelections before, CancellationToken operatorStop,
        TransportActivityGuard? guard = null)
    {
        topology.Validate();
        if (!operatorStop.CanBeCanceled || topology.Generation > long.MaxValue - 3 ||
            !ExternalRouteConfiguration.CanRead(before.Browser) || before.Browser.Disabled ||
            !ExternalRouteConfiguration.CanRead(before.Consumer) ||
            StringComparer.OrdinalIgnoreCase.Equals(before.Browser.ClientId, before.Consumer.ClientId) ||
            IsCable(before.Browser.EndpointId) || IsCable(before.Consumer.EndpointId))
            throw new ArgumentException("Snapshot original, geração e parada são obrigatórios. O cabo precisa estar isolado no estado original.");
        operatorStop.ThrowIfCancellationRequested();
        BrowserTopology = topology;
        QuietTopology = topology.AtGeneration(topology.Generation + 1);
        MicrophoneTopology = topology.AtGeneration(topology.Generation + 2);
        Before = before;
        BrowserSelections = Before with { Browser = before.Browser.SelectEndpoint(topology.Cable.Render.Endpoint.Id) };
        MicrophoneSelections = Before with { Consumer = before.Consumer.SelectEndpoint(topology.Cable.Capture.Endpoint.Id) };
        batchLease = (guard ?? TransportActivityGuard.Shared).TryAcquire(AudioActivity.IsolatedTransport)
            ?? throw new InvalidOperationException("Outra atividade de áudio está ativa.");
        stop = CancellationTokenSource.CreateLinkedTokenSource(operatorStop);

        bool IsCable(string? id) => StringComparer.OrdinalIgnoreCase.Equals(id, topology.Cable.Render.Endpoint.Id) ||
            StringComparer.OrdinalIgnoreCase.Equals(id, topology.Cable.Capture.Endpoint.Id);
    }

    public WasapiExternalRouteSession Begin(ExternalFlow flow, SingleCableTopology verified, ExternalSelections observed,
        bool isolated, Func<bool> noProducer, Func<TransportEndpoint, IWasapiPreparationDevice>? open = null,
        WasapiDriftSimulation? pacing = null)
    {
        lock (gate)
        {
            if (flow is not (ExternalFlow.Browser or ExternalFlow.Microphone)) throw new ArgumentOutOfRangeException(nameof(flow));
            bool browser = flow == ExternalFlow.Browser;
            Require(browser ? SingleCableState.Preparing : SingleCableState.MicrophoneReady,
                browser ? BrowserTopology : MicrophoneTopology, verified,
                browser ? BrowserSelections : MicrophoneSelections, observed, isolated);
            if (!browser)
            {
                // Um produtor reaparecido invalida a evidência de quietude antes de abrir o render.
                State = SingleCableState.Failed;
                if (!noProducer()) throw new InvalidOperationException("Produtor ativo após a sondagem de quietude.");
            }
            var session = new WasapiExternalRouteSession(verified.Route(flow), flow, stop.Token, phaseGuard, open, pacing);
            active = session;
            closingFlow = null;
            State = browser ? SingleCableState.Browser : SingleCableState.Microphone;
            return session;
        }
    }

    public Task FinishFlowAsync()
    {
        lock (gate)
        {
            if (active is null) throw new InvalidOperationException("Nenhuma rota em andamento.");
            return closingFlow ??= FinishFlowCoreAsync(active);
        }
    }

    private async Task FinishFlowCoreAsync(WasapiExternalRouteSession session)
    {
        bool closed = false;
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
            closed = true;
        }
        finally
        {
            lock (gate)
            {
                ResourcesRetained |= !closed;
                active = null;
                if (closed && !stop.IsCancellationRequested && session.Result is { Passed: true,
                    Levels: { BypassNonZeroFrames: > 0, EffectNonZeroFrames: > 0 } } result)
                    State = result.Flow == ExternalFlow.Browser ? SingleCableState.BrowserRestore : SingleCableState.FinalRestore;
                else State = SingleCableState.Failed;
            }
        }
    }

    public Task<CableQuietReport> CheckQuietAsync(SingleCableTopology verified, ExternalSelections observed,
        bool isolated, Func<bool> noProducer, Func<TransportEndpoint, IWasapiPreparationDevice>? open = null,
        Func<long>? clock = null, Action<CancellationToken>? wait = null)
    {
        lock (gate)
        {
            Require(SingleCableState.BrowserRestore, QuietTopology, verified, Before, observed, isolated);
            State = SingleCableState.CheckingQuiet;
            return quietTask = CheckQuietCoreAsync(verified.Cable.Capture.Info, noProducer, open, clock, wait);
        }
    }

    private async Task<CableQuietReport> CheckQuietCoreAsync(WasapiEndpointInfo endpoint, Func<bool> noProducer,
        Func<TransportEndpoint, IWasapiPreparationDevice>? open, Func<long>? clock, Action<CancellationToken>? wait)
    {
        var result = await WasapiCableQuietProbe.ObserveAsync(endpoint, phaseGuard, noProducer, stop.Token, open, clock, wait)
            .ConfigureAwait(false);
        lock (gate)
        {
            ResourcesRetained |= !result.ResourcesReleased;
            microphoneConfigurationAllowed = result.Passed && !stop.IsCancellationRequested;
            State = microphoneConfigurationAllowed ? SingleCableState.MicrophoneReady : SingleCableState.Failed;
        }
        return result;
    }

    public ExternalRestoreReport Restoration(ExternalSelections current, bool originalBrowserPresent = true, bool originalConsumerPresent = true)
    {
        lock (gate)
        {
            var observedTemporary = microphoneConfigurationAllowed ? MicrophoneSelections.Consumer : Before.Consumer;
            return new(ExternalRouteConfiguration.Classify(Before.Browser, BrowserSelections.Browser, current.Browser, originalBrowserPresent),
                ExternalRouteConfiguration.Classify(Before.Consumer, observedTemporary, current.Consumer, originalConsumerPresent));
        }
    }

    public ExternalRestoreReport FinishRestoration(ExternalSelections current, bool originalBrowserPresent = true,
        bool originalConsumerPresent = true)
    {
        lock (gate)
        {
            var result = Restoration(current, originalBrowserPresent, originalConsumerPresent);
            if (State == SingleCableState.FinalRestore && result.Complete && !stop.IsCancellationRequested)
                State = SingleCableState.Complete;
            return result;
        }
    }

    private void Require(SingleCableState expected, SingleCableTopology topology, SingleCableTopology verified,
        ExternalSelections settings, ExternalSelections observed, bool isolated)
    {
        stop.Token.ThrowIfCancellationRequested();
        if (disposed || State != expected || ResourcesRetained || !isolated || topology != verified ||
            !ExternalRouteConfiguration.Matches(settings.Browser, observed.Browser) ||
            !ExternalRouteConfiguration.Matches(settings.Consumer, observed.Consumer))
            throw new InvalidOperationException("Fase, identidade ou isolamento do cabo não confirmado.");
    }

    public async ValueTask DisposeAsync()
    {
        Task? pendingFlow;
        Task<CableQuietReport>? pendingQuiet;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            stop.Cancel();
            pendingFlow = active is not null ? FinishFlowAsync() : closingFlow;
            pendingQuiet = quietTask;
        }
        try
        {
            if (pendingFlow is not null) await pendingFlow.ConfigureAwait(false);
            if (pendingQuiet is not null) await pendingQuiet.ConfigureAwait(false);
        }
        finally
        {
            if (!ResourcesRetained) batchLease.Dispose();
            stop.Dispose();
        }
    }
}
