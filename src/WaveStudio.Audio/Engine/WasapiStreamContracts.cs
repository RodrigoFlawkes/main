using System.Diagnostics;

namespace WaveStudio.Audio.Engine;

// Fakeable native-call seam; each object is acquired, used and released by one stable MTA owner.
internal interface IWasapiStreamClient : IWasapiPreparationClient
{
    int Start();
    int Stop();
    void RevalidateFormat(WasapiPcm expected);
    uint ReadPadding();
    IWasapiCapturePackets OpenCapture();
    IWasapiRenderPackets OpenRender();
}
internal readonly record struct WasapiCaptureBuffer(int HResult, nint Data, uint Frames, uint Flags, ulong Position, ulong Qpc100ns);
internal interface IWasapiCapturePackets : IDisposable
{
    uint NextPacketSize();
    WasapiCaptureBuffer GetBuffer();
    int ReleaseBuffer(uint framesRead);
}
internal interface IWasapiRenderPackets : IDisposable
{
    nint GetBuffer(uint frames);
    int ReleaseBuffer(uint framesWritten, uint flags);
}
internal static class WasapiTime
{
    // Windows QPC converted to 100ns for age/control only; never an audio position substitute.
    public static long Now100ns() => checked((long)((UInt128)(ulong)Stopwatch.GetTimestamp() * 10000000 / (ulong)Stopwatch.Frequency));
}

internal sealed class WasapiRunningClock(TransportEndpoint endpoint, int rate, long started100ns)
{
    private readonly WasapiClockAdapter adapter = new(endpoint, rate);
    private ulong position;
    private bool anchored;
    public long MovementSamples { get; private set; }
    public NativeClockResult Observe(WasapiClockReading reading, long now100ns)
    {
        if (reading.Quality != NativeClockQuality.Accurate)
            return adapter.Convert(endpoint, reading, now100ns, out _);
        if (!anchored && (reading.Position == 0 || reading.Qpc100ns == 0))
            return now100ns - started100ns <= 5000000 ? NativeClockResult.Stalled : NativeClockResult.InvalidPosition;
        var result = adapter.Convert(endpoint, reading, now100ns, out _);
        if (result != NativeClockResult.Valid) return result;
        if (anchored && reading.Position > position) MovementSamples++;
        anchored = true; position = reading.Position;
        return NativeClockResult.Valid;
    }
}

internal sealed record WasapiRoleSnapshot(bool Initialized, bool StartAttempted, bool Started, bool StopConfirmed,
    bool Released, int OwnerThreadId, uint CapacityFrames, long DefaultPeriod100ns, long Latency100ns,
    long PacketsAcquired, long PacketsReleased, long FramesAcquired, long FramesReleased, long PrimedFrames,
    uint Padding, long MovementSamples, WasapiClockReading? LastClock, uint PacketFlags,
    ulong PacketPosition, ulong PacketQpc100ns, bool BufferInFlight, string? Error, string? CleanupError,
    int? StartHResult, int? StopHResult, int? AcquireHResult, int? ReleaseHResult, NativeClockResult? ClockStatus);

internal sealed record WasapiSilentSnapshot(WasapiRoleSnapshot Capture, WasapiRoleSnapshot Playback,
    bool Completed, bool ResourcesRetained, string? Failure, int? HResult,
    int FifoCapacityFrames, long ProducedFrames, long ReadFrames, long DroppedFrames, long UnderrunFrames, long Observation100ns, bool DurationReached)
{
    public bool AlwaysSilent => true;
    public bool DriftControlEnabled => false;
    public bool MovementProven => Completed && Failure is null && Capture.MovementSamples >= 2 && Playback.MovementSamples >= 2;
    public bool TransportObserved => DurationReached && MovementProven && Capture.Started && Playback.Started &&
        Capture.StopConfirmed && Playback.StopConfirmed && Capture.Released && Playback.Released &&
        Capture.FramesReleased > 0 && Playback.PrimedFrames == Playback.CapacityFrames &&
        Playback.FramesReleased > Playback.PrimedFrames &&
        Capture.PacketsAcquired == Capture.PacketsReleased && Playback.PacketsAcquired == Playback.PacketsReleased &&
        Capture.FramesAcquired == Capture.FramesReleased && Playback.FramesAcquired == Playback.FramesReleased;
}
