using NAudio.Wave;

namespace WaveStudio.Audio;

public enum MicrophoneTestState { Idle, Recording, Stopping, Ready, Playing, Error, Disposed }

public sealed record MicrophoneTestSnapshot(MicrophoneTestState State, TimeSpan RecordedDuration,
    TimeSpan Elapsed, string Message, bool CanRecord, bool CanPlay, bool CanClear,
    int? RecordedSampleRate = null, bool EqualizerSupported = false, string? PlaybackMode = null,
    long LimitedSamples = 0, long SanitizedSamples = 0, bool ResourcesRetained = false,
    bool ShutdownPending = false, string? PrimaryError = null, string? CleanupError = null);

internal delegate void TestAudioData(ReadOnlySpan<byte> data);

internal sealed class TestEndpointUnavailableException() : InvalidOperationException(
    StudioStrings.Get("MicrophoneTestModels.ODispositivoSelecionadoEstaIndisponivel"));

internal interface ITestCapture : IDisposable
{
    WaveFormat Format { get; }
    bool Completed { get; }
    Exception? Error { get; }
    void Start(TestAudioData receive);
    void RequestStop();
}

internal interface ITestPlayback : IDisposable
{
    bool Completed { get; }
    Exception? Error { get; }
    void Start();
    void RequestStop();
}

internal interface IMicrophoneTestBackend
{
    ITestCapture OpenCapture(string deviceId);
    ITestPlayback OpenPlayback(string deviceId, IWaveProvider source);
}
