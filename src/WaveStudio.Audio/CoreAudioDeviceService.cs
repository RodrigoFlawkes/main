using NAudio.CoreAudioApi;

namespace WaveStudio.Audio;

public sealed class CoreAudioDeviceService
{
    private readonly SemaphoreSlim queryGate = new(1, 1);

    public async Task<DeviceCatalog> QueryAsync(CancellationToken cancellationToken = default)
    {
        await queryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // As threads do pool usam MTA. Todos os wrappers COM ficam restritos a esta consulta.
            return await Task.Run(() => Query(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            queryGate.Release();
        }
    }

    private static DeviceCatalog Query(CancellationToken cancellationToken)
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = new List<DeviceSnapshot>();
        var warnings = new List<string>();
        ReadDirection(enumerator, DataFlow.Render, EndpointDirection.Playback, devices, warnings, cancellationToken);
        ReadDirection(enumerator, DataFlow.Capture, EndpointDirection.Capture, devices, warnings, cancellationToken);
        return new DeviceCatalog(devices.AsReadOnly(), warnings.AsReadOnly());
    }

    private static void ReadDirection(MMDeviceEnumerator enumerator, DataFlow flow,
        EndpointDirection direction, List<DeviceSnapshot> devices, List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            string? multimedia = ReadDefault(enumerator, flow, Role.Multimedia, warnings);
            string? communications = ReadDefault(enumerator, flow, Role.Communications, warnings);
            using var collection = enumerator.EnumerateAudioEndPoints(flow, DeviceState.All);
            for (int index = 0; index < collection.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var device = collection[index];
                    devices.Add(ReadDevice(device, direction, multimedia, communications));
                }
                catch (Exception error) when (AudioError.IsDeviceAccessError(error))
                {
                    warnings.Add(AudioError.Describe(error));
                }
            }
        }
        catch (Exception error) when (AudioError.IsDeviceAccessError(error))
        {
            warnings.Add($"{(direction == EndpointDirection.Playback ? "Reprodução" : "Captura")}: {AudioError.Describe(error)}");
        }
    }

    private static string? ReadDefault(MMDeviceEnumerator enumerator, DataFlow flow, Role role,
        List<string> warnings)
    {
        try
        {
            if (!enumerator.TryGetDefaultAudioEndpoint(flow, role, out var device))
                return null;
            using (device)
                return device.ID;
        }
        catch (Exception error) when (AudioError.IsDeviceAccessError(error))
        {
            warnings.Add($"Padrão do Windows ({flow}, {role}): {AudioError.Describe(error)}");
            return null;
        }
    }

    private static DeviceSnapshot ReadDevice(MMDevice device, EndpointDirection direction,
        string? multimedia, string? communications)
    {
        string id = device.ID;
        string name = "Nome indisponível";
        string? informationError = null;
        MixFormatSnapshot? mixFormat = null;
        var state = device.State;
        try
        {
            name = device.FriendlyName;
            if (state == DeviceState.Active)
            {
                using var client = device.CreateAudioClient();
                var format = client.MixFormat;
                mixFormat = new MixFormatSnapshot(format.SampleRate, format.Channels,
                    format.BitsPerSample, format.Encoding.ToString());
            }
        }
        catch (Exception error) when (AudioError.IsDeviceAccessError(error))
        {
            informationError = AudioError.Describe(error);
        }

        var endpointState = state switch
        {
            DeviceState.Active => EndpointState.Active,
            DeviceState.Disabled => EndpointState.Disabled,
            DeviceState.NotPresent => EndpointState.NotPresent,
            DeviceState.Unplugged => EndpointState.Unplugged,
            _ => EndpointState.Unknown
        };
        return new DeviceSnapshot(id, name, direction, endpointState,
            id == multimedia, id == communications, mixFormat, informationError);
    }
}
