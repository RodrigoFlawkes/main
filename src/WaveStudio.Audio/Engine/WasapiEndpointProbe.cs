using NAudio.CoreAudioApi;

namespace WaveStudio.Audio.Engine;

// Query-only adapter: no Initialize, Start, GetService(clock/buffer), volume setters or defaults.
internal static class WasapiEndpointProbe
{
    public static Task<WasapiEndpointInfo> QueryAsync(TransportEndpoint endpoint, CancellationToken token = default)
    {
        endpoint.Validate();
        return Task.Run(() => Query(endpoint, token), token);
    }
    private static WasapiEndpointInfo Query(TransportEndpoint endpoint, CancellationToken token)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA) throw new InvalidOperationException("Consulta exige MTA.");
        WasapiPcm? format = null; long? period = null, minimum = null;
        EndpointState state = EndpointState.Unknown;
        try
        {
            token.ThrowIfCancellationRequested();
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(endpoint.Id);
            if (device.ID != endpoint.Id || device.DataFlow != (endpoint.Direction == EndpointDirection.Capture ? DataFlow.Capture : DataFlow.Render))
                throw new InvalidOperationException("Identidade/direção divergente; nenhum outro endpoint será aberto.");
            state = device.State switch { DeviceState.Active => EndpointState.Active, DeviceState.Disabled => EndpointState.Disabled,
                DeviceState.NotPresent => EndpointState.NotPresent, DeviceState.Unplugged => EndpointState.Unplugged, _ => EndpointState.Unknown };
            if (state != EndpointState.Active) return new(endpoint, state, null, null, null, null, null, FormatEvidence.MixOnly, false, "Endpoint indisponível.");
            using var client = device.CreateAudioClient();
            var native = client.MixFormat;
            format = WasapiPcm.From(native);
            period = client.DefaultDevicePeriod; minimum = client.MinimumDevicePeriod;
            bool exact = client.IsFormatSupported(AudioClientShareMode.Shared, native, out var closest) && closest is null;
            token.ThrowIfCancellationRequested();
            string? limitation = null;
            try { format.Validate(); } catch (NotSupportedException error) { limitation = error.Message; }
            return new(endpoint, state, format, period, minimum, null, null,
                exact ? FormatEvidence.ExactCapability : FormatEvidence.MixOnly, false,
                limitation ?? (exact ? null : "Capacidade exata não confirmada; closest format não é fallback."));
        }
        catch (Exception error) when (AudioError.IsDeviceAccessError(error) || error is InvalidOperationException)
        {
            return new(endpoint, state, format, period, minimum, null, null, FormatEvidence.MixOnly, false, AudioError.Describe(error));
        }
    }
}
