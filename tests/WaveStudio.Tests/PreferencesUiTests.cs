using System.Diagnostics;
using WaveStudio.Audio;
using WaveStudio.Desktop;
using WaveStudio.Desktop.Settings;
using Xunit;
using System.Text.Json;

namespace WaveStudio.Tests;

public sealed class PreferencesUiTests(ITestOutputHelper output)
{
    private sealed class TestForm : Form
    {
        protected override bool ShowWithoutActivation => true;
    }

    private sealed class TestWriter(Action beforeWrite) : ISettingsWriter
    {
        private readonly SettingsWriter writer = new();
        public void WriteNew(string path, byte[] bytes, CancellationToken token)
        {
            beforeWrite();
            writer.WriteNew(path, bytes, token);
        }
        public void Commit(string temporary, string target, bool replacing, string? backup) =>
            writer.Commit(temporary, target, replacing, backup);
    }

    internal static async Task OnSta(Func<Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Exception? failure = null;
            try
            {
                using var context = new ApplicationContext();
                using var dispatch = new Control();
                _ = dispatch.Handle;
                dispatch.BeginInvoke((Action)(async () =>
                {
                    try { await test(); }
                    catch (Exception error) { failure = error; }
                    finally { context.ExitThread(); }
                }));
                Application.Run(context);
            }
            catch (Exception error) { failure = error; }
            if (failure is null) completion.TrySetResult();
            else completion.TrySetException(failure);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // A real message loop and asynchronous polling avoid reentrant DoEvents.
        // The deadline remains outside the STA; the process watchdog is the last resort.
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        Assert.True(thread.Join(TimeSpan.FromSeconds(2)), "A thread STA não terminou após o teste.");
    }

