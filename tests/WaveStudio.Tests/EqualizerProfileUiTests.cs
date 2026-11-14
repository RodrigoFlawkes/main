using WaveStudio.Audio;
using WaveStudio.Desktop;
using WaveStudio.Desktop.Settings;
using Xunit;
using static WaveStudio.Tests.PreferencesUiTests;

namespace WaveStudio.Tests;

public sealed class EqualizerProfileUiTests
{
    [Fact]
    public Task BindingCrudAndDiscardConfirmationNeverStartAudioOrOverwriteProfileImplicitly() => OnSta(async () =>
    {
        bool approve = false; int confirmations = 0, notifications = 0;
        using var host = new Form { ShowInTaskbar = false, Opacity = 0 };
        using var eq = new EqualizerPreviewPanel(_ => { confirmations++; return approve; });
        host.Controls.Add(eq); host.Show(); eq.SetAvailability(true, false);
        eq.PreferencesChanged += (_, _) => notifications++;
        eq.BeginLoad(); Assert.False(eq.Enabled);
        eq.LoadPreferences(new(), true); Assert.Equal(0, notifications); Assert.False(eq.Activate.Checked);
        eq.Activate.Checked = true; eq.Bands[5].Value = -9; eq.Preamp.Value = -2.5m;
        eq.ProfileName.Text = "Voz"; eq.CreateProfile.PerformClick();
        var id = eq.Preferences.SelectedProfileId; Assert.NotNull(id); Assert.Single(eq.Preferences.Profiles);
        eq.Bands[5].Value = -3; Assert.True(eq.Preferences.HasUnsavedChanges()); Assert.Contains("diferentes", eq.ProfileStatus);
        Assert.Equal(-9, eq.Preferences.Profiles.Single().Values.GainsDb[5]);
        eq.ProfileList.SelectedIndex = 0; Assert.Equal(id, eq.Preferences.SelectedProfileId); Assert.Equal(-3, eq.Bands[5].Value);
        eq.SaveProfile.PerformClick(); Assert.False(eq.Preferences.HasUnsavedChanges()); Assert.Equal(id, eq.Preferences.SelectedProfileId);
        eq.ProfileName.Text = "Voz natural"; eq.RenameProfile.PerformClick(); Assert.Equal("Voz natural", eq.Preferences.Profiles.Single().Name);
        eq.DeleteProfile.PerformClick(); Assert.Single(eq.Preferences.Profiles);
        approve = true; eq.DeleteProfile.PerformClick(); Assert.Empty(eq.Preferences.Profiles); Assert.Equal(-3, eq.Bands[5].Value);
        eq.ProfileList.SelectedIndex = 0; // Already Padrão after delete, so select another profile first.
        eq.ProfileName.Text = "Teste"; eq.CreateProfile.PerformClick(); eq.Bands[0].Value = 2; eq.ProfileList.SelectedIndex = 0;
        Assert.False(eq.Activate.Checked); Assert.All(eq.Bands, band => Assert.Equal(0, band.Value));
        Assert.True(confirmations >= 4); await Task.CompletedTask;
    });
    [Fact]
    public Task ProfilesFreezeTogetherWithEqDuringCaptureAndPlayback() => MicrophoneTestUiTests.OnSta((panel, backend) =>
    {
        panel.Equalizer.ProfileName.Text = "Voz"; panel.Equalizer.CreateProfile.PerformClick();
        panel.RecordButton.PerformClick(); MicrophoneTestUiTests.Pump(() => panel.Snapshot.State == MicrophoneTestState.Recording);
        Assert.False(panel.Equalizer.ProfileList.Enabled); Assert.False(panel.Equalizer.CreateProfile.Enabled); Assert.False(panel.Equalizer.SaveProfile.Enabled);
        backend.Capture.Emit(400); backend.Capture.RequestStop(); MicrophoneTestUiTests.Pump(() => panel.PlayButton.Enabled && panel.Equalizer.Enabled);
        panel.PlayButton.PerformClick(); MicrophoneTestUiTests.Pump(() => panel.Snapshot.State == MicrophoneTestState.Playing);
        Assert.False(panel.Equalizer.RenameProfile.Enabled); Assert.False(panel.Equalizer.DeleteProfile.Enabled);
    });
    [Fact]
    public Task IncompatibleFormatKeepsSavedParametersWithoutEnablingEqOrWritingByBinding() => OnSta(async () =>
    {
        using var eq = new EqualizerPreviewPanel(_ => false); int changed = 0;
        eq.PreferencesChanged += (_, _) => changed++; eq.SetAvailability(false, false);
        var saved = EqualizerProfileSettingsTests.Named(); eq.LoadPreferences(saved, true);
        Assert.Equal(0, changed); Assert.True(eq.Activate.Checked); Assert.False(eq.Activate.Enabled); Assert.False(eq.Bands[5].Enabled);
        Assert.Equal(-9, eq.Bands[5].Value); Assert.True(eq.Preferences.SameAs(saved)); await Task.CompletedTask;
    });
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task TwoStartupsKeepDraftSelectedProfileAndActivationButNoAudioOrClip()
    {
        using var directory = new TemporarySettingsDirectory(); string? id = null;
        for (int run = 0; run < 2; run++)
        {
            int index = run;
            await OnSta(async () =>
            {
                var backend = new MicrophoneTestTests.FakeBackend(); var session = new MicrophoneTestSession(backend);
                using var form = NewForm(directory.FilePath, session); form.Show(); await Ready(form);
                var panel = Descendants(form).OfType<MicrophoneTestPanel>().Single(); var eq = panel.Equalizer;
                Assert.Equal(20, panel.TestLevel.Value); Assert.Equal(TimeSpan.Zero, panel.Snapshot.RecordedDuration); Assert.Empty(backend.Calls);
                if (index == 0)
                {
                    Assert.False(eq.Activate.Checked); Assert.All(eq.Bands, band => Assert.Equal(0, band.Value));
                    eq.SetAvailability(true, false); eq.Activate.Checked = true; eq.Bands[5].Value = -9; eq.Preamp.Value = -2.5m;
                    eq.ProfileName.Text = "Voz"; eq.CreateProfile.PerformClick(); id = eq.Preferences.SelectedProfileId;
                    eq.Bands[5].Value = -6; // Draft must survive closing, without replacing the -9 dB named profile.
                }
                else
                {
                    Assert.True(eq.Activate.Checked); Assert.Equal(-6, eq.Bands[5].Value); Assert.Equal(-2.5m, eq.Preamp.Value);
                    Assert.Equal(id, eq.Preferences.SelectedProfileId); Assert.Equal(-9, eq.Preferences.Profiles.Single().Values.GainsDb[5]);
                    Assert.True(eq.Preferences.HasUnsavedChanges()); Assert.Contains("diferentes", eq.ProfileStatus);
                }
                form.Close(); await PumpUntilAsync(() => form.IsDisposed); Assert.Empty(backend.Calls);
            });
        }
        var loaded = await new SettingsStore(directory.FilePath).LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(id, loaded.Settings.Equalizer.SelectedProfileId); Assert.Equal(-6, loaded.Settings.Equalizer.Current.GainsDb[5]);
    }
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public Task DragIsConsolidatedAndClosingFlushesEvenWithoutMouseUp() => OnSta(async () =>
    {
        using var directory = new TemporarySettingsDirectory(); var writer = new CountingWriter();
        var backend = new MicrophoneTestTests.FakeBackend(); var session = new MicrophoneTestSession(backend);
        using var form = NewForm(directory.FilePath, session, writer); form.Show(); await Ready(form);
        var eq = Descendants(form).OfType<EqualizerPreviewPanel>().Single();
        eq.SetAvailability(true, false); eq.BeginAdjustment(); eq.Activate.Checked = true;
        eq.Bands[0].Value = 2; await Task.Delay(650, TestContext.Current.CancellationToken);
        eq.Bands[0].Value = 4; await Task.Delay(650, TestContext.Current.CancellationToken);
        Assert.Equal(0, writer.Writes); Assert.False(File.Exists(directory.FilePath));
        form.Close(); await PumpUntilAsync(() => form.IsDisposed);
        var saved = SettingsJson.Decode(File.ReadAllBytes(directory.FilePath));
        Assert.True(saved.Equalizer.Current.Enabled); Assert.Equal(4, saved.Equalizer.Current.GainsDb[0]); Assert.Empty(backend.Calls);
    });
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public Task FailedSaveIsVisibleAndExplicitRetryAndShutdownPreserveLatestValues() => OnSta(async () =>
    {
        using var directory = new TemporarySettingsDirectory(); var writer = new CountingWriter { Fail = true };
        var backend = new MicrophoneTestTests.FakeBackend(); var session = new MicrophoneTestSession(backend);
        using var form = NewForm(directory.FilePath, session, writer); form.Show(); await Ready(form);
        var eq = Descendants(form).OfType<EqualizerPreviewPanel>().Single();
        eq.SetAvailability(true, false); eq.Activate.Checked = true; eq.Bands[5].Value = -9;
        eq.ProfileName.Text = "Voz"; eq.CreateProfile.PerformClick();
        await PumpUntilAsync(() => eq.SaveStatus.Contains("Não foi possível salvar")); Assert.False(File.Exists(directory.FilePath));
        writer.Fail = false; eq.SaveProfile.PerformClick(); await PumpUntilAsync(() => eq.SaveStatus.StartsWith("Ajustes salvos"));
        eq.Bands[5].Value = -4; form.Close(); await PumpUntilAsync(() => form.IsDisposed);
        var saved = SettingsJson.Decode(File.ReadAllBytes(directory.FilePath)).Equalizer;
        Assert.Equal(-4, saved.Current.GainsDb[5]); Assert.Equal(-9, saved.Profiles.Single().Values.GainsDb[5]); Assert.Empty(backend.Calls);
    });
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public Task V1StartupAndFutureVersionKeepOriginalBytesUntilAnAuthorizedSave() => OnSta(async () =>
    {
        foreach (int version in new[] { 1, 3 })
        {
            using var directory = new TemporarySettingsDirectory(); byte[] original = System.Text.Encoding.UTF8.GetBytes($"{{\"schemaVersion\":{version}}}");
            File.WriteAllBytes(directory.FilePath, original); var backend = new MicrophoneTestTests.FakeBackend();
            using var form = NewForm(directory.FilePath, new MicrophoneTestSession(backend)); form.Show(); await Ready(form);
            var eq = Descendants(form).OfType<EqualizerPreviewPanel>().Single();
            Assert.Equal(original, File.ReadAllBytes(directory.FilePath)); Assert.False(eq.Activate.Checked);
            if (version == 3)
            {
                Assert.False(eq.CreateProfile.Enabled); eq.Activate.Checked = true; eq.Bands[0].Value = 2;
            }
            form.Close(); await PumpUntilAsync(() => form.IsDisposed);
            if (version == 3) Assert.Equal(original, File.ReadAllBytes(directory.FilePath));
            Assert.Empty(backend.Calls);
        }
    });
    private static MainForm NewForm(string path, MicrophoneTestSession session, ISettingsWriter? writer = null) =>
        new(new SettingsStore(path, writer), _ => { }, microphoneSession: session, workAreas: () => [new WindowArea(0, 0, 1920, 1080)])
        { ShowInTaskbar = false, Opacity = 0 };
    private static async Task Ready(MainForm form)
    {
        await PumpUntilAsync(() => Descendants(form).OfType<Label>().Any(label =>
            label.AccessibleName?.StartsWith("Estado do armazenamento de preferências", StringComparison.Ordinal) == true && label.Text.Length > 0));
        await form.StartupTask; await form.RefreshTask;
        var tabs = Descendants(form).OfType<TabControl>().Single(); tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "Testar microfone");
        await PumpUntilAsync(() => Descendants(form).OfType<EqualizerPreviewPanel>().Single().Enabled);
    }
    private sealed class CountingWriter : ISettingsWriter
    {
        private readonly SettingsWriter writer = new();
        public int Writes; public bool Fail;
        public void WriteNew(string path, byte[] bytes, CancellationToken token)
        { Interlocked.Increment(ref Writes); if (Fail) throw new IOException("escrita simulada"); writer.WriteNew(path, bytes, token); }
        public void Commit(string temporary, string target, bool replacing, string? backup) => writer.Commit(temporary, target, replacing, backup);
    }
}
