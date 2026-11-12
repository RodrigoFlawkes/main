namespace WaveStudio.Audio;

public sealed record PeakReading(double? Scalar, string? Error)
{
    public static readonly PeakReading Unavailable = new(null, "Pico indisponível");
    public double? Decibels => Scalar is > 0 ? 20 * Math.Log10(Scalar.Value) : null;
    public static PeakReading FromScalar(double value) => double.IsFinite(value) && value >= 0 && value <= 1
        ? new(value, null) : new(null, "O Windows retornou um pico inválido.");
}

public sealed record AudioWatchSelection(string? PlaybackId, string? CaptureId, long Revision);
public sealed record AudioWatchSnapshot(AudioWatchSelection Selection, PeakReading Playback, PeakReading Capture,
    string NotificationStatus, bool RefreshRequested, double QueryMilliseconds);

public interface IAudioPeakSource : IDisposable
{
    double ReadPeak();
}

public interface IAudioWatchBackend : IDisposable
{
    string? NotificationError { get; }
    IAudioPeakSource OpenPeak(string endpointId);
}

public sealed class DeviceChangeCoalescer
{
    private long? first;
    private long last;
    public void Signal(long now) { first ??= now; last = now; }
    public bool TakeReady(long now)
    {
        if (first is null || now - last < 200 && now - first.Value < 1000) return false;
        first = null;
        return true;
    }
}
