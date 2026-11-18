using System.Diagnostics;
using System.Text.Json;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiNativeSilentTests(ITestOutputHelper output)
{
    [Fact(Explicit = true)]
    [Trait("Category", "NativeSilentTransport")]
    public async Task AuthorizedHeadsetAndMicrophoneRunBoundedSilentTransport()
    {
        Assert.True(Environment.GetEnvironmentVariable("WAVESTUDIO_SILENT_START_AUTHORIZED") == "1",
            "A prova ativa exige autorização específica de Start.");
        Assert.True(Environment.GetEnvironmentVariable("WAVESTUDIO_SILENT_PREREQUISITES_CONFIRMED") == "1",
            "Confirme a validação física pertinente, o headset e a ausência de testes/chamadas em andamento.");
        AssertNoPreviewProcess();

        var token = TestContext.Current.CancellationToken;
        var catalog = await new CoreAudioDeviceService().QueryAsync(token);
        async Task<WasapiEndpointInfo> Probe(EndpointDirection direction, string variable)
        {
            var name = Environment.GetEnvironmentVariable(variable);
            Assert.False(string.IsNullOrWhiteSpace(name), "Selecione por nome exato um dispositivo ativo da direção solicitada.");
            var matches = catalog.Devices.Where(d => d.Direction == direction && d.State == EndpointState.Active
                && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            Assert.True(matches.Length == 1, $"A seleção de {direction} deve identificar exatamente um dispositivo ativo.");
            var endpoint = new TransportEndpoint(matches[0].Id, direction, 1);
            var probe = await WasapiEndpointProbe.QueryAsync(endpoint, token);
            Assert.True(probe.Evidence == FormatEvidence.ExactCapability && probe.Error is null && probe.Mix is not null,
                $"Capacidade exata indisponível para {direction}.");
            return probe;
        }

        var capture = await Probe(EndpointDirection.Capture, "WAVESTUDIO_SILENT_CAPTURE_NAME");
        var playback = await Probe(EndpointDirection.Playback, "WAVESTUDIO_SILENT_HEADSET_NAME");
        AssertNoPreviewProcess();
        await using var session = new WasapiSilentSession(new TransportRoute(capture, playback));
        var result = await session.RunAsync(TimeSpan.FromSeconds(5), token);

        // O snapshot contém evidências numéricas e nomes fixos de etapas, sem IDs de endpoints ou PCM.
        var report = JsonSerializer.Serialize(new { CaptureFormat = capture.Mix, PlaybackFormat = playback.Mix, Result = result },
            new JsonSerializerOptions { WriteIndented = true });
        output.WriteLine(report);
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/wasapi-silent"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"native-silent-{Guid.NewGuid():N}.json"), report);
        Assert.True(result.TransportObserved, "A prova não confirmou transporte silencioso, movimento e liberação dos dois streams.");
    }

    private static void AssertNoPreviewProcess()
    {
        var processes = Process.GetProcessesByName("WaveStudio");
        try { Assert.True(processes.Length == 0, "Feche o Wave Studio normalmente antes da prova."); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}