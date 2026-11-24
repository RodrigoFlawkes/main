namespace WaveStudio.Audio.Engine;

internal enum MixBus { Personal, Stream }
internal enum MixSourceKind { Game, Communication, Microphone, Auxiliary }
internal enum MixOrigin { Discrete, Aggregated, BusOutput }

internal readonly record struct MixLevel(float Gain, bool Muted = false)
{
    public float EffectiveGain => Muted ? 0 : Gain;

    public void Validate()
    {
        if (!float.IsFinite(Gain) || Gain is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Gain), "O ganho deve estar entre zero e um.");
    }
}

// A identidade representa o fluxo de captura vinculado pelo transporte. O papel é atribuído a esse fluxo,
// sem inferência por PID, rótulo ou sessão de volume do Windows. Origem agregada continua agregada.
internal sealed record MixSource(TransportEndpoint Endpoint, PcmFloatFormat Format, MixSourceKind Kind,
    MixOrigin Origin, ChannelPolicy Channels, MixLevel? Personal = null, MixLevel? Stream = null,
    bool AllowSensitiveStream = false, string? ReturnCallId = null);

internal sealed record MixExternalDestination(string Id, string? CallId = null);
internal readonly record struct MixReadResult(TransportEndpoint Endpoint, long Block, int Frames);
internal readonly record struct MixMeter(float Peak, double Rms, int ClippedSamples);
internal readonly record struct BusMeters(MixMeter Personal, MixMeter Stream);

// Igual ao contrato de entrega isolada, o ticket é irreversível e deve ser conferido no ponto de publicação.
internal sealed class MixDeliveryTicket(long generation, PcmFloatFormat format,
    MixExternalDestination? external, CancellationToken cancellation)
{
    private int invalid;
    public long Generation { get; } = generation;
    public PcmFloatFormat Format { get; } = format;
    public MixExternalDestination? External { get; } = external;
    public bool IsValid => Volatile.Read(ref invalid) == 0 && !cancellation.IsCancellationRequested;
    internal void Invalidate() => Interlocked.Exchange(ref invalid, 1);
}

// Todas as chamadas são síncronas, limitadas e sem espera. Os spans só valem até o retorno.
// O produtor deve alinhar as fontes à mesma origem de tempo da geração. Cada índice representa 10 ms
// nesse relógio, com exatamente taxa/100 frames PCM entrelaçados em cada fonte. Atrasos ou blocos
// incompletos são sinalizados no resultado. Um transporte que enfileire cópias deve descartar
// tickets invalidados também no consumo e nunca publicar dados antigos em uma rota nova.
internal interface IBusPcmTransport
{
    MixReadResult Read(MixSource source, long block, Span<float> pcm);
    bool Submit(MixDeliveryTicket ticket, long block, ReadOnlySpan<float> personal, ReadOnlySpan<float> stream, BusMeters meters);
    bool SubmitExternal(MixDeliveryTicket ticket, long block, ReadOnlySpan<float> stream);
}
