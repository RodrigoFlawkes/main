using NAudio.CoreAudioApi;

namespace WaveStudio.Audio.Engine;

// As duas entradas de reprodução descritas no manual podem alimentar o mesmo driver.
internal sealed record NativeCableIsolation(string[] RenderIds, string[] EndpointIds)
{
    public static NativeCableIsolation Verify(SingleCableTopology topology)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var active = enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active);
        var render = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < active.Count; i++)
        {
            using var device = active[i];
            var label = device.Properties[PropertyKeys.PKEY_DeviceInterface_FriendlyName].Value as string;
            if (!StringComparer.OrdinalIgnoreCase.Equals(label, NativeExternalRoute.SingleCableAdapter) ||
                !StringComparer.OrdinalIgnoreCase.Equals(NativeExternalRoute.ConnectedAdapter(device), topology.Cable.Render.AdapterId))
                continue;
            all.Add(device.ID);
            if (device.DataFlow == DataFlow.Render) render.Add(device.ID);
        }
        if (!render.Contains(topology.Cable.Render.Endpoint.Id) || !all.Contains(topology.Cable.Capture.Endpoint.Id))
            throw new InvalidOperationException("Pinos ativos do adaptador não confirmados.");
        foreach (DataFlow direction in new[] { DataFlow.Render, DataFlow.Capture })
        foreach (Role role in new[] { Role.Console, Role.Multimedia, Role.Communications })
        {
            if (!enumerator.TryGetDefaultAudioEndpoint(direction, role, out var endpoint))
                throw new InvalidOperationException("Padrão do Windows indisponível para verificação.");
            using (endpoint)
                if (all.Contains(endpoint.ID)) throw new InvalidOperationException("Um pino do cabo é padrão no Windows.");
        }
        return new(render.Order(StringComparer.OrdinalIgnoreCase).ToArray(), all.Order(StringComparer.OrdinalIgnoreCase).ToArray());
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
