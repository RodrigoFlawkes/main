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
        Assert.Equal(snapshots.Sessions.Count, snapshots.Sessions.Select(item => item.Key).Distinct().Count());
        await using var volume = new EndpointVolumeSession();
        var target = volume.Select(device.Id);
        var watch = Stopwatch.StartNew();
        for (int index = 0; index < 10; index++) await volume.ReadAsync(target);
        output.WriteLine($"Consulta endpoint completa: média {watch.Elapsed.TotalMilliseconds / 10:F2} ms em 10 leituras.");
        watch.Restart();
        for (int index = 0; index < 10; index++) await mixer.ReadAsync(selected);
        output.WriteLine($"Consulta mixer completa: média {watch.Elapsed.TotalMilliseconds / 10:F2} ms em 10 leituras.");
    }
}
