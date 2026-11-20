using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using WaveStudio.Audio.Engine;

namespace WaveStudio.Audio;

internal static class NativeRouteInventory
{
    private const string CableService = "VBAudioVACMME";
    // Combina o filtro de serviço com PRESENT e DONOTGENERATE para jamais criar um devnode durante a leitura.
    private const uint PresentServiceWithoutCreation = 0x10000142;

    internal static Task<RouteInventory> QueryAsync(CancellationToken token) =>
        Task.Run(() => Read(token), token);

    private static RouteInventory Read(CancellationToken token)
    {
        using var enumerator = new MMDeviceEnumerator();
        var ports = new List<RoutePort>();
        var defaults = new List<string?>(6);
        var warnings = new List<string>();
        bool complete = true;
        foreach (var (flow, direction) in new[] { (DataFlow.Render, EndpointDirection.Playback), (DataFlow.Capture, EndpointDirection.Capture) })
        {
            token.ThrowIfCancellationRequested();
            string? console = CoreAudioDeviceService.ReadDefault(enumerator, flow, Role.Console, warnings);
            string? multimedia = CoreAudioDeviceService.ReadDefault(enumerator, flow, Role.Multimedia, warnings);
            string? communications = CoreAudioDeviceService.ReadDefault(enumerator, flow, Role.Communications, warnings);
            defaults.AddRange([console, multimedia, communications]);
            try
            {
                using var collection = enumerator.EnumerateAudioEndPoints(flow, DeviceState.All);
                for (int index = 0; index < collection.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    using var endpoint = collection[index];
                    var device = CoreAudioDeviceService.ReadDevice(endpoint, direction, multimedia, communications);
                    ports.Add(ReadPort(enumerator, endpoint, device));
                }
            }
            catch (Exception error) when (AudioError.IsDeviceAccessError(error))
            {
                complete = false;
            }
        }
        return new(ports.AsReadOnly(), defaults.AsReadOnly(), ReadSingleDrivers(token), complete);
    }

    private static RoutePort ReadPort(MMDeviceEnumerator enumerator, MMDevice endpoint, DeviceSnapshot device)
    {
        var kind = RouteEndpointKind.Other;
        string? connection = null;
        PnpAudioAdapter? adapter = null;
        try
        {
            var name = endpoint.Properties[PropertyKeys.PKEY_DeviceInterface_FriendlyName].Value as string;
            kind = name?.ToUpperInvariant() switch
            {
                "VB-AUDIO VIRTUAL CABLE" => RouteEndpointKind.Single,
                "VB-AUDIO CABLE A" => RouteEndpointKind.CableA,
                "VB-AUDIO CABLE B" => RouteEndpointKind.CableB,
                _ => RouteEndpointKind.Other
            };
            if (device.State == EndpointState.Active)
            {
                connection = NativeExternalRoute.ConnectedAdapter(endpoint);
                adapter = NativeSingleCableIdentity.ReadConnected(enumerator, connection);
                if (adapter.Value.HasCableMarker)
                    kind = RouteEndpointKind.Single;
                else if (kind == RouteEndpointKind.Other && HasHardwareBus(adapter.Value.InstanceId))
                    kind = RouteEndpointKind.Physical;
            }
        }
        catch (Exception error) when (AudioError.IsDeviceAccessError(error)) { }
        return new(device, kind, adapter, connection);
    }

    private static bool HasHardwareBus(string instance) =>
        NativeSingleCableIdentity.ReadEnumerator(instance).ToUpperInvariant() is
            "USB" or "HDAUDIO" or "INTELAUDIO" or "PCI" or "BTHENUM" or "BTHHFENUM" or "BTHLEDEVICE";

    private static IReadOnlyList<PnpAudioAdapter>? ReadSingleDrivers(CancellationToken token)
    {
        uint result = CM_Get_Device_ID_List_SizeW(out uint length, CableService, PresentServiceWithoutCreation);
        if (result != 0 || length == 0 || length > 32768) return null;
        var buffer = new char[length];
        if (CM_Get_Device_ID_ListW(CableService, buffer, length, PresentServiceWithoutCreation) != 0)
            return null;

        var drivers = new List<PnpAudioAdapter>();
        try
        {
            foreach (var id in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                token.ThrowIfCancellationRequested();
                var adapter = NativeSingleCableIdentity.ReadInstance(id);
                if (adapter.IsSingleCable) drivers.Add(adapter);
            }
        }
        catch (Exception error) when (AudioError.IsDeviceAccessError(error))
        {
            return null;
        }
        return drivers.AsReadOnly();
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_ListW(string filter, [Out] char[] buffer, uint length, uint flags);
}
