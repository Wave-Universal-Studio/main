using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NAudio.Wave;

namespace WaveStudio.Audio.Engine;

// Propriedade isolada dos recursos nativos. Aquisição, consulta e liberação ocorrem nesta thread MTA proprietária.
// Não usa interfaces privadas ou RCWs da NAudio. Reutiliza a biblioteca apenas para decodificar o formato de forma independente.
internal sealed class NativeWasapiPreparationDevice(TransportEndpoint requested, uint streamFlags = 0) : IWasapiPreparationDevice
{
    private NativeReference<IPreparationEnumerator>? enumerator;
    private NativeReference<IPreparationDevice>? device;
    private bool apartmentInitialized, disposed;
    private readonly int owner = Environment.CurrentManagedThreadId;
    public static IWasapiPreparationDevice Open(TransportEndpoint endpoint) => new NativeWasapiPreparationDevice(endpoint);
    public static IWasapiPreparationDevice OpenNonPersistent(TransportEndpoint endpoint) =>
        new NativeWasapiPreparationDevice(endpoint, streamFlags: 0x00080000); // AUDCLNT_STREAMFLAGS_NOPERSIST

    public void ValidateIdentity(TransportEndpoint endpoint)
    {
        CheckOwner(); ObjectDisposedException.ThrowIf(disposed, this);
        if (endpoint != requested) throw new InvalidOperationException("Identidade/geração divergente.");
        if (enumerator is null)
        {
            int hr = CoInitializeEx(0, 0); // COINIT_MULTITHREADED. Balanceia S_OK e S_FALSE da mesma forma.
            if (hr is not (0 or 1)) throw new WasapiCallException("CoInitializeEx", hr);
            apartmentInitialized = true;
            var clsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
            var iid = typeof(IPreparationEnumerator).GUID;
            hr = CoCreateInstance(in clsid, 0, 1, in iid, out nint pointer); // INPROC_SERVER
            enumerator = NativeReference<IPreparationEnumerator>.Adopt(hr, pointer, "CoCreateInstance");
        }
        if (device is null)
        {
            int hr = enumerator.Value.GetDevice(endpoint.Id, out nint pointer);
            device = NativeReference<IPreparationDevice>.Adopt(hr, pointer, "IMMDeviceEnumerator.GetDevice");
        }
        nint id = 0;
        try
        {
            WasapiCallException.RequireSuccess(device.Value.GetId(out id), "IMMDevice.GetId");
            if (Marshal.PtrToStringUni(id) != endpoint.Id) throw new InvalidOperationException("Endpoint divergente; sem fallback.");
        }
        finally { Marshal.FreeCoTaskMem(id); }
        WasapiCallException.RequireSuccess(((IPreparationEndpoint)device.Value).GetDataFlow(out int flow), "IMMEndpoint.GetDataFlow");
        WasapiCallException.RequireSuccess(device.Value.GetState(out uint state), "IMMDevice.GetState");
        int expectedFlow = endpoint.Direction == EndpointDirection.Capture ? 1 : 0;
        if (flow != expectedFlow || state != 1) throw new InvalidOperationException("Direção/estado divergente; endpoint ativo obrigatório.");
    }

    public IWasapiPreparationClient Activate()
    {
        ValidateIdentity(requested);
        var iid = typeof(IPreparationAudioClient).GUID;
        int hr = device!.Value.Activate(in iid, 1, 0, out nint pointer);
        return new Client(NativeReference<IPreparationAudioClient>.Adopt(hr, pointer, "IMMDevice.Activate"), requested, streamFlags);
    }
    private void CheckOwner()
    {
        if (owner != Environment.CurrentManagedThreadId || Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA)
            throw new InvalidOperationException("Recurso WASAPI pertence ao MTA de preparação.");
    }
    public void Dispose()
    {
        CheckOwner(); if (disposed) return;
        device?.Dispose(); device = null;
        enumerator?.Dispose(); enumerator = null;
        if (apartmentInitialized) { CoUninitialize(); apartmentInitialized = false; }
        disposed = true;
    }

