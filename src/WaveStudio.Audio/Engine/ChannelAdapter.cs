namespace WaveStudio.Audio.Engine;

internal enum ChannelLayout { Mono = 1, StereoLeftRight = 2 }
internal enum ChannelPolicy { Preserve, DuplicateMono, AverageStereo }
internal readonly record struct ChannelAdaptation(int Frames, int SanitizedSamples);

internal sealed class ChannelAdapter
{
    public int InputChannels { get; }
    public int OutputChannels { get; }
    public ChannelPolicy Policy { get; }
    public ChannelAdapter(ChannelLayout input, ChannelLayout output, ChannelPolicy policy)
    {
        if (input is not (ChannelLayout.Mono or ChannelLayout.StereoLeftRight) ||
            output is not (ChannelLayout.Mono or ChannelLayout.StereoLeftRight))
            throw new NotSupportedException("Layout desconhecido ou multicanal não suportado.");
        bool valid = policy switch
        {
            ChannelPolicy.Preserve => input == output,
            ChannelPolicy.DuplicateMono => input == ChannelLayout.Mono && output == ChannelLayout.StereoLeftRight,
            ChannelPolicy.AverageStereo => input == ChannelLayout.StereoLeftRight && output == ChannelLayout.Mono,
            _ => false
        };
        if (!valid) throw new ArgumentException("Escolha uma política explícita compatível com os layouts.");
        InputChannels = (int)input;
        OutputChannels = (int)output;
        Policy = policy;
    }

    public ChannelAdaptation Process(ReadOnlySpan<float> input, Span<float> output)
    {
        if (input.Length % InputChannels != 0 || input.Length / InputChannels > 4096 ||
            output.Length != input.Length / InputChannels * OutputChannels)
            throw new ArgumentException("Exigidos frames completos, até 4096 frames de entrada e destino de tamanho exato.");
        if (input.Overlaps(output)) throw new ArgumentException("Adaptação não permite buffers sobrepostos.");
        int sanitized = 0, frames = input.Length / InputChannels;
        for (int frame = 0; frame < frames; frame++)
        {
            float left = Finite(input[frame * InputChannels], ref sanitized);
            float right = InputChannels == 2 ? Finite(input[frame * 2 + 1], ref sanitized) : left;
            if (Policy == ChannelPolicy.AverageStereo) output[frame] = .5f * left + .5f * right;
            else
            {
                output[frame * OutputChannels] = left;
                if (OutputChannels == 2) output[frame * 2 + 1] = right;
            }
        }
        return new(frames, sanitized);
    }
    private static float Finite(float value, ref int count)
    {
        if (float.IsFinite(value)) return value;
        count++;
        return 0;
    }
}
