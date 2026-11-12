using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WaveStudio.Audio;

public sealed class CoreAudioVolumeAccess : IEndpointVolumeAccess
{
    private readonly MMDevice device;
    private readonly AudioEndpointVolume volume;

    public CoreAudioVolumeAccess(string deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        device = enumerator.GetDevice(deviceId);
        try
        {
            if (device.State != DeviceState.Active)
                throw new InvalidOperationException("O dispositivo está indisponível. Confira a conexão e atualize a lista.");
            volume = device.AudioEndpointVolume;
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    public double VolumeScalar
    {
        get => volume.MasterVolumeLevelScalar;
        set => volume.MasterVolumeLevelScalar = (float)value;
    }

    public bool Muted
    {
        get => volume.Mute;
        set => volume.Mute = value;
    }

    public StereoLevels ReadStereo()
    {
        if (volume.Channels.Count != 2)
            throw new NotSupportedException("Balanço indisponível — o endpoint precisa expor dois canais de volume independentes.");
        using var client = device.CreateAudioClient();
        var format = client.MixFormat;
        int? mask = (format as WaveFormatExtensible)?.ChannelMask;
        if (!StereoLevels.IsConfirmedStereo(volume.Channels.Count, format.Channels, mask))
            throw new NotSupportedException("Balanço indisponível — o Windows não confirmou o mapeamento frontal esquerdo/direito.");

        return new StereoLevels(volume.Channels[0].VolumeLevelScalar, volume.Channels[1].VolumeLevelScalar);
    }

    public void SetChannelLevel(int channel, double level) => volume.Channels[channel].VolumeLevelScalar = (float)level;

    // MMDevice owns AudioEndpointVolume. Its Dispose unregisters the native callback as well.
    public void Dispose() => device.Dispose();
}
