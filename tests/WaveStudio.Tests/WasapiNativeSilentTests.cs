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
        var token = TestContext.Current.CancellationToken;
        var route = await Preflight("WAVESTUDIO_SILENT_CAPTURE_NAME", "WAVESTUDIO_SILENT_HEADSET_NAME", token);
        await using var session = new WasapiSilentSession(route);
        var result = await session.RunAsync(TimeSpan.FromSeconds(5), token);
        Save("wasapi-silent", "native-silent", new { CaptureFormat = route.Capture.Mix, PlaybackFormat = route.Playback.Mix, Result = result });
        Assert.True(result.TransportObserved, "A prova não confirmou transporte silencioso, movimento e liberação dos dois streams.");
    }

    [Fact(Explicit = true)]
    [Trait("Category", "NativeSilentDriftDiagnostic")]
    public async Task AuthorizedEndpointsCollectSilentDriftEvidenceAtNominalRatio()
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable("WAVESTUDIO_DRIFT_DIAGNOSTIC_START_AUTHORIZED"));
        Assert.Equal("1", Environment.GetEnvironmentVariable("WAVESTUDIO_DRIFT_DIAGNOSTIC_PREREQUISITES_CONFIRMED"));

        var token = TestContext.Current.CancellationToken;
        var route = await Preflight("WAVESTUDIO_DRIFT_DIAGNOSTIC_CAPTURE_NAME", "WAVESTUDIO_DRIFT_DIAGNOSTIC_HEADSET_NAME", token);
        await using var session = new WasapiSilentSession(route, nativeDriftDiagnostic: true);
        var result = await session.RunAsync(TimeSpan.FromSeconds(10), token);
        var diagnostic = session.ReadDriftReport();
        Save("wasapi-drift-diagnostic", "native-drift", new
        {
            CaptureFormat = route.Capture.Mix, PlaybackFormat = route.Playback.Mix, Result = result, Diagnostic = diagnostic
        });

        Assert.True(result.TransportObserved && result.Capture.CaptureTiming is { Confirmed: true },
            "A coleta precisa confirmar transporte silencioso, continuidade, duração e limpeza.");
        Assert.NotNull(diagnostic);
        Assert.False(diagnostic.CorrectionEnabled);
        Assert.Equal(0, diagnostic.TrackingUpdates);
        Assert.True(diagnostic.AvailableCuts >= 60, "A coleta precisa de pelo menos 60 cortes coerentes.");
        var windows = diagnostic.Recent.Where(s => s.CorrectionInterval is not null).ToArray();
        Assert.True(windows.Length >= 10 && windows[^1].Cut100ns - windows[0].Cut100ns >= 40_000_000,
            "A coleta precisa de janelas em trechos separados por pelo menos quatro segundos.");
        Assert.All(diagnostic.Recent, s =>
        {
            Assert.Null(s.RequestedPpm);
            Assert.Equal(0, s.Capture.Evidence.AppliedPpm);
        });
    }

    private static async Task<TransportRoute> Preflight(string captureName, string playbackName, CancellationToken token)
    {
        AssertNoPreviewProcess();
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

        var capture = await Probe(EndpointDirection.Capture, captureName);
        var playback = await Probe(EndpointDirection.Playback, playbackName);
        AssertNoPreviewProcess();
        return new(capture, playback);
    }

    private void Save(string folder, string prefix, object evidence)
    {
        // O relatório registra somente formatos, evidências numéricas e rótulos fixos.
        var report = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
        output.WriteLine(report);
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts", folder));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{prefix}-{Guid.NewGuid():N}.json"), report);
    }

    private static void AssertNoPreviewProcess()
    {
        var processes = Process.GetProcessesByName("WaveStudio");
        try { Assert.True(processes.Length == 0, "Feche o Wave Studio normalmente antes da prova."); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}