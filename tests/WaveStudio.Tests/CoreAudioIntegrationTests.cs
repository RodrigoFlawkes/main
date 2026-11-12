using WaveStudio.Audio;
using Xunit;

namespace WaveStudio.Tests;

public sealed class CoreAudioIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task ReadsEndpointControlsAndDisposesNativeCallbacksWithoutChangingLevels()
    {
        var catalog = await new CoreAudioDeviceService().QueryAsync(TestContext.Current.CancellationToken);
        var activeDefaults = catalog.Devices.Where(device => device.State == EndpointState.Active && device.IsDefaultMultimedia);
        foreach (var device in activeDefaults)
        {
            await using var session = new EndpointVolumeSession();
            var snapshot = await session.ReadAsync(session.Select(device.Id));
            output.WriteLine($"{device.Direction}: volume={snapshot.VolumePercent}, mute={snapshot.Muted}, L/R={snapshot.Stereo}, motivo={snapshot.BalanceError}");
            Assert.True(snapshot.VolumePercent is not null || snapshot.VolumeError is not null);
            Assert.True(snapshot.Muted is not null || snapshot.MuteError is not null);
            Assert.True(snapshot.Stereo is not null || snapshot.BalanceError is not null);
            if (snapshot.VolumePercent is double percent)
                Assert.InRange(percent, 0, 100);
        }
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task EnumeratesRealEndpointsAndReturnsDetachedSnapshots()
    {
        var service = new CoreAudioDeviceService();
        var catalog = await service.QueryAsync(TestContext.Current.CancellationToken);
        output.WriteLine($"Endpoints reais: {catalog.Devices.Count}. Avisos: {catalog.Warnings.Count}.");
        foreach (var device in catalog.Devices)
        {
            output.WriteLine($"{device.Direction} | {device.Name} | {device.State} | MM={device.IsDefaultMultimedia} | COM={device.IsDefaultCommunications} | {device.MixFormat}");
            Assert.False(string.IsNullOrWhiteSpace(device.Id));
            Assert.False(string.IsNullOrWhiteSpace(device.Name));
            if (device.MixFormat is { } format)
            {
                Assert.True(format.SampleRate > 0);
                Assert.True(format.Channels > 0);
                Assert.True(format.BitsPerSample > 0);
            }
        }
        Assert.Equal(catalog.Devices.Count, catalog.Devices.Select(device => device.Id).Distinct().Count());
        // An empty hardware inventory is valid. API failures are reported as warnings.
        Assert.Empty(catalog.Warnings);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task SerializesConcurrentQueriesAndCanQueryAgainAfterCancellation()
    {
        var service = new CoreAudioDeviceService();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.QueryAsync(canceled.Token));
        var results = await Task.WhenAll(service.QueryAsync(TestContext.Current.CancellationToken),
            service.QueryAsync(TestContext.Current.CancellationToken));
        Assert.All(results, result => Assert.Empty(result.Warnings));
    }
}
