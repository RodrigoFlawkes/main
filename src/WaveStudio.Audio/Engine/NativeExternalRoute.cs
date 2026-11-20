using NAudio.CoreAudioApi;

namespace WaveStudio.Audio.Engine;

internal sealed record ExternalEndpointIds(string BrowserRender, string BrowserCapture, string MicRender,
    string MicCapture, string Headset, string Microphone);

internal sealed record SingleCableEndpointIds(string Render, string Capture, string Headset, string Microphone);

internal static class NativeExternalRoute
{
    public static async Task<ExternalRouteTopology> ProbeAsync(ExternalEndpointIds ids, long generation, CancellationToken token)
    {
        var aRender = await Probe(ids.BrowserRender, EndpointDirection.Playback, generation, "VB-Audio Cable A", token);
        var aCapture = await Probe(ids.BrowserCapture, EndpointDirection.Capture, generation, "VB-Audio Cable A", token);
        var bRender = await Probe(ids.MicRender, EndpointDirection.Playback, generation, "VB-Audio Cable B", token);
        var bCapture = await Probe(ids.MicCapture, EndpointDirection.Capture, generation, "VB-Audio Cable B", token);
        var headset = await Probe(ids.Headset, EndpointDirection.Playback, generation, null, token);
        var microphone = await Probe(ids.Microphone, EndpointDirection.Capture, generation, null, token);
        var topology = new ExternalRouteTopology(new(aRender, aCapture), new(bRender, bCapture), headset, microphone);
        topology.Validate();
        return topology;
    }

    internal const string SingleCableAdapter = "VB-Audio Virtual Cable";

    public static async Task<SingleCableTopology> ProbeSingleAsync(SingleCableEndpointIds ids, long generation, CancellationToken token)
    {
        var render = await Probe(ids.Render, EndpointDirection.Playback, generation, SingleCableAdapter, token, singleCable: true);
        var capture = await Probe(ids.Capture, EndpointDirection.Capture, generation, SingleCableAdapter, token, singleCable: true);
        var headset = await Probe(ids.Headset, EndpointDirection.Playback, generation, null, token, singleCable: true);
        var microphone = await Probe(ids.Microphone, EndpointDirection.Capture, generation, null, token, singleCable: true);
        var topology = new SingleCableTopology(new(render, capture), headset, microphone);
        topology.Validate();
        return topology;
    }

    private static async Task<ExternalEndpoint> Probe(string id, EndpointDirection direction, long generation,
        string? cableAdapter, CancellationToken token, bool singleCable = false)
    {
        var endpoint = new TransportEndpoint(id, direction, generation);
        var info = await WasapiEndpointProbe.QueryAsync(endpoint, token).ConfigureAwait(false);
        if (info is not { State: EndpointState.Active, Evidence: FormatEvidence.ExactCapability, Error: null })
            throw new InvalidOperationException("Endpoint externo sem capacidade exata.");
        string adapterId = await Task.Run(() => ReadAdapter(endpoint, cableAdapter, singleCable), token).ConfigureAwait(false);
        return new(info, adapterId);
    }

    private static string ReadAdapter(TransportEndpoint endpoint, string? cableAdapter, bool singleCable)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(endpoint.Id);
        if (device.ID != endpoint.Id || device.State != DeviceState.Active ||
            device.DataFlow != (endpoint.Direction == EndpointDirection.Capture ? DataFlow.Capture : DataFlow.Render))
            throw new InvalidOperationException("A seleção mudou durante a consulta.");
        var name = device.Properties[PropertyKeys.PKEY_DeviceInterface_FriendlyName].Value as string;
        if (cableAdapter is not null && !StringComparer.OrdinalIgnoreCase.Equals(name, cableAdapter))
            throw new InvalidOperationException("Adaptador do cabo candidato não confirmado.");
        return singleCable
            ? NativeSingleCableIdentity.Read(enumerator, device).RequireRole(expectsCable: cableAdapter is not null)
            : ConnectedAdapter(device);
    }

    internal static string ConnectedAdapter(MMDevice device)
    {
        // O ID da topologia conectada é opaco, independe do nome e não exige abrir um stream.
        var topology = device.DeviceTopology;
        if (topology.ConnectorCount != 1) throw new InvalidOperationException("Topologia ambígua do endpoint.");
        var connector = topology.GetConnector(0);
        string adapter = connector.ConnectedToDeviceId;
        if (string.IsNullOrWhiteSpace(adapter)) throw new InvalidOperationException("Identidade da topologia conectada indisponível.");
        return adapter;
    }
}
