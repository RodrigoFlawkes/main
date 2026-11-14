using System.Diagnostics;
using System.Text.Json;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

// Explicit is intentional: ordinary regression cannot authorize Initialize on hardware.
public sealed class WasapiNativePreparationTests(ITestOutputHelper output)
{
    [Fact(Explicit = true)]
    [Trait("Category", "NativePreparation")]
    public async Task AuthorizedExactEndpointsPrepareClockAndCloseWithoutStartOrAudioBuffers()
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable("WAVESTUDIO_PREPARE_AUTHORIZED"));
        var report = new List<object>();
        var token = TestContext.Current.CancellationToken;
        var catalog = await new CoreAudioDeviceService().QueryAsync(token);
        foreach (var direction in new[] { EndpointDirection.Capture, EndpointDirection.Playback })
        {
            AssertNoPreviewProcess();
            string? name = Environment.GetEnvironmentVariable(direction == EndpointDirection.Capture
                ? "WAVESTUDIO_PREPARE_CAPTURE_NAME" : "WAVESTUDIO_PREPARE_PLAYBACK_NAME");
            Assert.False(string.IsNullOrWhiteSpace(name));
            // Resolve an explicit unique name once to a full ID; no defaults or nearest-device fallback.
            var device = Assert.Single(catalog.Devices, d => d.Direction == direction && d.State == EndpointState.Active
                && d.Name.Contains(name!, StringComparison.OrdinalIgnoreCase));
            var endpoint = new TransportEndpoint(device.Id, direction, 61);
            var probe = await WasapiEndpointProbe.QueryAsync(endpoint, token);
            Assert.Equal(FormatEvidence.ExactCapability, probe.Evidence); Assert.Null(probe.Error); Assert.NotNull(probe.Mix);
            probe.Mix.Validate(); AssertNoPreviewProcess();
            var result = await WasapiPreparation.PrepareAsync(endpoint, probe.Mix, TransportActivityGuard.Shared, token);
            var numeric = new { Direction = direction.ToString(), result.Format, result.DefaultPeriod100ns,
                result.MinimumExclusivePeriod100ns, result.BufferFrames, result.StreamLatency100ns, result.Initialized,
                result.Clock, result.FailedStage, result.HResult, result.CleanupError, result.ResourcesReleased,
                result.Succeeded, result.AudioStarted, PositionIsRunningEvidence = false };
            report.Add(numeric); output.WriteLine(JsonSerializer.Serialize(numeric));
            Assert.True(result.ResourcesReleased); Assert.False(result.AudioStarted);
            if (result.Succeeded)
            {
                Assert.True(result.Initialized); Assert.True(result.BufferFrames > 0); Assert.True(result.StreamLatency100ns >= 0);
                Assert.NotNull(result.Clock); Assert.Equal(0, result.Clock.Value.FrequencyHResult); Assert.True(result.Clock.Value.Frequency > 0);
                Assert.Contains(result.Clock.Value.PositionHResult, new[] { 0, 1 });
            }
            else Assert.NotNull(result.FailedStage); // Failure is evidence, never a reason to Start or select another device.
        }
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/wasapi-preparation"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "native-preparation.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void AssertNoPreviewProcess()
    {
        var processes = Process.GetProcessesByName("WaveStudio");
        try { Assert.Empty(processes); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
