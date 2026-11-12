using WaveStudio.Audio;

namespace WaveStudio.Tests;

internal sealed class FakeEndpointState
{
    public double Volume = 0.4;
    public bool Muted;
    public StereoLevels Stereo = new(0.4, 0.4);
    public int Channels = 2;
    public int ChannelMask = 3;
    public int Writes;
    public int ChannelWrites;
    public int Disposals;
    public int FailChannelWrite;
    public bool LinkedChannels;
    public bool UnsupportedVolume;
    public bool UnsupportedMute;
    public bool IgnoreWrites;
    public bool Disconnected;
    public Action? ReadingVolume;
    public Action? ChannelWritten;
    public Action? ReadingStereo;
}

internal sealed class FakeEndpointVolume(FakeEndpointState state) : IEndpointVolumeAccess
{
    private void CheckAvailable()
    {
        if (state.Disconnected)
            throw new InvalidOperationException("Dispositivo desconectado.");
    }

    public double VolumeScalar
    {
        get
        {
            state.ReadingVolume?.Invoke();
            CheckAvailable();
            if (state.UnsupportedVolume)
                throw new NotSupportedException("Volume indisponível.");
            return state.Volume;
        }
        set
        {
            CheckAvailable();
            if (state.UnsupportedVolume)
                throw new NotSupportedException("Volume indisponível.");
            state.Writes++;
            if (!state.IgnoreWrites)
                state.Volume = value;
        }
    }

    public bool Muted
    {
        get
        {
            CheckAvailable();
            if (state.UnsupportedMute)
                throw new NotSupportedException("Mute indisponível.");
            return state.Muted;
        }
        set
        {
            CheckAvailable();
            if (state.UnsupportedMute)
                throw new NotSupportedException("Mute indisponível.");
            state.Writes++;
            if (!state.IgnoreWrites)
                state.Muted = value;
        }
    }

    public StereoLevels ReadStereo()
    {
        CheckAvailable();
        state.ReadingStereo?.Invoke();
        if (!StereoLevels.IsConfirmedStereo(state.Channels, state.Channels, state.ChannelMask))
            throw new NotSupportedException("Canais L/R não confirmados.");
        return state.Stereo;
    }

    public void SetChannelLevel(int channel, double level)
    {
        CheckAvailable();
        state.ChannelWrites++;
        if (state.ChannelWrites == state.FailChannelWrite)
            throw new InvalidOperationException("Falha simulada na escrita do canal.");
        state.Writes++;
        state.Stereo = channel == 0 ? state.Stereo with { Left = level } : state.Stereo with { Right = level };
        if (state.LinkedChannels)
            state.Stereo = new StereoLevels(level, level);
        state.ChannelWritten?.Invoke();
    }

    public void Dispose() => state.Disposals++;
}
