using WaveStudio.Audio.Engine;
using Xunit;
using static WaveStudio.Tests.WasapiContractTests;

namespace WaveStudio.Tests;

public sealed class CheckupTransportTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MissingOrFailedSnapshotInvalidatesDeliveryInsteadOfUsingZeroOrThrowing(bool throws)
    {
        int missing = 0;
        using var fake = new IsolatedTransportTests.FakeTransport();
        await using var session = new IsolatedTransportSession(new(), pipe => Volatile.Read(ref missing) != 0
            ? throws ? throw new InvalidOperationException("snapshot simulado") : null : pipe.Snapshot().Buffer);
        var route = Route(); await session.StartAsync(route, fake, 0, cancellation: TestContext.Current.CancellationToken);
        fake.Capture(route, 480, 100000, .2f); fake.Render(route, 480, 100000);
        var known = await session.TickAsync(fake.CloseThrough(100000));
        Volatile.Write(ref missing, 1);
        var failed = await session.TickAsync(fake.CloseThrough(200000));
        Assert.Equal(TransportState.Faulted, failed.State); Assert.Equal(TransportFault.SnapshotUnavailable, failed.Fault);
        Assert.False(failed.BufferSnapshotAvailable); Assert.Null(failed.MeanOccupancy);
        Assert.Equal(known.ProducedFrames, failed.ProducedFrames); Assert.Equal(known.ReadFrames, failed.ReadFrames);
        long submitted = fake.Submissions; fake.Render(route, 480, 300000); Assert.Equal(submitted, fake.Submissions);
    }
    [Fact]
    public async Task StartCallbacksCannotConsumePrimingOrDeliverBeforeGateIsEnabled()
    {
        var route = Route(); using var fake = new IsolatedTransportTests.FakeTransport();
        await using var session = new IsolatedTransportSession(new());
        Assert.False(session.Snapshot.BufferSnapshotAvailable);
        fake.BeforeStartPlayback = () => fake.Render(route, 480, 100000);
        fake.BeforeStartCapture = () => fake.Capture(route, 480, 100000, .5f);
        var started = await session.StartAsync(route, fake, 0, cancellation: TestContext.Current.CancellationToken);
        Assert.Equal(TransportState.Running, started.State); Assert.Equal(1024, started.PrimedFrames);
        Assert.Equal(0, started.ReadFrames); Assert.Equal(0, started.ProducedFrames); Assert.Equal(0, fake.Submissions);
        fake.Render(route, 480, 200000); var stopped = await session.StopAsync();
        Assert.Equal(480, stopped.ReadFrames); Assert.Equal(544, stopped.DiscardedQueuedFrames);
        Assert.Equal(1, fake.Submissions);
    }
}
