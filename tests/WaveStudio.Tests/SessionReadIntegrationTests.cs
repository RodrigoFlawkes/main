using System.Diagnostics;
using WaveStudio.Audio;
using Xunit;

namespace WaveStudio.Tests;

public sealed class SessionReadIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task EnumeratesRealPlaybackSessionsAndMeasuresReadCost()
    {
        var catalog = await new CoreAudioDeviceService().QueryAsync(TestContext.Current.CancellationToken);
        var device = catalog.Devices.FirstOrDefault(item => item.Direction == EndpointDirection.Playback && item.IsDefaultMultimedia)
            ?? catalog.Devices.FirstOrDefault(item => item.Direction == EndpointDirection.Playback && item.State == EndpointState.Active);
        if (device is null) { output.WriteLine("Sem endpoint de reprodução disponível. Inventário vazio válido."); return; }
        await using var mixer = new ApplicationSessionService();
        var selected = mixer.Select(device.Id);
        var snapshots = await mixer.ReadAsync(selected);
        output.WriteLine($"Sessões reais: {snapshots.Sessions.Count}; avisos: {snapshots.Warnings.Count}.");
        foreach (var item in snapshots.Sessions)
        {
            output.WriteLine($"{item.Name} | PID {item.ProcessId} | {item.Activity} | volume {item.VolumePercent} | mute {item.Muted} | erro {item.Error}");
            Assert.Equal(device.Id, item.Key.EndpointId);
            Assert.False(string.IsNullOrWhiteSpace(item.Key.InstanceId));
        }
        Assert.Empty(snapshots.Warnings);
        Assert.All(snapshots.Sessions, item =>
        {
            Assert.NotNull(item.Peak);
            Assert.True(item.PeakRecordedAt > 0);
            if (item.Peak.Scalar is double scalar) Assert.InRange(scalar, 0, 1);
        });
        output.WriteLine($"Picos disponíveis: {snapshots.Sessions.Count(item => item.Peak?.Scalar is not null)}; valores {string.Join(", ", snapshots.Sessions.Select(item => item.Peak?.Scalar?.ToString("F6") ?? "indisponível").Distinct())}.");
        Assert.Equal(snapshots.Sessions.Count, snapshots.Sessions.Select(item => item.Key).Distinct().Count());
        await using var volume = new EndpointVolumeSession();
        var target = volume.Select(device.Id);
        var watch = Stopwatch.StartNew();
        for (int index = 0; index < 10; index++) await volume.ReadAsync(target);
        output.WriteLine($"Consulta endpoint completa: média {watch.Elapsed.TotalMilliseconds / 10:F2} ms em 10 leituras.");
        watch.Restart();
        for (int index = 0; index < 100; index++) await mixer.ReadAsync(selected);
        output.WriteLine($"Consulta mixer com picos: média {watch.Elapsed.TotalMilliseconds / 100:F2} ms em 100 leituras de custo, frequência da UI 1 Hz.");
    }
}
