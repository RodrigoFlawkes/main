using NAudio.CoreAudioApi;

namespace WaveStudio.Audio.Engine;

internal readonly record struct SingleCablePin(string Id, EndpointDirection Direction, bool NamedCandidate, PnpAudioAdapter Adapter);

// As duas entradas de reprodução descritas no manual podem alimentar o mesmo driver.
internal sealed record NativeCableIsolation(string[] RenderIds, string[] EndpointIds)
{
    public static NativeCableIsolation Verify(SingleCableTopology topology)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var active = enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active);
        var pins = new List<SingleCablePin>();
        for (int i = 0; i < active.Count; i++)
        {
            using var device = active[i];
            var label = device.Properties[PropertyKeys.PKEY_DeviceInterface_FriendlyName].Value as string;
            var direction = device.DataFlow == DataFlow.Render ? EndpointDirection.Playback : EndpointDirection.Capture;
            pins.Add(new(device.ID, direction, StringComparer.OrdinalIgnoreCase.Equals(label, NativeExternalRoute.SingleCableAdapter),
                NativeSingleCableIdentity.Read(enumerator, device)));
        }
        var isolation = IdentifyPins(topology.Cable.Render.AdapterId, topology.Cable.Render.Endpoint.Id,
            topology.Cable.Capture.Endpoint.Id, pins);
        foreach (DataFlow direction in new[] { DataFlow.Render, DataFlow.Capture })
        foreach (Role role in new[] { Role.Console, Role.Multimedia, Role.Communications })
        {
            if (!enumerator.TryGetDefaultAudioEndpoint(direction, role, out var endpoint))
                throw new InvalidOperationException("Padrão do Windows indisponível para verificação.");
            using (endpoint)
                if (isolation.EndpointIds.Contains(endpoint.ID, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Um pino do cabo é padrão no Windows.");
        }
        return isolation;
    }

    internal static NativeCableIsolation IdentifyPins(string instance, string selectedRender, string selectedCapture,
        IEnumerable<SingleCablePin> pins)
    {
        var ids = StringComparer.OrdinalIgnoreCase;
        var render = new HashSet<string>(ids);
        var all = new HashSet<string>(ids);
        bool captureFound = false;
        foreach (var pin in pins)
        {
            bool sameInstance = ids.Equals(pin.Adapter.InstanceId, instance);
            if (!sameInstance && !pin.NamedCandidate && !pin.Adapter.HasCableMarker)
                continue;

            pin.Adapter.RequireRole(expectsCable: true);
            if (!sameInstance || !all.Add(pin.Id))
                throw new InvalidOperationException("Instâncias ou pinos do cabo ambíguos.");

            if (pin.Direction == EndpointDirection.Playback)
                render.Add(pin.Id);
            else if (pin.Direction == EndpointDirection.Capture && ids.Equals(pin.Id, selectedCapture))
                captureFound = true;
            else
                throw new InvalidOperationException("Pino de captura do cabo divergente da seleção.");
        }

        if (!render.Contains(selectedRender) || !captureFound)
            throw new InvalidOperationException("Pinos ativos do adaptador não confirmados.");
        return new(render.Order(ids).ToArray(), all.Order(ids).ToArray());
    }

    public bool HasNoForeignProducer(int? allowedPid = null, string? ownRenderId = null)
    {
        using var enumerator = new MMDeviceEnumerator();
        foreach (string id in RenderIds)
        {
            using var device = enumerator.GetDevice(id);
            if (device.ID != id || device.State != DeviceState.Active || device.DataFlow != DataFlow.Render)
                return false;
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                using var session = sessions[i];
                var state = (SessionActivity)(int)session.State;
                if (state is SessionActivity.Inactive or SessionActivity.Expired) continue;
                if (state != SessionActivity.Active || !allowedPid.HasValue || !StringComparer.Ordinal.Equals(id, ownRenderId) ||
                    session.IsSystemSoundsSession ||
                    session.GetProcessID != (uint)allowedPid.Value)
                    return false;
            }
        }
        return true;
    }
}
