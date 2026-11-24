using System.Diagnostics;

namespace WaveStudio.Audio;

/// <summary>Motor com um único proprietário. Os callbacks nativos apenas acionam um sinal atômico.</summary>
public sealed class AudioWatchEngine : IDisposable
{
    private sealed class MeterSlot
    {
        public string? Id;
        public IAudioPeakSource? Source;
        public PeakReading Last = PeakReading.Unavailable;
        public long RetryAt;
    }

    private readonly Func<Action, IAudioWatchBackend> factory;
    private readonly DeviceChangeCoalescer changes = new();
    private readonly MeterSlot playback = new();
    private readonly MeterSlot capture = new();
    private IAudioWatchBackend? backend;
    private int signaled;
    private long retryAt;
    private string? backendError;
    private bool disposed;
    private long recoveryRevision;
    public string? ShutdownError { get; private set; }

    public AudioWatchEngine(Func<Action, IAudioWatchBackend> factory) => this.factory = factory;
    public void Signal() => Interlocked.Exchange(ref signaled, 1);

    public AudioWatchSnapshot Tick(long now, AudioWatchSelection selection, bool restart = false, bool metering = true) =>
        Tick(now, selection, restart, metering, captureMetering: true);

    internal AudioWatchSnapshot Tick(long now, AudioWatchSelection selection, bool restart, bool metering, bool captureMetering)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        long started = Stopwatch.GetTimestamp();
        bool refresh = restart;
        bool resetControls = restart;
        if (restart) Reset();
        if ((backend is null || backend.NotificationError is not null) && now >= retryAt)
        {
            Reset();
            try { backend = factory(Signal); backendError = backend.NotificationError; }
            catch (Exception error) { backendError = EndpointVolumeOperations.Describe(error); }
            retryAt = now + 5000;
            refresh = true;
        }
        if (Interlocked.Exchange(ref signaled, 0) != 0) changes.Signal(now);
        if (changes.TakeReady(now))
        {
            long currentRevision = backend?.RecoveryRevision ?? 0;
            resetControls |= currentRevision != recoveryRevision;
            recoveryRevision = currentRevision;
            CloseSlot(playback);
            CloseSlot(capture);
            refresh = true;
        }
        var left = Read(playback, selection.PlaybackId, now, metering);
        var right = Read(capture, selection.CaptureId, now, metering && captureMetering);
        string status = backend is not null && backendError is null ? StudioStrings.Get("AudioWatchEngine.DeteccaoAutomaticaDeDispositivosAtiva")
            : StudioStrings.Format("AudioWatchEngine.DeteccaoAutomaticaIndisponivelNovaConsulta", backendError);
        return new AudioWatchSnapshot(selection, left, right, status, refresh, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            resetControls, Stopwatch.GetTimestamp());
    }

    private PeakReading Read(MeterSlot slot, string? id, long now, bool metering)
    {
        if (slot.Id != id)
        {
            CloseSlot(slot);
            slot.Id = id;
        }
        if (id is null || backend is null || !metering) return PeakReading.Unavailable;
        if (slot.Source is null && now < slot.RetryAt) return slot.Last;
        try
        {
            slot.Source ??= backend.OpenPeak(id);
            slot.Last = PeakReading.FromScalar(slot.Source.ReadPeak());
        }
        catch (Exception error)
        {
            CloseSlot(slot);
            slot.Last = new PeakReading(null, EndpointVolumeOperations.Describe(error));
            slot.RetryAt = now + 5000;
        }
        return slot.Last;
    }

    private void CloseSlot(MeterSlot slot)
    {
        try { slot.Source?.Dispose(); }
        catch (Exception error) { ShutdownError = EndpointVolumeOperations.Describe(error); }
        slot.Source = null;
        slot.RetryAt = 0;
        slot.Last = PeakReading.Unavailable;
    }

    private void Reset()
    {
        CloseSlot(playback);
        CloseSlot(capture);
        try { backend?.Dispose(); }
        catch (Exception error) { ShutdownError = EndpointVolumeOperations.Describe(error); }
        backend = null;
        recoveryRevision = 0;
        retryAt = 0;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Reset();
    }
}
