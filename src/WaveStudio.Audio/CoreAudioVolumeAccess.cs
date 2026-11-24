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
                throw new InvalidOperationException(StudioStrings.Get("CoreAudioVolumeAccess.ODispositivoEstaIndisponivelConfira"));
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
            throw new NotSupportedException(StudioStrings.Get("CoreAudioVolumeAccess.BalancoIndisponivelOEndpointPrecisa"));
        using var client = device.CreateAudioClient();
        var format = client.MixFormat;
        int? mask = (format as WaveFormatExtensible)?.ChannelMask;
        if (!StereoLevels.IsConfirmedStereo(volume.Channels.Count, format.Channels, mask))
            throw new NotSupportedException(StudioStrings.Get("CoreAudioVolumeAccess.BalancoIndisponivelOWindowsNao"));

        return new StereoLevels(volume.Channels[0].VolumeLevelScalar, volume.Channels[1].VolumeLevelScalar);
    }

    public void SetChannelLevel(int channel, double level) => volume.Channels[channel].VolumeLevelScalar = (float)level;

    // MMDevice é responsável por AudioEndpointVolume. Seu Dispose também cancela o registro do callback nativo.
    public void Dispose() => device.Dispose();
}
