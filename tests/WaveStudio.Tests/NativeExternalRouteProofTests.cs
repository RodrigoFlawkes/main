using System.Diagnostics;
using System.Text.Json;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class NativeExternalRouteProofTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "NativeBoundedExternalRoute")]
    public async Task AuthorizedOperatorRunsBothRoutesAndVerifiesRestoration()
    {
        var token = TestContext.Current.CancellationToken;
        foreach (var flag in new[] { "AUTHORIZED", "ISOLATION_CONFIRMED", "HEADSET_LOW_CONFIRMED", "DRIVER_CHECKED" })
            Assert.Equal("1", Read(flag));
        Assert.True(Environment.UserInteractive && !Console.IsInputRedirected);
        WasapiNativeSilentTests.AssertNoPreviewProcess();
        WasapiNativeAudibleProofTests.AssertNoOtherTestApphost();

        using var browser = Process.GetProcessById(int.Parse(Read("BROWSER_PID")));
        using var obs = Process.GetProcessById(int.Parse(Read("OBS_PID")));
        Assert.Contains(browser.ProcessName.ToLowerInvariant(), new[] { "msedge", "chrome", "firefox" });
        Assert.Equal("obs64", obs.ProcessName, ignoreCase: true);
        // Os PIDs apenas abrem handles do processo escolhido. A identidade observada inclui o início e o perfil/cena escolhidos.
        string browserIdentity = ProcessIdentity(browser, Read("BROWSER_PROFILE"));
        string obsIdentity = ProcessIdentity(obs, Read("OBS_SCENE_SOURCE"));
        var ids = new ExternalEndpointIds(Read("A_RENDER"), Read("A_CAPTURE"), Read("B_RENDER"),
            Read("B_CAPTURE"), Read("HEADSET"), Read("MIC"));
        var topology = await NativeExternalRoute.ProbeAsync(ids, 1, token);
        var initial = new ExternalSelections(Original(browserIdentity, "BROWSER_BEFORE"), Original(obsIdentity, "OBS_BEFORE"));
        await RequireOriginalEndpoints(initial, token);
        await RequireNoCableDefaults(ids, token);
        var settings = new ExternalRouteConfiguration(topology, initial);
        var trials = new List<object>(2);
        int accepted = 0;
        string evidenceId = Guid.NewGuid().ToString("N");
        ExternalRestoreReport? after = null;
        bool retained = false;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            Console.WriteLine("Confira a seleção temporária no mixer por aplicativo e na fonte isolada do OBS.");
            var observed = await ObserveSettings(settings, browser, obs, token);
            var consent = settings.Confirm(observed, true, true, stop.Token);
            var watchReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var watch = WatchTopology(stop, watchReady);
            try
            {
                await watchReady.Task.WaitAsync(stop.Token);
                Console.WriteLine("Prova agrupada. Pressione Esc para testar a parada e Enter para confirmar a execução.");
                await WasapiNativeAudibleProofTests.ConfirmStart(stop.Token);
                foreach (var flow in new[] { ExternalFlow.Browser, ExternalFlow.Microphone })
                {
                    if (flow == ExternalFlow.Microphone)
                    {
                        Console.WriteLine("Prepare o medidor isolado no OBS e pressione Enter para seguir. Esc encerra a sessão.");
                        if (await ReadKey(token, ConsoleKey.Enter, ConsoleKey.Escape) == ConsoleKey.Escape) break;
                    }
                    stop.Token.ThrowIfCancellationRequested();
                    Assert.False(browser.HasExited || obs.HasExited, "Um dos aplicativos selecionados fechou.");
                    WasapiNativeSilentTests.AssertNoPreviewProcess();
                    WasapiNativeAudibleProofTests.AssertNoOtherTestApphost();
                    var verified = await NativeExternalRoute.ProbeAsync(ids, 1, stop.Token);
                    Assert.True(topology == verified, "A topologia, a capacidade ou o formato mudou.");
                    await RequireNoCableDefaults(ids, stop.Token);

                    var session = new WasapiExternalRouteSession(topology, flow, consent);
                    using var phase = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    Console.WriteLine(flow == ExternalFlow.Browser ? "Navegador: reproduza o sinal local conhecido."
                        : "OBS: observe o medidor e fale em tom normal.");
                    Console.WriteLine("B alterna a atenuação. Esc encerra a sessão.");
                    var keyboard = WatchKeys(session, browser, obs, phase, stop);
                    var stats = Stopwatch.StartNew();
                    using var self = Process.GetCurrentProcess();
                    var cpu = self.TotalProcessorTime;
                    long initialBytes = self.WorkingSet64;
                    try
                    {
                        var result = await session.RunAsync(WasapiExternalRouteSession.MaximumDuration, phase.Token);
                        retained = result.Transport.Cleanup.ResourcesRetained;
                        self.Refresh();
                        trials.Add(new
                        {
                            Flow = flow.ToString(),
                            CaptureFormat = topology.Route(flow).Capture.Mix,
                            RenderFormat = topology.Route(flow).Playback.Mix,
                            Result = result,
                            WallMilliseconds = stats.Elapsed.TotalMilliseconds,
                            CpuMilliseconds = (self.TotalProcessorTime - cpu).TotalMilliseconds,
                            WorkingSetBefore = initialBytes,
                            WorkingSetAfter = self.WorkingSet64
                        });
                        if (!result.Passed || result.Levels is not { BypassNonZeroFrames: > 0, EffectNonZeroFrames: > 0 }) break;
                        accepted++;
                    }
                    finally
                    {
                        await phase.CancelAsync();
                        await keyboard;
                        retained = true;
                        await session.DisposeAsync();
                        retained = false;
                    }
                }
            }
            finally
            {
                await stop.CancelAsync();
                await watch;
            }
        }
        finally
        {
            SaveEvidence(evidenceId, trials, retained, after);
            // A restauração é solicitada só após quiescência. O relatório parcial já está preservado.
            if (!retained && !token.IsCancellationRequested)
            {
                Console.WriteLine("Confira a configuração atual. Restaure manualmente só onde a decisão for Restore.");
                var current = await ObserveSettings(settings, browser, obs, token);
                var available = await OriginalAvailability(initial, token);
                var plan = settings.PlanRestore(current, available.Browser, available.Consumer);
                Console.WriteLine($"Navegador: {plan.Browser}. OBS: {plan.Consumer}.");
                if (plan.Browser == ExternalRestoreState.Restore || plan.Consumer == ExternalRestoreState.Restore)
                {
                    Console.WriteLine("Depois de aplicar a restauração indicada, confira os dois valores novamente.");
                    current = await ObserveSettings(settings, browser, obs, token);
                }
                available = await OriginalAvailability(initial, token);
                after = settings.PlanRestore(current, available.Browser, available.Consumer);
                SaveEvidence(evidenceId, trials, retained, after);
            }
        }
        Assert.Equal(2, accepted);
        Assert.True(after is { Complete: true }, "Restauração pendente.");
        // A escuta e o recebimento no OBS ainda dependem do relato do operador.
    }

    private static string Read(string key)
    {
        string? value = Environment.GetEnvironmentVariable("WAVESTUDIO_EXTERNAL_" + key);
        Assert.False(string.IsNullOrWhiteSpace(value), "Pré-condição ausente: " + key);
        return value;
    }

    private static string ProcessIdentity(Process process, string context) =>
        $"{process.Id}/{process.StartTime.ToUniversalTime().Ticks}/{context}";

    private static ExternalSelection Original(string identity, string name)
    {
        string value = Read(name);
        return new(identity, value == "DEFAULT" ? null : value, true, true);
    }

    private static async Task<(bool Browser, bool Consumer)> OriginalAvailability(ExternalSelections initial, CancellationToken token)
    {
        var catalog = await new CoreAudioDeviceService().QueryAsync(token);
        bool IsActive(ExternalSelection selection, EndpointDirection direction) => selection.EndpointId is null ||
            catalog.Devices.Any(d => d.Id == selection.EndpointId && d.Direction == direction && d.State == EndpointState.Active);
        return (IsActive(initial.Browser, EndpointDirection.Playback), IsActive(initial.Consumer, EndpointDirection.Capture));
    }

    private static async Task RequireNoCableDefaults(ExternalEndpointIds ids, CancellationToken token)
    {
        var devices = await new CoreAudioDeviceService().QueryAsync(token);
        string[] cableIds = [ids.BrowserRender, ids.BrowserCapture, ids.MicRender, ids.MicCapture];
        Assert.DoesNotContain(devices.Devices, device => (device.IsDefaultMultimedia || device.IsDefaultCommunications) &&
            cableIds.Contains(device.Id, StringComparer.OrdinalIgnoreCase));
    }

    private static async Task RequireOriginalEndpoints(ExternalSelections initial, CancellationToken token)
    {
        var present = await OriginalAvailability(initial, token);
        Assert.True(present.Browser && present.Consumer, "Configuração original indisponível.");
    }

    private static async Task<ExternalSelections> ObserveSettings(ExternalRouteConfiguration settings, Process browser,
        Process obs, CancellationToken token)
    {
        var a = await Observe("navegador", browser, settings.Before.Browser, settings.Applied.Browser);
        var b = await Observe("OBS", obs, settings.Before.Consumer, settings.Applied.Consumer);
        return new(a, b);

        async Task<ExternalSelection> Observe(string role, Process app, ExternalSelection before, ExternalSelection applied)
        {
            if (app.HasExited) return before with { Accessible = false };
            Console.WriteLine($"Confira {role} no mesmo perfil ou fonte. T = temporária, O = original, X = divergente, I = inacessível, Esc = sair.");
            return await ReadKey(token, ConsoleKey.T, ConsoleKey.O, ConsoleKey.X, ConsoleKey.I, ConsoleKey.Escape) switch
            {
                ConsoleKey.T => applied,
                ConsoleKey.O => before,
                ConsoleKey.X => before with { ClientId = "contexto divergente" },
                ConsoleKey.I => before with { Accessible = false },
                _ => throw new OperationCanceledException()
            };
        }
    }

    private static Task WatchTopology(CancellationTokenSource stop, TaskCompletionSource ready) =>
        Task.Run(() =>
        {
            try
            {
                using var watch = new CoreAudioWatchBackend(() => stop.Cancel());
                if (watch.NotificationError is not null) throw new InvalidOperationException("Notificações de endpoints indisponíveis.");
                ready.TrySetResult();
                stop.Token.WaitHandle.WaitOne();
            }
            catch (Exception error)
            {
                ready.TrySetException(error);
                stop.Cancel();
            }
        });

    private static async Task WatchKeys(WasapiExternalRouteSession session, Process browser, Process obs,
        CancellationTokenSource phase, CancellationTokenSource stop)
    {
        bool bypass = true;
        try
        {
            while (!phase.IsCancellationRequested)
            {
                if (browser.HasExited || obs.HasExited) { await stop.CancelAsync(); return; }
                if (Console.KeyAvailable)
                {
                    switch (Console.ReadKey(intercept: true).Key)
                    {
                        case ConsoleKey.B: session.SetBypass(bypass = !bypass); break;
                        case ConsoleKey.Escape: await stop.CancelAsync(); return;
                    }
                }
                await Task.Delay(15, phase.Token);
            }
        }
        catch (OperationCanceledException) when (phase.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            await stop.CancelAsync();
        }
    }

    private static async Task<ConsoleKey> ReadKey(CancellationToken token, params ConsoleKey[] choices)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key is var key && choices.Contains(key)) return key;
            await Task.Delay(20, token);
        }
    }

    private static void SaveEvidence(string evidenceId, List<object> trials, bool retained, ExternalRestoreReport? restored)
    {
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/f3-external-route"));
        Directory.CreateDirectory(directory);
        // O JSON é limitado a métricas, formatos, estados e papéis fixos.
        string path = Path.Combine(directory, $"external-{evidenceId}.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new { Trials = trials, ResourcesRetained = retained, Restored = restored },
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