    private sealed class Client(NativeReference<IPreparationAudioClient> client, TransportEndpoint endpoint, uint streamFlags) : IWasapiNativeBoundStreamClient
    {
        public TransportEndpoint Endpoint => endpoint;
        private WasapiPcm? negotiated;
        private nint format;
        public WasapiPcm PreparedFormat
        {
            get
            {
                _ = client.Value;
                return initialized && negotiated is { } ready ? ready :
                    throw new InvalidOperationException("Cliente WASAPI sem formato preparado.");
            }
        }
        private bool initialized, busServicesBorrowed;

        public IDisposable BorrowBusServices()
        {
            _ = PreparedFormat;
            if (busServicesBorrowed) throw new InvalidOperationException("Cliente WASAPI já vinculado a um barramento.");
            var lease = new BusServicesLease(this);
            busServicesBorrowed = true;
            return lease;
        }

        private sealed class BusServicesLease(Client owner) : IDisposable
        {
            private bool active = true;
            public void Dispose()
            {
                if (!active) return;
                owner.ReleaseBusServices();
                active = false;
            }
        }

        private void ReleaseBusServices()
        {
            _ = client.Value;
            busServicesBorrowed = false;
        }
        public WasapiPcm ReadMixFormat()
        {
            _ = client.Value; // Verifica a propriedade e o descarte antes de acessar a memória.
            if (format != 0) throw new InvalidOperationException("Formato já consultado.");
            WasapiCallException.RequireSuccess(client.Value.GetMixFormat(out format), "IAudioClient.GetMixFormat");
            if (format == 0) throw new InvalidOperationException("Formato nativo ausente.");
            return negotiated = WasapiPcm.From(WaveFormat.MarshalFromPtr(format));
        }
        public void RequireExactSupport()
        {
            if (format == 0) throw new InvalidOperationException("Formato não consultado.");
            nint closest = 0;
            try
            {
                WasapiCallException.RequireSuccess(client.Value.IsFormatSupported(0, format, out closest), "IAudioClient.IsFormatSupported");
                if (closest != 0) throw new InvalidOperationException("Capacidade exata não confirmada; closest recusado.");
            }
            finally { Marshal.FreeCoTaskMem(closest); }
        }
        public (long, long) ReadPeriods()
        {
            WasapiCallException.RequireSuccess(client.Value.GetDevicePeriod(out long p, out long m), "IAudioClient.GetDevicePeriod");
            return (p, m);
        }
        public void Initialize()
        {
            if (format == 0 || initialized) throw new InvalidOperationException("Preparação fora de sequência.");
            var session = Guid.NewGuid(); // Sessão isolada, sem vínculo com os controles de diagnóstico ou de volume das sessões.
            WasapiCallException.RequireSuccess(client.Value.Initialize(0, streamFlags, 0, 0, format, in session), "IAudioClient.Initialize");
            initialized = true;
        }
        public (uint, long) ReadBufferInfo()
        {
            WasapiCallException.RequireSuccess(client.Value.GetBufferSize(out uint f), "IAudioClient.GetBufferSize");
            WasapiCallException.RequireSuccess(client.Value.GetStreamLatency(out long l), "IAudioClient.GetStreamLatency");
            return (f, l);
        }
        public IWasapiPreparationClock OpenClock()
        {
            if (!initialized) throw new InvalidOperationException("Cliente ainda não inicializado.");
            var iid = typeof(IPreparationAudioClock).GUID;
            int hr = client.Value.GetService(in iid, out nint pointer);
            return new Clock(NativeReference<IPreparationAudioClock>.Adopt(hr, pointer, "IAudioClient.GetService(IAudioClock)"));
        }
        public int Start() => client.Value.Start();
        public int Stop() => client.Value.Stop();
        public void RevalidateFormat(WasapiPcm expected)
        {
            nint current = 0, closest = 0;
            try
            {
                WasapiCallException.RequireSuccess(client.Value.GetMixFormat(out current), "IAudioClient.GetMixFormat(revalidate)");
                if (current == 0 || WasapiPcm.From(WaveFormat.MarshalFromPtr(current)) != expected)
                    throw new InvalidOperationException("Formato mudou antes de Start; sem fallback.");
                WasapiCallException.RequireSuccess(client.Value.IsFormatSupported(0, current, out closest), "IAudioClient.IsFormatSupported(revalidate)");
                if (closest != 0) throw new InvalidOperationException("Formato exato indisponível antes de Start.");
            }
            finally { Marshal.FreeCoTaskMem(closest); Marshal.FreeCoTaskMem(current); }
        }
        public uint ReadPadding()
        {
            WasapiCallException.RequireSuccess(client.Value.GetCurrentPadding(out uint frames), "IAudioClient.GetCurrentPadding");
            return frames;
        }
        public IWasapiCapturePackets OpenCapture()
        {
            var iid = typeof(INativeCaptureClient).GUID;
            int hr = client.Value.GetService(in iid, out nint pointer);
            return new CapturePackets(NativeReference<INativeCaptureClient>.Adopt(hr, pointer, "GetService(IAudioCaptureClient)"));
        }
        public IWasapiRenderPackets OpenRender()
        {
            var iid = typeof(INativeRenderClient).GUID;
            int hr = client.Value.GetService(in iid, out nint pointer);
            return new RenderPackets(NativeReference<INativeRenderClient>.Adopt(hr, pointer, "GetService(IAudioRenderClient)"));
        }
        public void Dispose()
        {
            if (busServicesBorrowed) throw new InvalidOperationException("Libere os serviços do barramento antes do cliente WASAPI.");
            if (format != 0) { _ = client.Value; Marshal.FreeCoTaskMem(format); format = 0; }
            client.Dispose();
        }
    }
    private sealed class Clock(NativeReference<IPreparationAudioClock> clock) : IWasapiPreparationClock
    {
        public WasapiClockReading Read()
        {
            int fhr = clock.Value.GetFrequency(out ulong frequency);
            int phr = clock.Value.GetPosition(out ulong position, out ulong qpc);
            return new(fhr, frequency, phr, position, qpc);
        }
        public void Dispose() => clock.Dispose();
    }
    private sealed class CapturePackets(NativeReference<INativeCaptureClient> packets) : IWasapiCapturePackets, IWasapiNativePacketService
    {
        public uint NextPacketSize()
        {
            WasapiCallException.RequireSuccess(packets.Value.GetNextPacketSize(out uint frames), "IAudioCaptureClient.GetNextPacketSize");
            return frames;
        }
        public WasapiCaptureBuffer GetBuffer()
        {
            int hr = packets.Value.GetBuffer(out nint data, out uint frames, out uint flags, out ulong position, out ulong qpc);
            return new(hr, data, frames, flags, position, qpc);
        }
        public int ReleaseBuffer(uint framesRead) => packets.Value.ReleaseBuffer(framesRead);
        public void Dispose() => packets.Dispose();
    }
    private sealed class RenderPackets(NativeReference<INativeRenderClient> packets) : IWasapiRenderPackets, IWasapiNativePacketService
    {
        public nint GetBuffer(uint frames)
        {
            WasapiCallException.RequireSuccess(packets.Value.GetBuffer(frames, out nint data), "IAudioRenderClient.GetBuffer");
            return data;
        }
        public int ReleaseBuffer(uint framesWritten, uint flags) => packets.Value.ReleaseBuffer(framesWritten, flags);
        public void Dispose() => packets.Dispose();
    }

    // RCW gerado único e referência bruta contada. Cada proprietário é independente do cache clássico de RCWs.
    private sealed class NativeReference<T>(T value) : IDisposable where T : class
    {
        private T? current = value;
        private readonly int owner = Environment.CurrentManagedThreadId;
        private static readonly StrategyBasedComWrappers wrappers = new();
        public T Value => owner != Environment.CurrentManagedThreadId
            ? throw new InvalidOperationException("COM acessado fora do proprietário.") : current ?? throw new ObjectDisposedException(typeof(T).Name);
        public static NativeReference<T> Adopt(int hr, nint pointer, string stage)
        {
            ComObject? wrapper = null;
            try
            {
                WasapiCallException.RequireSuccess(hr, stage);
                if (pointer == 0) throw new InvalidOperationException("Interface COM ausente.");
                wrapper = (ComObject)wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
                return new((T)(object)wrapper);
            }
            catch { wrapper?.FinalRelease(); throw; }
            finally { if (pointer != 0) Marshal.Release(pointer); }
        }
        public void Dispose()
        {
            if (owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("COM liberado fora do proprietário.");
            if (current is null) return;
            ((ComObject)(object)current).FinalRelease(); current = null;
        }
    }
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(nint reserved, uint coinit);
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
}
