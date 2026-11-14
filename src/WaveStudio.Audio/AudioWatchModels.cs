using System.Diagnostics;

namespace WaveStudio.Audio;

public sealed record PeakReading(double? Scalar, string? Error)
{
    public static readonly PeakReading Unavailable = new(null, "Pico indisponível");
    public static readonly PeakReading Stale = new(null, "Leitura de pico atrasada. Aguarde uma leitura atual ou atualize os dispositivos.");
    public double? Decibels => Scalar is > 0 ? 20 * Math.Log10(Scalar.Value) : null;
    public static PeakReading FromScalar(double value) => double.IsFinite(value) && value >= 0 && value <= 1
        ? new(value, null) : new(null, "O Windows retornou um pico inválido.");
}

public sealed record AudioWatchSelection(string? PlaybackId, string? CaptureId, long Revision);
public sealed record AudioWatchSnapshot(AudioWatchSelection Selection, PeakReading Playback, PeakReading Capture,
    string NotificationStatus, bool RefreshRequested, double QueryMilliseconds, bool ResetControls = false,
    long RecordedAt = 0, bool WorkerFailed = false)
{
    public AudioWatchSnapshot ForDisplay(long now) => RecordedAt == 0 || Stopwatch.GetElapsedTime(RecordedAt, now) > TimeSpan.FromSeconds(2)
        ? this with { Playback = PeakReading.Stale, Capture = PeakReading.Stale,
            NotificationStatus = WorkerFailed ? NotificationStatus : "Leitura de áudio atrasada. Use Atualizar dispositivos para recuperar." }
        : this;
}

public interface IAudioPeakSource : IDisposable
{
    double ReadPeak();
}

public interface IAudioWatchBackend : IDisposable
{
    string? NotificationError { get; }
    long RecoveryRevision => 0;
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
