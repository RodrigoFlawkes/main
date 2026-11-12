using System.Diagnostics;
using NAudio.CoreAudioApi;
using WaveStudio.Audio;
using Xunit;

namespace WaveStudio.Tests;

public sealed class AudioWatchIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task ReadsRealPeaksWithPersistentSourcesAndNotificationSubscription()
    {
        var catalog = await new CoreAudioDeviceService().QueryAsync(TestContext.Current.CancellationToken);
        var playback = catalog.Devices.FirstOrDefault(item => item.Direction == EndpointDirection.Playback && item.IsDefaultMultimedia && item.State == EndpointState.Active);
        var capture = catalog.Devices.FirstOrDefault(item => item.Direction == EndpointDirection.Capture && item.IsDefaultMultimedia && item.State == EndpointState.Active);
        await using var worker = new AudioWatchWorker();
        var selected = worker.Select(playback?.Id, capture?.Id);
        var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        long memory = process.WorkingSet64;
        var timer = Stopwatch.StartNew();
        var samples = new List<AudioWatchSnapshot>();
        for (int index = 0; index < 20; index++)
        {
            await Task.Delay(110, TestContext.Current.CancellationToken);
            if (worker.Latest is { } sample && sample.Selection == selected) samples.Add(sample);
        }
        Assert.NotEmpty(samples);
        Assert.All(samples, sample =>
        {
            Assert.Equal(selected, sample.Selection);
            if (sample.Playback.Scalar is double left) Assert.InRange(left, 0, 1);
            if (sample.Capture.Scalar is double right) Assert.InRange(right, 0, 1);
        });
        output.WriteLine($"Amostras: {samples.Count}; duração {timer.Elapsed.TotalSeconds:F2} s; consulta média {samples.Average(item => item.QueryMilliseconds):F3} ms.");
        output.WriteLine($"Reprodução observada: {string.Join(", ", samples.Select(item => item.Playback.Scalar?.ToString("F6") ?? item.Playback.Error).Distinct())}");
        output.WriteLine($"Captura observada: {string.Join(", ", samples.Select(item => item.Capture.Scalar?.ToString("F6") ?? item.Capture.Error).Distinct())}");
        output.WriteLine(samples[^1].NotificationStatus);
        process.Refresh();
        output.WriteLine($"Processo de testes completo: CPU delta {(process.TotalProcessorTime - cpu).TotalMilliseconds:F1} ms; working set inicial {memory}, final {process.WorkingSet64} bytes.");
        await worker.DisposeAsync();
        Assert.Null(worker.ShutdownError);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task EvaluatesSessionMeterApiWithoutCapturingAudioOrWriting()
    {
        await Task.Run(() =>
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var endpoint))
            {
                output.WriteLine("Sem endpoint de reprodução padrão.");
                return;
            }
            using (endpoint)
            {
                var sessions = endpoint.AudioSessionManager.Sessions;
                int supported = 0;
                for (int index = 0; index < sessions.Count; index++)
                {
                    using var session = sessions[index];
                    using var meter = session.AudioMeterInformation;
                    if (meter is null) continue;
                    var reading = PeakReading.FromScalar(meter.MasterPeakValue);
                    output.WriteLine($"Pico de sessão observado: {reading.Scalar}; erro {reading.Error}");
                    if (reading.Scalar is not null) supported++;
                }
                output.WriteLine($"Medidores de sessão disponíveis: {supported} de {sessions.Count}. Avaliação de leitura pontual.");
            }
        }, TestContext.Current.CancellationToken);
    }
}
