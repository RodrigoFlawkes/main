using WaveStudio.Audio;
using Xunit;

namespace WaveStudio.Tests;

public sealed class ApplicationSessionTests
{
    private sealed class Session(string endpoint, string instance, uint pid = 42) : IApplicationSessionAccess
    {
        public SessionKey Key { get; set; } = new(endpoint, instance);
        public SessionActivity Activity { get; set; } = SessionActivity.Active;
        public double VolumeScalar { get; set; } = 0.5;
        public bool Muted { get; set; }
        public int Writes;
        public int Disposals;
        public bool Denied;
        public bool System;
        public bool FailRead;
        public Func<SessionKey>? KeyReader;
        public ApplicationSessionSnapshot Read()
        {
            if (FailRead) throw new UnauthorizedAccessException();
            return new(Key, System ? "Sons do sistema" : "Aplicativo não identificado", pid,
                System, Activity, VolumeScalar * 100, Muted, null);
        }
        public void Dispose() => Disposals++;
        public void WriteVolume(double value) { if (Denied) throw new UnauthorizedAccessException(); Writes++; VolumeScalar = value; }
        public void WriteMute(bool value) { if (Denied) throw new UnauthorizedAccessException(); Writes++; Muted = value; }
    }

    private sealed class BorrowedSession(Session state) : IApplicationSessionAccess
    {
        public SessionKey Key => state.KeyReader?.Invoke() ?? state.Key;
        public SessionActivity Activity => state.Activity;
        public double VolumeScalar { get => state.VolumeScalar; set => state.WriteVolume(value); }
        public bool Muted { get => state.Muted; set => state.WriteMute(value); }
        public ApplicationSessionSnapshot Read() => state.Read();
        public void Dispose() => state.Dispose();
    }

    private sealed class Endpoint(List<Session> sessions, Action? opening = null, Action? disposing = null) : IApplicationSessionEndpoint
    {
        public int Count => sessions.Count;
        public IApplicationSessionAccess Open(int index) { opening?.Invoke(); return new BorrowedSession(sessions[index]); }
        public void Dispose() => disposing?.Invoke();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(35.5)]
    public async Task ExplicitVolumeChangesOnlyItsInstance(double percent)
    {
        var first = new Session("out", "first");
        var second = new Session("out", "second");
        await using var service = new ApplicationSessionService(_ => new Endpoint([first, second]));
        var target = service.Select("out");
        await service.ApplyAsync(target, second.Key, new SetSessionVolume(percent));
        Assert.Equal(percent / 100, second.VolumeScalar);
        Assert.Equal(0, first.Writes);
        Assert.Equal(1, second.Writes);
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task InvalidVolumeDoesNotOpenOrWrite(double percent)
    {
        await using var service = new ApplicationSessionService(_ => throw new Exception("Must not open"));
        var target = service.Select("out");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ApplyAsync(target, new("out", "instance"), new SetSessionVolume(percent)));
    }

    [Fact]
    public async Task RefreshAndExternalChangesAreReadOnlyAndKeepSamePidSessionsSeparate()
    {
        var first = new Session("out", "first");
        var second = new Session("out", "second") { System = true };
        await using var service = new ApplicationSessionService(_ => new Endpoint([first, second]));
        var target = service.Select("out");
        Assert.Equal(2, (await service.ReadAsync(target)).Sessions.Count);
        first.VolumeScalar = 0.2;
        second.Muted = true;
        var snapshot = await service.ReadAsync(target);
        Assert.Equal(20, snapshot.Sessions[0].VolumePercent);
        Assert.True(snapshot.Sessions[1].Muted);
        Assert.True(snapshot.Sessions[1].IsSystemSounds);
        Assert.Equal("Sons do sistema", snapshot.Sessions[1].Name);
        Assert.Equal(snapshot.Sessions[0].ProcessId, snapshot.Sessions[1].ProcessId);
        Assert.NotEqual(snapshot.Sessions[0].Key, snapshot.Sessions[1].Key);
        Assert.Equal(0, first.Writes + second.Writes);
    }

