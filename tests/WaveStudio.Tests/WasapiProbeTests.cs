using System.Text.Json;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiProbeTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task RealEndpointProbeQueriesDetachedFormatAndPeriodsWithoutInitializingStream()
    {
        var catalog = await new CoreAudioDeviceService().QueryAsync(TestContext.Current.CancellationToken);
        var report = new List<object>();
        foreach (var direction in new[] { EndpointDirection.Capture, EndpointDirection.Playback })
        {
            var device = catalog.Devices.FirstOrDefault(d => d.Direction == direction && d.State == EndpointState.Active);
            if (device is null) { report.Add(new { Direction = direction.ToString(), Available = false }); continue; }
            var endpoint = new TransportEndpoint(device.Id, direction, 7);
            var result = await WasapiEndpointProbe.QueryAsync(endpoint, TestContext.Current.CancellationToken);
            Assert.Equal(endpoint, result.Endpoint); Assert.False(result.StreamInitialized);
            Assert.Null(result.BufferCapacityFrames); Assert.Null(result.StreamLatency100ns);
            Assert.True(result.Mix is not null || result.Error is not null);
            if (result.Mix is not null)
            {
                Assert.True(result.Mix.BlockAlign > 0); Assert.True(result.DefaultPeriod100ns > 0);
                Assert.True(result.MinimumExclusivePeriod100ns > 0);
            }
            var numeric = new { Direction = direction.ToString(), result.State, result.Mix, result.DefaultPeriod100ns,
                result.MinimumExclusivePeriod100ns, result.BufferCapacityFrames, result.StreamLatency100ns,
                result.Evidence, result.StreamInitialized, result.Error };
            report.Add(numeric); output.WriteLine(JsonSerializer.Serialize(numeric));
        }
        string directory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "wasapi-backend");
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "native-probe.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Assert.Empty(catalog.Warnings);
    }
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task InvalidEndpointIsReportedWithoutDefaultFallbackAndCancellationPropagates()
    {
        var endpoint = new TransportEndpoint("wave-studio-missing-simulated-id", EndpointDirection.Capture, 1);
        var result = await WasapiEndpointProbe.QueryAsync(endpoint, TestContext.Current.CancellationToken);
        Assert.Equal(endpoint, result.Endpoint); Assert.NotNull(result.Error); Assert.Null(result.Mix);
        Assert.False(result.StreamInitialized);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WasapiEndpointProbe.QueryAsync(endpoint, cancellation.Token));
    }
}
