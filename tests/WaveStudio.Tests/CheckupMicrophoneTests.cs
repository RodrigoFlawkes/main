using WaveStudio.Audio;
using WaveStudio.Audio.Dsp;
using Xunit;

namespace WaveStudio.Tests;

public sealed class CheckupMicrophoneTests
{
    [Theory]
    [InlineData(false, "clear")] [InlineData(true, "clear")]
    [InlineData(false, "select")] [InlineData(true, "select")]
    [InlineData(false, "invalidate")] [InlineData(true, "invalidate")]
    [InlineData(false, "close")] [InlineData(true, "close")]
    public void CompletionErrorStillReleasesExactlyOnceWhenDiscardIsPending(bool playing, string action)
    {
        var backend = new MicrophoneTestTests.FakeBackend(); long revision = 1;
        var engine = new MicrophoneTestEngine(backend, r => r == revision);
        engine.Select("input", "output", revision); engine.Record(revision, TimeSpan.Zero); backend.Capture.Emit(400);
        if (playing) { engine.Stop(); engine.Tick(TimeSpan.Zero); engine.Play(revision, .2f, TimeSpan.Zero); }
        if (playing) backend.Playback.Error = new InvalidOperationException("erro primário de reprodução");
        else backend.Capture.Error = new InvalidOperationException("erro primário de captura");
        if (action == "select") engine.Select("new-input", "output", ++revision);
        else if (action == "invalidate") revision++;
        else engine.Clear(action == "close" ? "Encerrando" : "Limpando");
        engine.Tick(TimeSpan.FromSeconds(1)); engine.Tick(TimeSpan.FromSeconds(2));
        Assert.False(engine.Busy);
        Assert.Equal(1, backend.Capture.Disposals); Assert.Equal(playing ? 1 : 0, backend.Playback.Disposals);
        Assert.Equal(TimeSpan.Zero, engine.Snapshot.RecordedDuration); Assert.False(engine.Snapshot.CanPlay);
        Assert.Contains("erro primário", engine.Snapshot.Message);
        engine.MarkDisposed();
    }
    [Fact]
    public void PlaybackCanceledAfterOpenClearsProviderAndModeButPreservesOriginal()
    {
        var backend = new MicrophoneTestTests.FakeBackend(); bool requested = true;
        var engine = new MicrophoneTestEngine(backend, _ => true);
        engine.Select("input", "output", 1); engine.Record(1, TimeSpan.Zero); backend.Capture.Emit(400); engine.Stop(); engine.Tick(TimeSpan.Zero);
        var duration = engine.Snapshot.RecordedDuration;
        backend.BeforePlaybackOpen = () => requested = false;
        engine.Play(1, .2f, TimeSpan.Zero, () => requested, new GraphicEqualizerSettings(true, new double[10]));
        Assert.False(engine.Busy); Assert.Equal(0, backend.Playback.Starts); Assert.Equal(1, backend.Playback.Disposals);
        Assert.Null(engine.Snapshot.PlaybackMode); Assert.Equal(0, engine.Snapshot.LimitedSamples);
        Assert.Equal(duration, engine.Snapshot.RecordedDuration); Assert.True(engine.Snapshot.CanPlay);
        engine.Clear(); engine.MarkDisposed();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PrimaryAndDisposeErrorsAreBothReportedWithoutDoubleRelease(bool playing)
    {
        var backend = new MicrophoneTestTests.FakeBackend(); var engine = new MicrophoneTestEngine(backend, _ => true);
        engine.Select("input", "output", 1); engine.Record(1, TimeSpan.Zero); backend.Capture.Emit(400);
        if (playing) { engine.Stop(); engine.Tick(TimeSpan.Zero); engine.Play(1, .2f, TimeSpan.Zero); backend.Playback.Error = new InvalidOperationException("primário"); backend.Playback.DisposeError = new InvalidOperationException("limpeza"); }
        else { backend.Capture.Error = new InvalidOperationException("primário"); backend.Capture.DisposeError = new InvalidOperationException("limpeza"); }
        engine.Clear(); engine.Tick(TimeSpan.Zero); engine.Tick(TimeSpan.Zero);
        Assert.False(engine.Busy); Assert.Contains("primário", engine.Snapshot.PrimaryError); Assert.Contains("limpeza", engine.Snapshot.CleanupError);
        Assert.Equal(1, backend.Capture.Disposals); Assert.Equal(playing ? 1 : 0, backend.Playback.Disposals); engine.MarkDisposed();
    }
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task WorkerRetainsActiveResourceAfterStopOrTickFailureAndCanCloseLater(bool playing, bool tickFailure)
    {
        var backend = new MicrophoneTestTests.FakeBackend();
        var session = new MicrophoneTestSession(backend, TimeSpan.FromMilliseconds(60));
        try
        {
            await session.SelectAsync("input", "output"); await session.RecordAsync(); backend.Capture.Emit(400);
            if (playing) { await session.StopAsync(); await Until(() => session.Latest.CanPlay); await session.PlayAsync(.2f); }
            if (playing) { backend.Playback.FinishOnStop = false; if (tickFailure) Interlocked.Exchange(ref backend.Playback.CompletionReadFailures, 1); else backend.Playback.StopError = new InvalidOperationException("falha de parada"); }
            else { backend.Capture.FinishOnStop = false; if (tickFailure) Interlocked.Exchange(ref backend.Capture.CompletionReadFailures, 1); else backend.Capture.StopError = new InvalidOperationException("falha de parada"); }
            if (!tickFailure) await session.StopAsync();
            await Until(() => session.Latest.ResourcesRetained && session.Latest.PrimaryError is not null);
            Assert.Equal(playing ? 1 : 0, backend.Capture.Disposals); Assert.Equal(0, backend.Playback.Disposals);
            if (playing) Assert.Equal(0, backend.Source!.Read(new byte[128]));
            await Assert.ThrowsAsync<TimeoutException>(() => session.DisposeAsync().AsTask());
            Assert.True(session.Latest.ShutdownPending); Assert.False(session.Latest.CanRecord);
            await session.RecordAsync(); Assert.Equal(1, backend.Capture.Starts);
        }
        finally
        {
            backend.Capture.StopError = backend.Playback.StopError = null;
            backend.Capture.Completed = backend.Playback.Completed = true;
            await session.DisposeAsync();
        }
        Assert.Equal(MicrophoneTestState.Disposed, session.Latest.State); Assert.False(session.Latest.ResourcesRetained);
        Assert.Equal(1, backend.Capture.Disposals); Assert.Equal(playing ? 1 : 0, backend.Playback.Disposals);
    }
    [Fact]
    public async Task BlockedNativeStopDoesNotPermitForcedDisposalOrConflictingStart()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var backend = new MicrophoneTestTests.FakeBackend(); var session = new MicrophoneTestSession(backend, TimeSpan.FromMilliseconds(60));
        await session.SelectAsync("input", "output"); await session.RecordAsync();
        backend.Capture.BeforeStop = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken)) throw new TimeoutException(); };
        var stopped = session.StopAsync();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<TimeoutException>(() => session.DisposeAsync().AsTask());
            Assert.Equal(0, backend.Capture.Disposals); Assert.True(session.Latest.ResourcesRetained);
            await session.RecordAsync(); Assert.Equal(1, backend.Capture.Starts);
        }
        finally { release.Set(); await stopped; await session.DisposeAsync(); }
        Assert.Equal(1, backend.Capture.Disposals); Assert.Equal(MicrophoneTestState.Disposed, session.Latest.State);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CompletionErrorDuringWorkerCloseDisposesInsteadOfGettingStuck(bool playing)
    {
        var backend = new MicrophoneTestTests.FakeBackend(); var session = new MicrophoneTestSession(backend, TimeSpan.FromSeconds(1));
        await session.SelectAsync("input", "output"); await session.RecordAsync(); backend.Capture.Emit(400);
        if (playing) { await session.StopAsync(); await Until(() => session.Latest.CanPlay); await session.PlayAsync(.2f); backend.Playback.Error = new InvalidOperationException("erro primário"); }
        else backend.Capture.Error = new InvalidOperationException("erro primário");
        await session.DisposeAsync(); Assert.Equal(1, backend.Capture.Disposals);
        Assert.Equal(MicrophoneTestState.Disposed, session.Latest.State); Assert.Contains("erro primário", session.Latest.PrimaryError);
        Assert.Equal(playing ? 1 : 0, backend.Playback.Disposals);
    }
    private static async Task Until(Func<bool> condition)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!condition()) { if (deadline.Elapsed > TimeSpan.FromSeconds(2)) throw new TimeoutException("Worker não publicou o estado esperado."); await Task.Delay(5, TestContext.Current.CancellationToken); }
    }
    [Fact]
    public void ClearCannotClaimMemoryWasErasedBeforeAnActiveStreamCompletes()
    {
        var backend = new MicrophoneTestTests.FakeBackend(); var engine = new MicrophoneTestEngine(backend, _ => true);
        engine.Select("input", "output", 1); engine.Record(1, TimeSpan.Zero); backend.Capture.Emit(400);
        backend.Capture.FinishOnStop = false; engine.Clear();
        Assert.True(engine.Busy); Assert.True(engine.Snapshot.ResourcesRetained); Assert.Equal(0, backend.Capture.Disposals);
        Assert.Contains("pendente", engine.Snapshot.Message); Assert.DoesNotContain("apagado da memória", engine.Snapshot.Message);
        backend.Capture.Completed = true; engine.Tick(TimeSpan.Zero);
        Assert.False(engine.Busy); Assert.Equal(TimeSpan.Zero, engine.Snapshot.RecordedDuration); engine.MarkDisposed();
    }
}