    [Fact]
    public async Task MuteAndUnmuteAreIndependentFromVolume()
    {
        var item = new Session("out", "first");
        await using var service = new ApplicationSessionService(_ => new Endpoint([item]));
        var target = service.Select("out");
        Assert.True((await service.ApplyAsync(target, item.Key, new SetSessionMute(true))).Muted);
        Assert.False((await service.ApplyAsync(target, item.Key, new SetSessionMute(false))).Muted);
        Assert.Equal(0.5, item.VolumeScalar);
    }

    [Fact]
    public async Task ReusedPidCannotReplaceMissingInstance()
    {
        var replacement = new Session("out", "replacement");
        await using var service = new ApplicationSessionService(_ => new Endpoint([replacement]));
        var target = service.Select("out");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(target, new("out", "original"), new SetSessionMute(true)));
        Assert.Equal(0, replacement.Writes);
    }

    [Fact]
    public async Task EndpointMismatchDoesNotOpenBackend()
    {
        await using var service = new ApplicationSessionService(_ => throw new Exception("Must not open"));
        var target = service.Select("out");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(target, new("other", "same"), new SetSessionMute(true)));
    }

    [Fact]
    public async Task ExpiredOrRemovedSessionCannotBeWrittenAndReconciliationRemovesIt()
    {
        var item = new Session("out", "first") { Activity = SessionActivity.Expired };
        var items = new List<Session> { item };
        await using var service = new ApplicationSessionService(_ => new Endpoint(items));
        var target = service.Select("out");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(target, item.Key, new SetSessionMute(true)));
        items.Clear();
        Assert.Empty((await service.ReadAsync(target)).Sessions);
        items.Add(new Session("out", "new"));
        Assert.Single((await service.ReadAsync(target)).Sessions);
        Assert.Equal(0, item.Writes);
    }

    [Fact]
    public async Task IdentityChangedImmediatelyBeforeWriteIsRejected()
    {
        var item = new Session("out", "first");
        int reads = 0;
        item.KeyReader = () => ++reads == 1 ? item.Key : new SessionKey("out", "replacement");
        await using var service = new ApplicationSessionService(_ => new Endpoint([item]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(service.Select("out"), item.Key, new SetSessionMute(true)));
        Assert.Equal(0, item.Writes);
    }

    [Fact]
    public async Task PartialReadFailureDisposesResourcesAndPreservesOtherSessions()
    {
        var denied = new Session("out", "denied") { FailRead = true };
        var available = new Session("out", "available");
        await using var service = new ApplicationSessionService(_ => new Endpoint([denied, available]));
        var catalog = await service.ReadAsync(service.Select("out"));
        Assert.Single(catalog.Sessions);
        Assert.Single(catalog.Warnings);
        Assert.Equal(available.Key, catalog.Sessions[0].Key);
        Assert.Equal(1, denied.Disposals);
        Assert.Equal(1, available.Disposals);
    }

    [Fact]
    public async Task BackendCannotPublishAnotherEndpointsSession()
    {
        var foreign = new Session("other", "foreign");
        await using var service = new ApplicationSessionService(_ => new Endpoint([foreign]));
        var catalog = await service.ReadAsync(service.Select("out"));
        Assert.Empty(catalog.Sessions);
        Assert.Single(catalog.Warnings);
        Assert.Equal(0, foreign.Writes);
    }

    [Fact]
    public async Task AccessDeniedDisposesSessionAndEndpointAndAllowsRecovery()
    {
        var item = new Session("out", "first") { Denied = true };
        int closed = 0;
        await using var service = new ApplicationSessionService(_ => new Endpoint([item], disposing: () => closed++));
        var target = service.Select("out");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyAsync(target, item.Key, new SetSessionMute(true)));
        Assert.Equal(1, item.Disposals);
        Assert.Equal(1, closed);
        item.Denied = false;
        Assert.Single((await service.ReadAsync(target)).Sessions);
    }

    [Fact]
    public async Task SwitchCancelsQueuedWriteAndIgnoresOldRead()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new Session("out", "first");
        await using var service = new ApplicationSessionService(id => new Endpoint(id == "out" ? [item] : [], () =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
        }));
        var old = service.Select("out");
        var reading = service.ReadAsync(old);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var command = service.ApplyAsync(old, item.Key, new SetSessionMute(true));
            var next = service.Select("other");
            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command);
            Assert.Empty((await service.ReadAsync(next)).Sessions);
            Assert.Equal(0, item.Writes);
        }
        finally { release.Set(); }
    }
}