    internal static async Task PumpUntilAsync(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException("A UI não concluiu a operação.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    internal static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));

    [Fact]
    public Task RecoveryWithSameIdClearsLatchedFailureAndMetadataRefreshPreservesGeneration() => OnSta(async () =>
    {
        var state = new FakeEndpointState();
        using var host = new TestForm { ClientSize = new Size(800, 900), ShowInTaskbar = false, Opacity = 0 };
        using var audio = new EndpointControls(new EndpointVolumeSession(_ => new FakeEndpointVolume(state)));
        using var panel = new DevicePanel("Saída", audio);
        host.Controls.Add(panel);
        host.Show();
        var device = new DeviceSnapshot("same", "Saída", EndpointDirection.Playback, EndpointState.Active, true, false, null, null);
        var catalog = new DeviceCatalog([device], []);
        panel.ShowDevices(catalog, EndpointDirection.Playback, device);
        var number = Descendants(audio).OfType<NumericUpDown>().First();
        await PumpUntilAsync(() => number.Enabled);
        var original = audio.Selection;
        state.IgnoreWrites = true;
        number.Value = 20;
        await PumpUntilAsync(() => audio.HasLatchedFailure);
        Assert.False(number.Enabled);
        panel.ShowDevices(catalog, EndpointDirection.Playback, device, resetControls: false);
        Assert.Equal(original, audio.Selection);
        Assert.True(audio.HasLatchedFailure);
        state.IgnoreWrites = false;
        panel.ShowDevices(catalog, EndpointDirection.Playback, device, resetControls: true);
        await PumpUntilAsync(() => number.Enabled);
        Assert.NotEqual(original, audio.Selection);
        Assert.False(audio.HasLatchedFailure);
        Assert.Equal(1, state.Writes);
        var stopping = panel.StopAsync().AsTask();
        await PumpUntilAsync(() => stopping.IsCompleted);
        stopping.GetAwaiter().GetResult();
    });

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task MainWindowReloadsAbsentPreferenceAcrossTwoStartsWithIsolatedStorage()
    {
        using var directory = new TemporarySettingsDirectory();
        var settings = new StudioSettings
        {
            Playback = new(false, "absent-output-for-test"), Capture = new(),
            Endpoints = [new("absent-output-for-test", EndpointDirection.Playback, 25, true, 50)],
            Window = new(90000, 90000, 2000, 1500)
        };
        File.WriteAllBytes(directory.FilePath, SettingsJson.Encode(settings));
        for (int index = 0; index < 2; index++)
        {
            var warnings = new List<string>();
            await OnSta(async () =>
            {
                void Phase(string phase) => output.WriteLine($"abertura={index}; fase={phase}; thread={Environment.CurrentManagedThreadId}; {DateTimeOffset.UtcNow:O}");
                Phase("construção");
                using var form = new MainForm(new SettingsStore(directory.FilePath), warning =>
                {
                    warnings.Add(warning);
                    Phase("aviso: " + warning);
                }) { ShowInTaskbar = false, Opacity = 0 };
                form.FormClosed += (_, _) => Phase("FormClosed");
                form.Disposed += (_, _) => Phase("Disposed");
                Phase("Show");
                form.Show();
                await PumpUntilAsync(() => form.StartupTask.IsCompleted);
                form.StartupTask.GetAwaiter().GetResult();
                Phase("startup concluído");
                var panel = Descendants(form).OfType<DevicePanel>().First();
                await PumpUntilAsync(() => Descendants(panel).OfType<TextBox>().Any(text => text.Text.Contains("Preferência explícita preservada")));
                Phase("refresh concluído");
                Assert.Null(panel.AudioControls.Selection.DeviceId);
                Assert.Contains(Descendants(panel).OfType<Label>(), label => label.Text.Contains("Salvo — volume 25"));
                Assert.False(Descendants(form).OfType<CheckBox>().Single(check => check.Text == "Seguir padrão multimídia de reprodução").Checked);
                Assert.True(Descendants(form).OfType<CheckBox>().Single(check => check.Text == "Seguir padrão multimídia de captura").Checked);
                Assert.Contains(form.Bounds, Screen.AllScreens.Select(screen => Rectangle.Intersect(screen.WorkingArea, form.Bounds)));
                Phase("Close");
                form.Close();
                await PumpUntilAsync(() => form.IsDisposed);
                Phase("fechamento concluído");
            });
            Assert.Empty(warnings);
        }
        var loaded = await new SettingsStore(directory.FilePath).LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(settings.Playback, loaded.Settings.Playback);
        Assert.Equal(settings.Capture, loaded.Settings.Capture);
        Assert.Equal(settings.Endpoints, loaded.Settings.Endpoints);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":99,\"future\":true}")]
    [InlineData("corrupt original")]
    [Trait("Category", "WindowsIntegration")]
    public Task MainWindowClosePreservesProtectedSettingsByteForByte(string original) => OnSta(async () =>
    {
        using var directory = new TemporarySettingsDirectory();
        File.WriteAllText(directory.FilePath, original);
        File.WriteAllBytes(directory.FilePath + ".bak", SettingsJson.Encode(new StudioSettings()));
        byte[] bytes = File.ReadAllBytes(directory.FilePath);
        var warnings = new List<string>();
        using var form = new MainForm(new SettingsStore(directory.FilePath), warnings.Add) { ShowInTaskbar = false, Opacity = 0 };
        form.Show();
        var status = Descendants(form).OfType<Label>().Single(label => label.AccessibleName?.StartsWith("Estado do armazenamento de preferências", StringComparison.Ordinal) == true);
        await PumpUntilAsync(() => status.Text.Length > 0 && form.StartupTask.IsCompleted);
        form.StartupTask.GetAwaiter().GetResult();
        Assert.Contains("preservad", status.Text, StringComparison.OrdinalIgnoreCase);
        form.Close();
        await PumpUntilAsync(() => form.IsDisposed);
        Assert.Equal(bytes, File.ReadAllBytes(directory.FilePath));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
        Assert.Empty(warnings);
    });

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public Task PredictableSaveFailureReportsWarningAndClosesPreservingValidFile() => OnSta(async () =>
    {
        using var directory = new TemporarySettingsDirectory();
        byte[] original = SettingsJson.Encode(new StudioSettings());
        File.WriteAllBytes(directory.FilePath, original);
        var warnings = new List<string>();
        var store = new SettingsStore(directory.FilePath, new TestWriter(() => throw new JsonException("Validação simulada durante gravação.")));
        using var form = new MainForm(store, warnings.Add) { ShowInTaskbar = false, Opacity = 0 };
        form.Show();
        var status = Descendants(form).OfType<Label>().Single(label => label.AccessibleName?.StartsWith("Estado do armazenamento de preferências", StringComparison.Ordinal) == true);
        await PumpUntilAsync(() => status.Text.Length > 0 && form.StartupTask.IsCompleted);
        form.Close();
        await PumpUntilAsync(() => form.IsDisposed);
        Assert.Single(warnings);
        Assert.Contains("preservada", warnings[0]);
        Assert.Equal(original, File.ReadAllBytes(directory.FilePath));
    });

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task SelectionIsDisabledUntilLoadAndCloseCancelsPendingLoad()
    {
        using var directory = new TemporarySettingsDirectory();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new SettingsStore(directory.FilePath, new TestWriter(() =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException();
        }));
        await store.LoadAsync(TestContext.Current.CancellationToken);
        var persisted = new StudioSettings { Capture = new(false, "explicit-before-load") };
        var writing = store.SaveAsync(persisted, TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await OnSta(async () =>
            {
                var warnings = new List<string>();
                using var form = new MainForm(store, warnings.Add) { ShowInTaskbar = false, Opacity = 0 };
                var checks = Descendants(form).OfType<CheckBox>().ToArray();
                Assert.All(checks, check => Assert.False(check.Enabled));
                Assert.All(Descendants(form).OfType<DevicePanel>(), panel => Assert.False(panel.Enabled));
                checks[0].Checked = false;
                form.Show();
                await Task.Delay(5, TestContext.Current.CancellationToken);
                Assert.All(checks, check => Assert.False(check.Enabled));
                form.Close();
                await PumpUntilAsync(() => form.IsDisposed);
                Assert.Empty(warnings);
            });
        }
        finally { release.Set(); }
        Assert.True((await writing).Success);
        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(persisted.Playback, loaded.Settings.Playback);
        Assert.Equal(persisted.Capture, loaded.Settings.Capture);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [Trait("Category", "WindowsIntegration")]
    public async Task InvalidOrMissingWorkAreasDoNotFaultStartupOrLoseSavedPlacement(int scenario)
    {
        using var directory = new TemporarySettingsDirectory();
        var savedWindow = scenario switch
        {
            3 or 4 => new WindowPlacement(90000, 90000, 2000, 1500),
            5 => new WindowPlacement(90000, 90000, 2000, 1500, true),
            6 => new WindowPlacement(-1200, 30, 900, 600),
            _ => new WindowPlacement(170, 160, 900, 600)
        };
        var settings = new StudioSettings { Window = savedWindow, Playback = new(false, "absent-window-test-output"),
            Endpoints = [new("absent-window-test-output", EndpointDirection.Playback, 25, true, 50)] };
        byte[] original = SettingsJson.Encode(settings);
        File.WriteAllBytes(directory.FilePath, original);
        var warnings = new List<string>();
        await OnSta(async () =>
        {
            WindowArea[] areas = scenario switch
            {
                0 => [],
                1 => [new(0, 0, 0, 0)],
                2 => [new(0, 0, 0, 0), new(0, 0, 1600, 900)],
                3 => [new(0, 0, -10, 900), new(-1280, 0, 1280, 800)],
                4 => [new(0, 0, 400, 300)],
                5 => [new(0, 0, 0, -1)],
                _ => [new(0, 0, 1024, 768)]
            };
            using var form = new MainForm(new SettingsStore(directory.FilePath), warnings.Add, workAreas: () => areas)
                { ShowInTaskbar = false, Opacity = 0 };
            form.Show();
            await PumpUntilAsync(() => form.StartupTask.IsCompleted);
            form.StartupTask.GetAwaiter().GetResult();
            await PumpUntilAsync(() => Descendants(form).OfType<CheckBox>().All(check => check.Enabled)
                && Descendants(form).OfType<DevicePanel>().All(panel => panel.Enabled));
            Assert.All(Descendants(form).OfType<DevicePanel>(), panel => Assert.True(panel.Enabled));
            Assert.Equal(FormWindowState.Normal, form.WindowState); // No maximization using fictitious area.
            if (scenario is 0 or 1 or 5)
                Assert.Contains(Descendants(form).OfType<Label>(), label => label.Text.Contains("Área útil da tela indisponível"));
            else
            {
                var valid = areas.Where(area => area.Width > 0 && area.Height > 0).Single();
                Assert.True(new Rectangle(valid.X, valid.Y, valid.Width, valid.Height).Contains(form.Bounds));
            }
            form.Close();
            await PumpUntilAsync(() => form.IsDisposed);
        });
        Assert.Empty(warnings);
        if (scenario is 0 or 1 or 2 or 5) Assert.Equal(original, File.ReadAllBytes(directory.FilePath));
        var loaded = await new SettingsStore(directory.FilePath).LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(settings.Playback, loaded.Settings.Playback);
        Assert.Equal(settings.Endpoints, loaded.Settings.Endpoints);
        if (scenario is 0 or 1 or 5) Assert.Equal(savedWindow, loaded.Settings.Window);
    }
}
