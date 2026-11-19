namespace WaveStudio.Audio.Engine;

internal enum ExternalFlow { Browser, Microphone }
internal sealed record ExternalEndpoint(WasapiEndpointInfo Info, string AdapterId)
{
    public TransportEndpoint Endpoint => Info.Endpoint;
}
internal sealed record ExternalCable(ExternalEndpoint Render, ExternalEndpoint Capture);

// Duas cadeias de sentido único. IDs dos seis vértices e adaptadores dos cabos precisam ser distintos nos papéis que separam as cadeias.
internal sealed record ExternalRouteTopology(ExternalCable BrowserCable, ExternalCable MicCable,
    ExternalEndpoint Headset, ExternalEndpoint Microphone)
{
    public TransportRoute Route(ExternalFlow flow) => flow switch
    {
        ExternalFlow.Browser => new(BrowserCable.Capture.Info, Headset.Info),
        ExternalFlow.Microphone => new(Microphone.Info, MicCable.Render.Info),
        _ => throw new ArgumentOutOfRangeException(nameof(flow))
    };

    public void Validate()
    {
        ExternalEndpoint[] ports = [BrowserCable.Render, BrowserCable.Capture, MicCable.Render, MicCable.Capture, Headset, Microphone];
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var generation = Headset.Endpoint.Generation;
        var evidence = Headset.Info.Evidence;
        for (int i = 0; i < ports.Length; i++)
        {
            var port = ports[i];
            port.Endpoint.Validate();
            var expected = i % 2 == 0 ? EndpointDirection.Playback : EndpointDirection.Capture;
            if (port.Endpoint.Direction != expected || port.Endpoint.Generation != generation ||
                !ids.Add(port.Endpoint.Id) || string.IsNullOrWhiteSpace(port.AdapterId))
                throw new ArgumentException("Direção, geração ou identidade do grafo externo inválida.");
            if (port.Info.State != EndpointState.Active || port.Info.Evidence != evidence ||
                evidence is not (FormatEvidence.ExactCapability or FormatEvidence.SimulatedExact) ||
                port.Info.Mix is null || port.Info.Error is not null)
                throw new NotSupportedException("Todos os endpoints precisam estar ativos com capacidade exata.");
            port.Info.Mix.Validate();
        }
        if (!SameAdapter(BrowserCable.Render, BrowserCable.Capture) || !SameAdapter(MicCable.Render, MicCable.Capture) ||
            SameAdapter(BrowserCable.Render, MicCable.Render) ||
            SameAdapter(Headset, BrowserCable.Render) || SameAdapter(Headset, MicCable.Render) ||
            SameAdapter(Microphone, BrowserCable.Render) || SameAdapter(Microphone, MicCable.Render))
            throw new ArgumentException("Pareamento não confirmado, adaptador compartilhado ou retorno para um cabo virtual.");
        Route(ExternalFlow.Browser).Validate();
        Route(ExternalFlow.Microphone).Validate();
    }

    private static bool SameAdapter(ExternalEndpoint a, ExternalEndpoint b) =>
        StringComparer.OrdinalIgnoreCase.Equals(a.AdapterId, b.AdapterId);
}
