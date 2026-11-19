namespace WaveStudio.Audio.Engine;

// Consentimento de uso único e ligado à seleção e à parada do operador.
internal sealed class WasapiAudibleConsent
{
    private readonly TransportRoute route;
    private int used;
    public CancellationToken StopToken { get; }

    private WasapiAudibleConsent(TransportRoute route, CancellationToken stopToken) =>
        (this.route, StopToken) = (route, stopToken);

    public static WasapiAudibleConsent Confirm(TransportRoute route, bool headsetConfirmed, bool lowLevelConfirmed,
        CancellationToken operatorStop)
    {
        route.Validate();
        if (!headsetConfirmed || !lowLevelConfirmed || !operatorStop.CanBeCanceled)
            throw new InvalidOperationException("A prova audível exige headset, nível baixo e parada do operador.");
        operatorStop.ThrowIfCancellationRequested();
        return new(route, operatorStop);
    }

    internal void Consume(TransportRoute selected)
    {
        StopToken.ThrowIfCancellationRequested();
        if (selected != route || Interlocked.Exchange(ref used, 1) != 0)
            throw new InvalidOperationException("Consentimento incompatível com a seleção ou já utilizado.");
    }
}

// Sem setter de ganho. A única entrada nativa é a prova interna isolada.
internal sealed class WasapiAudibleProofSession : IAsyncDisposable
{
    public const float ProofGain = .05f;
    private readonly WasapiSessionCore core;

    private WasapiAudibleProofSession(TransportRoute route, WasapiAudibleConsent consent, TransportActivityGuard? guard,
        Func<TransportEndpoint, IWasapiPreparationDevice>? open = null, WasapiDriftSimulation? pacing = null)
    {
        core = new(route, guard, open, simulation: pacing, nominal: new(new(route.Playback.Mix!), consent));
    }

    public static WasapiAudibleProofSession CreateNative(TransportRoute route, WasapiAudibleConsent consent) =>
        new(route, consent, null);

    internal static WasapiAudibleProofSession CreateSimulated(TransportRoute route, WasapiAudibleConsent consent,
        TransportActivityGuard guard, Func<TransportEndpoint, IWasapiPreparationDevice> open, WasapiDriftSimulation pacing) =>
        new(route, consent, guard, open, pacing);

    public async Task<WasapiAudibleReport> RunAsync(TimeSpan duration, CancellationToken token)
    {
        var result = await core.RunAsync(duration, token);
        return new(core.ReadNominalReport(result), core.ReadAudibleOutput(result));
    }
    internal bool Running => core.NominalRunning;
    internal void Step() => core.StepNominal();
    public ValueTask DisposeAsync() => core.DisposeAsync();
}

internal readonly record struct WasapiEncodedBlock(int NonZeroFrames, float PeakAmplitude, float PeakGain);
internal readonly record struct WasapiAudibleOutput(long SilentFrames, long PcmFrames, long NonZeroFrames,
    float PeakAmplitude, float PeakGain, long ClippedSamples);
internal sealed record WasapiAudibleReport(WasapiNominalReport Transport, WasapiAudibleOutput? Output);
