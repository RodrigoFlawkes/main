using System.Buffers.Binary;
using System.Collections.Concurrent;
using NAudio.Wave;
using WaveStudio.Audio;
using Xunit;

namespace WaveStudio.Tests;

// Todos os objetos de captura e reprodução abaixo são simulados. Estes testes nunca abrem o microfone real.
public sealed class MicrophoneTestTests
{
    internal sealed class FakeCapture : ITestCapture
    {
        public WaveFormat Format { get; set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        private bool completed;
        public int CompletionReadFailures;
        public bool Completed
        {
            get { if (Interlocked.Exchange(ref CompletionReadFailures, 0) > 0) throw new InvalidOperationException("falha de Tick simulada"); return Volatile.Read(ref completed); }
            set => Volatile.Write(ref completed, value);
        }
        public Exception? Error { get; set; }
        public Exception? StartError { get; set; }
        public Exception? StopError { get; set; }
        public Exception? DisposeError { get; set; }
        public Action? BeforeStop { get; set; }
        public bool FinishOnStop { get; set; } = true;
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public int Disposals { get; private set; }
        public TestAudioData? Receive { get; private set; }
        public void Start(TestAudioData receive)
        {
            if (StartError is not null) throw StartError;
            Receive = receive;
            Starts++;
        }
        public void Emit(int bytes) => Receive?.Invoke(new byte[bytes]);
        public void RequestStop() { Stops++; BeforeStop?.Invoke(); if (StopError is not null) throw StopError; if (FinishOnStop) Completed = true; }
        public void Dispose() { Disposals++; if (DisposeError is not null) throw DisposeError; }
    }

    internal sealed class FakePlayback : ITestPlayback
    {
        private bool completed;
        public int CompletionReadFailures;
        public bool Completed
        {
            get { if (Interlocked.Exchange(ref CompletionReadFailures, 0) > 0) throw new InvalidOperationException("falha de Tick simulada"); return Volatile.Read(ref completed); }
            set => Volatile.Write(ref completed, value);
        }
        public Exception? Error { get; set; }
        public Exception? StopError { get; set; }
        public Exception? DisposeError { get; set; }
        public bool FinishOnStop { get; set; } = true;
        public int Starts { get; private set; }
        public int Disposals { get; private set; }
        public void Start() { Completed = false; Starts++; }
        public void RequestStop() { if (StopError is not null) throw StopError; if (FinishOnStop) Completed = true; }
        public void Dispose() { Disposals++; if (DisposeError is not null) throw DisposeError; }
    }

    internal sealed class FakeBackend : IMicrophoneTestBackend
    {
        public FakeCapture Capture { get; } = new();
        public FakePlayback Playback { get; } = new();
        public ConcurrentQueue<(string Operation, string Id, int Thread)> Calls { get; } = new();
        public IWaveProvider? Source { get; private set; }
        public Action? BeforeCaptureOpen { get; set; }
        public Action? BeforePlaybackOpen { get; set; }
        public Exception? CaptureError { get; set; }
        public Exception? PlaybackError { get; set; }
        public ITestCapture OpenCapture(string deviceId)
        {
            Calls.Enqueue(("capture", deviceId, Environment.CurrentManagedThreadId));
            BeforeCaptureOpen?.Invoke();
            if (CaptureError is not null) throw CaptureError;
            return Capture;
        }
        public ITestPlayback OpenPlayback(string deviceId, IWaveProvider source)
        {
            Calls.Enqueue(("playback", deviceId, Environment.CurrentManagedThreadId));
            Source = source;
            BeforePlaybackOpen?.Invoke();
            if (PlaybackError is not null) throw PlaybackError;
            return Playback;
        }
    }

    private sealed class Fixture
    {
        public FakeBackend Backend { get; } = new();
        public long Revision { get; set; } = 1;
        public MicrophoneTestEngine Engine { get; }
        public Fixture()
        {
            Engine = new(Backend, value => value == Revision);
            Engine.Select("input", "output", Revision);
        }
        public void Ready()
        {
            Engine.Record(Revision, TimeSpan.Zero);
            Backend.Capture.Emit(19200);
            Engine.Stop();
            Engine.Tick(TimeSpan.FromMilliseconds(100));
        }
        public void Close()
        {
            Engine.Clear();
            Backend.Capture.Completed = Backend.Playback.Completed = true;
            Engine.Tick(TimeSpan.Zero);
            Engine.MarkDisposed();
        }
    }

