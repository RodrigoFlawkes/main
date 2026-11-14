using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NAudio.Wave;

namespace WaveStudio.Audio.Engine;

// Isolated native ownership. All acquisitions, queries and releases stay on this MTA owner.
// No NAudio private interface/RCW is borrowed; NAudio is reused only for detached format decoding.
internal sealed class NativeWasapiPreparationDevice(TransportEndpoint requested) : IWasapiPreparationDevice
{
    private NativeReference<IPreparationEnumerator>? enumerator;
    private NativeReference<IPreparationDevice>? device;
    private bool apartmentInitialized, disposed;
    private readonly int owner = Environment.CurrentManagedThreadId;
    public static IWasapiPreparationDevice Open(TransportEndpoint endpoint) => new NativeWasapiPreparationDevice(endpoint);

    public void ValidateIdentity(TransportEndpoint endpoint)
    {
        CheckOwner(); ObjectDisposedException.ThrowIf(disposed, this);
        if (endpoint != requested) throw new InvalidOperationException("Identidade/geração divergente.");
        if (enumerator is null)
        {
            int hr = CoInitializeEx(0, 0); // COINIT_MULTITHREADED; balance S_OK and S_FALSE alike.
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
        return new Client(NativeReference<IPreparationAudioClient>.Adopt(hr, pointer, "IMMDevice.Activate"));
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

    private sealed class Client(NativeReference<IPreparationAudioClient> client) : IWasapiPreparationClient
    {
        private nint format;
        private bool initialized;
        public WasapiPcm ReadMixFormat()
        {
            _ = client.Value; // Owner/disposal check before touching memory.
            if (format != 0) throw new InvalidOperationException("Formato já consultado.");
            WasapiCallException.RequireSuccess(client.Value.GetMixFormat(out format), "IAudioClient.GetMixFormat");
            if (format == 0) throw new InvalidOperationException("Formato nativo ausente.");
            return WasapiPcm.From(WaveFormat.MarshalFromPtr(format));
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
            var session = Guid.NewGuid(); // Isolated session; never attaches to the diagnostic/session-volume controls.
            WasapiCallException.RequireSuccess(client.Value.Initialize(0, 0, 0, 0, format, in session), "IAudioClient.Initialize");
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
        public void Dispose()
        {
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

    // Unique generated RCW + counted raw reference. Unlike classic RCW caching, each owner is independent.
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
