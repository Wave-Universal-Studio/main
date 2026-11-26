namespace WaveStudio.Audio.Engine;

// Os serviços são filhos do mesmo cliente já preparado. O chamador conserva o ciclo de vida do cliente.
internal sealed class WasapiBusNativeBinding<TPackets> : IDisposable where TPackets : class, IDisposable
{
    private readonly IWasapiBoundStreamClient client;
    private readonly WasapiResourceScope scope;
    private readonly IWasapiPreparationClock clock;

    public TPackets Packets { get; }

    private WasapiBusNativeBinding(IWasapiBoundStreamClient client, WasapiResourceScope scope,
        IWasapiPreparationClock clock, TPackets packets)
    {
        this.client = client;
        this.scope = scope;
        this.clock = clock;
        Packets = packets;
    }

    public static WasapiBusNativeBinding<TPackets> Open(IWasapiBoundStreamClient client, Func<IWasapiBoundStreamClient, TPackets> openPackets)
    {
        var scope = new WasapiResourceScope();
        try
        {
            scope.Add(client.BorrowBusServices());
            var clock = scope.Add(client.OpenClock() ?? throw new InvalidOperationException("Serviço de clock WASAPI ausente."));
            var packets = scope.Add(openPackets(client) ?? throw new InvalidOperationException("Serviço de pacotes WASAPI ausente."));
            return new(client, scope, clock, packets);
        }
        catch (Exception error)
        {
            scope.Dispose();
            if (!scope.Released)
            {
                WasapiResourceScope.Retain(client);
                throw new WasapiBusBindingCleanupException(error);
            }
            throw;
        }
    }

    public NativeClockSample ReadClock() => clock.Read().ToSample(client.Endpoint);

    public void Dispose()
    {
        scope.Dispose();
        if (scope.Released) return;
        WasapiResourceScope.Retain(this);
        throw new WasapiBusBindingCleanupException();
    }
}

internal sealed class WasapiBusBindingCleanupException(Exception? error = null)
    : InvalidOperationException("Liberação incerta dos serviços do barramento WASAPI.", error);
