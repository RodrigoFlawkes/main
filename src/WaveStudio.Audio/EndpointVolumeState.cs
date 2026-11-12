namespace WaveStudio.Audio;

public sealed record StereoLevels(double Left, double Right)
{
    public static bool IsConfirmedStereo(int endpointChannels, int mixChannels, int? channelMask) =>
        endpointChannels == 2 && mixChannels == 2 && channelMask == 3;

    public double BaseLevel => Math.Max(Left, Right);
    public double Balance => BaseLevel == 0 ? 0 : (Right - Left) / BaseLevel * 100;

    public StereoLevels WithBalance(double position)
    {
        ValidateLevel(Left);
        ValidateLevel(Right);
        if (!double.IsFinite(position) || position < -100 || position > 100)
            throw new ArgumentOutOfRangeException(nameof(position));
        return new StereoLevels(BaseLevel * (1 - Math.Max(0, position) / 100),
            BaseLevel * (1 + Math.Min(0, position) / 100));
    }

    public static void ValidateLevel(double level)
    {
        if (!double.IsFinite(level) || level < 0 || level > 1)
            throw new ArgumentOutOfRangeException(nameof(level));
    }
}

public sealed record EndpointVolumeState(double? VolumePercent, bool? Muted, StereoLevels? Stereo,
    string? VolumeError, string? MuteError, string? BalanceError);

public sealed record EndpointSelection(string? DeviceId, long Revision);

public abstract record EndpointCommand;
public sealed record SetEndpointVolume(double Percent) : EndpointCommand;
public sealed record SetEndpointMute(bool Muted) : EndpointCommand;
public sealed record SetEndpointBalance(double Position) : EndpointCommand;

/// <summary>All access must occur on the worker that opened the endpoint.</summary>
public interface IEndpointVolumeAccess : IDisposable
{
    double VolumeScalar { get; set; }
    bool Muted { get; set; }
    StereoLevels ReadStereo();
    void SetChannelLevel(int channel, double level);
}