    [Fact]
    public void ConstructionSelectionAndTickDoNotStartStreams()
    {
        var f = new Fixture();
        f.Engine.Tick(TimeSpan.FromSeconds(20));
        Assert.Empty(f.Backend.Calls);
        Assert.True(f.Engine.Snapshot.CanRecord);
        Assert.False(f.Engine.Snapshot.CanPlay);
        f.Close();
    }

    [Fact]
    public void RecordingStopsReadyWithoutAutomaticPlaybackAndCanBeCleared()
    {
        var f = new Fixture();
        f.Ready();
        Assert.Equal(MicrophoneTestState.Ready, f.Engine.State);
        Assert.Equal(TimeSpan.FromMilliseconds(100), f.Engine.Snapshot.RecordedDuration);
        Assert.Equal(0, f.Backend.Playback.Starts);
        Assert.Equal(1, f.Backend.Capture.Disposals);
        f.Engine.Clear();
        Assert.Equal(TimeSpan.Zero, f.Engine.Snapshot.RecordedDuration);
        Assert.False(f.Engine.Snapshot.CanPlay);
        f.Close();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TenSecondWallClockOrBufferLimitStopsCapture(bool byteLimit)
    {
        var f = new Fixture();
        f.Engine.Record(1, TimeSpan.Zero);
        f.Backend.Capture.Emit(byteLimit ? 2_100_000 : 19200);
        f.Engine.Tick(TimeSpan.FromSeconds(byteLimit ? 1 : 10));
        Assert.Equal(MicrophoneTestState.Ready, f.Engine.State);
        Assert.InRange(f.Engine.Snapshot.RecordedDuration.TotalSeconds, 0, 10);
        Assert.True(f.Backend.Capture.Stops > 0);
        Assert.Equal(0, f.Backend.Playback.Starts);
        f.Close();
    }

    [Fact]
    public void EmptyCaptureDoesNotEnablePlayback()
    {
        var f = new Fixture();
        f.Engine.Record(1, TimeSpan.Zero);
        f.Engine.Stop();
        f.Engine.Tick(TimeSpan.FromSeconds(1));
        Assert.False(f.Engine.Snapshot.CanPlay);
        Assert.Contains("Nenhum áudio", f.Engine.Snapshot.Message);
        f.Close();
    }

    [Fact]
    public void SimultaneousRecordAndPlaybackAreRejectedAndTargetsAreExact()
    {
        var f = new Fixture();
        f.Engine.Record(1, TimeSpan.Zero);
        f.Engine.Record(1, TimeSpan.Zero);
        f.Engine.Play(1, .2f, TimeSpan.Zero);
        Assert.Single(f.Backend.Calls);
        f.Backend.Capture.Emit(400);
        f.Engine.Stop();
        f.Engine.Tick(TimeSpan.FromSeconds(1));
        f.Engine.Play(1, .2f, TimeSpan.FromSeconds(1));
        f.Engine.Record(1, TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { "input", "output" }, f.Backend.Calls.Select(x => x.Id));
        Assert.Equal(1, f.Backend.Playback.Starts);
        f.Close();
    }

    [Fact]
    public void StaleCommandsAndCallbacksCannotCaptureOrPlayAnotherTarget()
    {
        var f = new Fixture();
        f.Engine.Record(1, TimeSpan.Zero);
        var oldCallback = f.Backend.Capture.Receive!;
        f.Revision = 2;
        f.Engine.Select("other-input", "other-output", 2);
        oldCallback(new byte[19200]);
        f.Engine.Tick(TimeSpan.FromSeconds(1));
        f.Engine.Record(1, TimeSpan.Zero);
        f.Engine.Play(1, .2f, TimeSpan.Zero);
        Assert.Single(f.Backend.Calls);
        Assert.Equal(TimeSpan.Zero, f.Engine.Snapshot.RecordedDuration);
        f.Close();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TargetChangedDuringOpenNeverStartsTheOpenedStream(bool playback)
    {
        var f = new Fixture();
        if (playback)
        {
            f.Ready();
            f.Backend.BeforePlaybackOpen = () => f.Revision++;
            f.Engine.Play(1, .2f, TimeSpan.Zero);
            Assert.Equal(0, f.Backend.Playback.Starts);
            Assert.Equal(1, f.Backend.Playback.Disposals);
        }
        else
        {
            f.Backend.BeforeCaptureOpen = () => f.Revision++;
            f.Engine.Record(1, TimeSpan.Zero);
            Assert.Equal(0, f.Backend.Capture.Starts);
            Assert.Equal(1, f.Backend.Capture.Disposals);
        }
        f.Close();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisconnectSuspendOrCloseStopsAndDiscardsEvenWithSameIds(bool playback)
    {
        var f = new Fixture();
        if (playback) { f.Ready(); f.Engine.Play(1, .2f, TimeSpan.Zero); }
        else { f.Engine.Record(1, TimeSpan.Zero); f.Backend.Capture.Emit(400); }
        f.Revision++;
        f.Engine.Select("input", "output", f.Revision);
        f.Engine.Tick(TimeSpan.FromSeconds(1));
        Assert.False(f.Engine.Busy);
        Assert.False(f.Engine.Snapshot.CanPlay);
        Assert.Equal(TimeSpan.Zero, f.Engine.Snapshot.RecordedDuration);
        f.Close();
    }

    [Fact]
    public void PendingStopRejectsNewCommandsAndLateDataUntilNativeCompletion()
    {
        var f = new Fixture();
        f.Backend.Capture.FinishOnStop = false;
        f.Engine.Record(1, TimeSpan.Zero);
        f.Backend.Capture.Emit(400);
        f.Engine.Stop();
        f.Backend.Capture.Emit(400);
        f.Engine.Record(1, TimeSpan.Zero);
        f.Engine.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(MicrophoneTestState.Stopping, f.Engine.State);
        Assert.Equal(1, f.Backend.Capture.Starts);
        Assert.Equal(0, f.Backend.Capture.Disposals);
        f.Backend.Capture.Completed = true;
        f.Engine.Tick(TimeSpan.FromSeconds(2));
        Assert.Equal(400d / 192000, f.Engine.Snapshot.RecordedDuration.TotalSeconds, 6);
        f.Close();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StreamFailureReleasesResourcesAndDoesNotAutoretry(bool duringStart)
    {
        var f = new Fixture();
        var error = new UnauthorizedAccessException();
        if (duringStart) f.Backend.Capture.StartError = error;
        f.Engine.Record(1, TimeSpan.Zero);
        if (!duringStart)
        {
            f.Backend.Capture.Error = error;
            f.Backend.Capture.Completed = true;
            f.Engine.Tick(TimeSpan.FromSeconds(1));
        }
        Assert.Equal(MicrophoneTestState.Error, f.Engine.State);
        Assert.Contains("Privacidade", f.Engine.Snapshot.Message);
        Assert.Equal(1, f.Backend.Capture.Disposals);
        Assert.False(f.Engine.Snapshot.CanPlay);
        Assert.Equal(0, f.Backend.Playback.Starts);
        f.Close();
    }

    [Fact]
    public void UnavailableMutedOrUnsupportedOutputPreservesClipForExplicitRetry()
    {
        var f = new Fixture();
        f.Ready();
        f.Backend.PlaybackError = new InvalidOperationException("Saída em mute.");
        f.Engine.Play(1, .2f, TimeSpan.Zero);
        Assert.Equal(MicrophoneTestState.Error, f.Engine.State);
        Assert.True(f.Engine.Snapshot.CanPlay);
        Assert.Contains("mute", f.Engine.Snapshot.Message);
        f.Backend.PlaybackError = null;
        f.Engine.Play(1, .2f, TimeSpan.Zero);
        f.Engine.Stop();
        f.Engine.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(MicrophoneTestState.Ready, f.Engine.State);
        f.Close();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlaybackCompletionOrWatchdogReturnsReadyWithoutRestart(bool watchdog)
    {
        var f = new Fixture();
        f.Ready();
        f.Engine.Play(1, .2f, TimeSpan.Zero);
        f.Backend.Playback.Completed = !watchdog;
        f.Engine.Tick(TimeSpan.FromSeconds(watchdog ? 12 : 1));
        Assert.Equal(MicrophoneTestState.Ready, f.Engine.State);
        Assert.Equal(1, f.Backend.Playback.Starts);
        Assert.Equal(1, f.Backend.Playback.Disposals);
        f.Close();
    }

    [Fact]
    public void UnsupportedCaptureFormatReleasesEndpointBeforeAnyCapture()
    {
        var f = new Fixture();
        f.Backend.Capture.Format = new WaveFormat(48000, 24, 1);
        f.Engine.Record(1, TimeSpan.Zero);
        Assert.Equal(MicrophoneTestState.Error, f.Engine.State);
        Assert.Contains("Formato", f.Engine.Snapshot.Message);
        Assert.Equal(0, f.Backend.Capture.Starts);
        Assert.Equal(1, f.Backend.Capture.Disposals);
        f.Close();
    }

    [Fact]
    public void PlaybackStreamFaultDiscardsClipWithoutDependingOnCatalogNotifications()
    {
        var f = new Fixture();
        f.Ready();
        f.Engine.Play(1, .2f, TimeSpan.Zero);
        f.Backend.Playback.Error = new System.Runtime.InteropServices.COMException("Removed", unchecked((int)0x88890004));
        f.Backend.Playback.Completed = true;
        f.Engine.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(MicrophoneTestState.Error, f.Engine.State);
        Assert.False(f.Engine.Snapshot.CanPlay);
        Assert.Equal(TimeSpan.Zero, f.Engine.Snapshot.RecordedDuration);
        Assert.Equal(1, f.Backend.Playback.Disposals);
        f.Close();
    }

    [Fact]
    public void OutputDisconnectedDuringOpenDiscardsClipWithoutNotification()
    {
        var f = new Fixture();
        f.Ready();
        f.Backend.PlaybackError = new TestEndpointUnavailableException();
        f.Engine.Play(1, .2f, TimeSpan.Zero);
        Assert.Equal(MicrophoneTestState.Error, f.Engine.State);
        Assert.False(f.Engine.Snapshot.CanPlay);
        Assert.Equal(TimeSpan.Zero, f.Engine.Snapshot.RecordedDuration);
        Assert.Equal(0, f.Backend.Playback.Starts);
        f.Close();
    }

    [Fact]
    public void NoAvailableEndpointsMeansNoAutomaticFallback()
    {
        var f = new Fixture();
        f.Engine.Select(null, null, 1);
        f.Engine.Record(1, TimeSpan.Zero);
        f.Engine.Play(1, .2f, TimeSpan.Zero);
        Assert.Empty(f.Backend.Calls);
        Assert.False(f.Engine.Snapshot.CanRecord);
        f.Close();
    }

    [Theory]
    [InlineData(8000, 1, false)]
    [InlineData(44100, 2, false)]
    [InlineData(48000, 1, true)]
    [InlineData(96000, 2, true)]
    public void SupportedFormatsStayWithinTimeAndMemoryLimits(int rate, int channels, bool ieeeFloat)
    {
        using var clip = new BoundedTestAudio(ieeeFloat ? WaveFormat.CreateIeeeFloatWaveFormat(rate, channels) : new WaveFormat(rate, 16, channels));
        clip.Append(new byte[clip.Capacity + 1000]);
        Assert.True(clip.Full);
        Assert.Equal(10, clip.Duration.TotalSeconds);
        Assert.InRange(clip.Capacity, 1, BoundedTestAudio.MaximumBytes);
        Assert.Equal(0, clip.Length % clip.Format.BlockAlign);
    }

    [Theory]
    [InlineData(192000, 32, 1)]
    [InlineData(48000, 24, 1)]
    [InlineData(48000, 16, 6)]
    [InlineData(4000, 16, 1)]
    public void UnsupportedFormatsHaveReadableError(int rate, int bits, int channels)
    {
        var error = Assert.Throws<NotSupportedException>(() => new BoundedTestAudio(new WaveFormat(rate, bits, channels)));
        Assert.Contains("Formato", error.Message);
    }

    [Theory]
    [InlineData(-.1f)]
    [InlineData(1.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void PlaybackLevelRejectsBoostAndInvalidValues(float level)
    {
        using var clip = new BoundedTestAudio(new WaveFormat(48000, 16, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => clip.CreatePlayback(level, () => true));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(.2f)]
    [InlineData(1f)]
    public void DigitalLevelIsConservativeAndNeverExceedsUnity(float level)
    {
        using var clip = new BoundedTestAudio(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1));
        float[] values = [-1, -.5f, 0, .5f, 1, float.NaN, float.PositiveInfinity, 2];
        byte[] data = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i * 4), values[i]);
        clip.Append(data);
        var source = clip.CreatePlayback(level, () => true);
        Assert.Equal(data.Length, source.Read(data.AsSpan()));
        for (int i = 0; i < values.Length; i++)
        {
            float actual = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(i * 4));
            float expected = float.IsFinite(values[i]) ? Math.Clamp(values[i], -1, 1) * level : 0;
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Pcm16AttenuationAndGenerationGateDoNotTouchWindowsControls()
    {
        using var clip = new BoundedTestAudio(new WaveFormat(48000, 16, 1));
        byte[] data = [0, 128, 255, 127];
        clip.Append(data);
        bool current = true;
        var source = clip.CreatePlayback(.2f, () => current);
        Assert.Equal(4, source.Read(data.AsSpan()));
        Assert.Equal((short)(short.MinValue * .2f), BinaryPrimitives.ReadInt16LittleEndian(data));
        current = false;
        Assert.Equal(0, clip.CreatePlayback(1, () => current).Read(data.AsSpan()));
        clip.Dispose();
        Assert.Equal(0, clip.Length);
        Assert.Equal(0, source.Read(data.AsSpan()));
    }

    [Fact]
    public async Task WorkerInvalidatesBlockedOpenAndOwnsAllNativeCommands()
    {
        var backend = new FakeBackend();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        backend.BeforeCaptureOpen = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)) throw new TimeoutException(); };
        await using var session = new MicrophoneTestSession(backend);
        await session.SelectAsync("input", "output");
        var record = session.RecordAsync();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        var changed = session.SelectAsync("new-input", "new-output");
        release.Set();
        await record;
        await changed;
        Assert.Equal(0, backend.Capture.Starts);
        Assert.Single(backend.Calls);
        Assert.NotEqual(Environment.CurrentManagedThreadId, backend.Calls.Single().Thread);
        Assert.False(session.Latest.CanPlay);
    }

    [Fact]
    public async Task WorkerCloseIsIdempotentAndReleasesActiveCapture()
    {
        var backend = new FakeBackend();
        var session = new MicrophoneTestSession(backend);
        await session.SelectAsync("input", "output");
        await session.RecordAsync();
        await session.DisposeAsync();
        await session.DisposeAsync();
        await session.RecordAsync();
        Assert.Equal(1, backend.Capture.Disposals);
        Assert.Equal(1, backend.Capture.Starts);
        Assert.Equal(MicrophoneTestState.Disposed, session.Latest.State);
        Assert.Equal(TimeSpan.Zero, session.Latest.RecordedDuration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopOrClearCancelsBlockedOpenBeforeItCanStart(bool clear)
    {
        var backend = new FakeBackend();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var token = TestContext.Current.CancellationToken;
        backend.BeforeCaptureOpen = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5), token)) throw new TimeoutException(); };
        await using var session = new MicrophoneTestSession(backend);
        await session.SelectAsync("input", "output");
        var record = session.RecordAsync();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5), token));
        var cancel = clear ? session.ClearAsync() : session.StopAsync();
        release.Set();
        await record;
        await cancel;
        Assert.Equal(0, backend.Capture.Starts);
        Assert.Equal(1, backend.Capture.Disposals);
        Assert.False(session.Latest.CanPlay);
    }
}
