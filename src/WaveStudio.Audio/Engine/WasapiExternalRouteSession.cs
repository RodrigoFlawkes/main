namespace WaveStudio.Audio.Engine;

// Cada tentativa tem uma geração comum, uma rota e os mesmos donos WASAPI das provas anteriores.
internal sealed class WasapiExternalRouteSession : IAsyncDisposable
{
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(5);
    public const float BypassGain = .05f;
    public const float EffectGain = .0125f;
    private readonly WasapiSessionCore core;
    private readonly WasapiNominalControl nominal;
    private readonly ExternalFlow flow;
    private int attempted;
    internal ExternalRouteReport? Result { get; private set; }

    internal WasapiExternalRouteSession(ExternalRouteTopology topology, ExternalFlow flow, ExternalRouteConsent consent,
        TransportActivityGuard? guard = null, Func<TransportEndpoint, IWasapiPreparationDevice>? open = null,
        WasapiDriftSimulation? pacing = null)
        : this(Select(topology, flow, consent), flow, consent.Stop, guard, open, pacing) { }

    private static TransportRoute Select(ExternalRouteTopology topology, ExternalFlow flow, ExternalRouteConsent consent)
    {
        topology.Validate();
        consent.Consume(topology, flow);
        return topology.Route(flow);
    }

    internal WasapiExternalRouteSession(TransportRoute route, ExternalFlow flow, CancellationToken stop,
        TransportActivityGuard? guard, Func<TransportEndpoint, IWasapiPreparationDevice>? open, WasapiDriftSimulation? pacing)
    {
        route.Validate();
        if (open is null && route.Capture.Evidence != FormatEvidence.ExactCapability ||
            open is not null && (route.Capture.Evidence != FormatEvidence.SimulatedExact || pacing is not { ApplyCorrection: false }))
            throw new ArgumentException("O modo externo exige capacidade exata e razão nominal.");
        this.flow = flow;
        var permission = WasapiAudibleConsent.Confirm(route, true, true, stop);
        nominal = new(new(route.Playback.Mix!, compareGains: true), permission, externalRoute: true);
        core = new(route, guard, open, simulation: pacing, nominal: nominal);
    }

    public bool Running => core.NominalRunning;
    public void SetBypass(bool bypass) => nominal.SetExternalBypass(bypass);
    public void Invalidate(long generation) => core.Invalidate(generation);
    internal void Step() => core.StepNominal();

    public async Task<ExternalRouteReport> RunAsync(TimeSpan duration, CancellationToken token)
    {
        if (duration < TimeSpan.FromMilliseconds(50) || duration > MaximumDuration) throw new ArgumentOutOfRangeException(nameof(duration));
        if (Interlocked.Exchange(ref attempted, 1) != 0)
            throw new InvalidOperationException("Nova tentativa exige consentimento e seleção conferidos.");
        var result = await core.RunAsync(duration, token).ConfigureAwait(false);
        return Result = new(flow, core.ReadNominalReport(result), core.ReadAudibleOutput(result), core.ReadExternalLevels(result));
    }

    public ValueTask DisposeAsync() => core.DisposeAsync();
}

internal readonly record struct ExternalRouteLevels(long BypassNonZeroFrames, float BypassPeak,
    long EffectNonZeroFrames, float EffectPeak);

internal sealed record ExternalRouteReport(ExternalFlow Flow, WasapiNominalReport Transport, WasapiAudibleOutput? Output,
    ExternalRouteLevels? Levels)
{
    public bool Passed => Transport.NominalObserved && Output is { NonZeroFrames: > 0 } output &&
        output.NonZeroFrames <= output.PcmFrames && output.PeakGain <= WasapiExternalRouteSession.BypassGain &&
        output.PeakAmplitude <= WasapiExternalRouteSession.BypassGain + .5f / 32768 &&
        output.PcmFrames + output.SilentFrames == Transport.Cleanup.Playback.FramesReleased;
}
