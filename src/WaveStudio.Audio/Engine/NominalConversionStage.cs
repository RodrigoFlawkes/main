namespace WaveStudio.Audio.Engine;

// One serial producer owns adaptation, phase, EOF and reset. A separate SPSC stage
// can receive only the ProducedFrames prefix; no concurrent lifecycle calls here.
internal sealed class NominalConversionStage
{
    private readonly ChannelAdapter adapter;
    private readonly NominalResampler resampler;
    private readonly float[] mapped;
    public PcmFloatFormat Input { get; }
    public PcmFloatFormat Output { get; }
    public long InputFrames => resampler.InputFrames;
    public long OutputFrames => resampler.OutputFrames;
    public bool Completed => resampler.Completed;
    public int StorageBytes => mapped.Length * sizeof(float) + resampler.StorageBytes;
    public double GroupDelayMilliseconds => resampler.GroupDelayMilliseconds;

    public NominalConversionStage(PcmFloatFormat input, PcmFloatFormat output, ChannelPolicy policy)
    {
        Input = input;
        Output = output;
        adapter = new((ChannelLayout)input.Channels, (ChannelLayout)output.Channels, policy);
        resampler = new(input.SampleRate, output.SampleRate, output.Channels);
        mapped = new float[NominalResampler.MaximumInputBlockFrames * output.Channels];
    }
    public ResamplingResult Process(ReadOnlySpan<float> input, Span<float> output)
    {
        int frames = Input.FramesFromSamples(input.Length);
        if (frames > NominalResampler.MaximumInputBlockFrames) throw new ArgumentException("Bloco de entrada excede o limite.");
        if (input.Overlaps(output)) throw new ArgumentException("Conversão não permite buffers sobrepostos.");
        var intermediate = mapped.AsSpan(0, frames * Output.Channels);
        adapter.Process(input, intermediate);
        var result = resampler.Process(intermediate, output);
        NeutralProcessor.Process(output[..(result.ProducedFrames * Output.Channels)]);
        return result;
    }
    public void BeginFlush() => resampler.BeginFlush();
    public ResamplingResult Flush(Span<float> output)
    {
        var result = resampler.Flush(output);
        NeutralProcessor.Process(output[..(result.ProducedFrames * Output.Channels)]);
        return result;
    }
    public void Reset() { resampler.Reset(); Array.Clear(mapped); }
}
