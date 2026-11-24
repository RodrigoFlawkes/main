using System.Globalization;
using System.Text;
using WaveStudio.Audio;
using WaveStudio.Audio.Localization;
using WaveStudio.Desktop;
using WaveStudio.Desktop.Settings;
using Xunit;

namespace WaveStudio.Tests;

public sealed class MixerAndLanguageTests
{
    [Fact]
    public void ResolvesOnlyCompleteSupportedCulturesAndKeepsNumberCultureSeparate()
    {
        var american = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal("en-US", StudioLanguage.Resolve(StudioLanguage.FollowWindows, american).Name);
        Assert.Equal("es-ES", StudioLanguage.Resolve("es-ES", american).Name);
        Assert.Equal("pt-BR", StudioLanguage.Resolve("pt-BR", american).Name);
        Assert.Equal(["pt-BR", "en-US", "es-ES"], StudioLanguage.SupportedCultures.Select(culture => culture.Name));
        Assert.Equal("pt-BR", StudioStrings.NeutralCulture.Name);
        var previousNumberCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = american;
            CultureInfo.CurrentUICulture = StudioStrings.NeutralCulture;
            Assert.Equal("12.5%", StudioStrings.Format("Mixer.Percent", 12.5));
            Assert.Equal("Idioma", StudioStrings.Get("Language.Title"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousNumberCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public void MigratesLanguageFromV1AndV2AndRoundTripsAChoiceWithoutChangingAudioPreferences()
    {
        string legacy = """{"schemaVersion":1,"playback":{"followDefault":false,"endpointId":"out"}}""";
        var v1 = SettingsJson.Decode(Encoding.UTF8.GetBytes(legacy));
        Assert.Equal(3, v1.SchemaVersion);
        Assert.Equal("pt-BR", v1.Language);
        var v2Object = System.Text.Json.Nodes.JsonNode.Parse(SettingsJson.Encode(new StudioSettings()))!.AsObject();
        v2Object["schemaVersion"] = 2;
        v2Object.Remove("language");
        var v2 = SettingsJson.Decode(Encoding.UTF8.GetBytes(v2Object.ToJsonString()));
        Assert.Equal("pt-BR", v2.Language);
        var saved = SettingsJson.Decode(SettingsJson.Encode(v1 with { Language = StudioLanguage.FollowWindows }));
        Assert.Equal(StudioLanguage.FollowWindows, saved.Language);
        Assert.Equal(v1.Playback, saved.Playback);
        Assert.Throws<NotSupportedException>(() => SettingsJson.Decode(Encoding.UTF8.GetBytes("""{"schemaVersion":4}""")));
    }

    [Fact]
    public Task FaderKeepsTheGestureDuringRefreshAndWritesOnlyTheFinalValue() => PreferencesUiTests.OnSta(async () =>
    {
        using var fader = new MixerFader();
        var writes = new List<int>();
        fader.Committed += writes.Add;
        fader.ShowVolume(38.5);
        Assert.Equal(39, fader.Slider.Value);
        fader.BeginGesture();
        fader.Slider.Value = 42;
        fader.Slider.Value = 73;
        fader.ShowVolume(19);
        Assert.Equal(73, fader.Slider.Value);
        Assert.Empty(writes);
        fader.CommitGesture();
        Assert.Equal([73], writes);
        fader.ShowVolume(72.95);
        Assert.Equal(73, fader.Slider.Value);
        fader.BeginGesture();
        fader.Slider.Value = 100;
        fader.CancelEdit();
        fader.CommitGesture();
        Assert.Equal([73], writes);
        await Task.CompletedTask;
    });

    [Fact]
    public Task BoardWritesOnlyTheChosenSessionAndKeepsDeviceMasterIndependent() => PreferencesUiTests.OnSta(async () =>
    {
        using var host = new Form { ClientSize = new Size(950, 650), ShowInTaskbar = false, Opacity = 0 };
        var first = new FakeSession("first");
        var second = new FakeSession("second");
        var deviceMaster = new FakeEndpointState();
        using var panel = new ApplicationMixerPanel(new ApplicationSessionService(id => new FakeSessions(id == "out" ? [first, second] : [])),
            new EndpointVolumeSession(_ => new FakeEndpointVolume(deviceMaster)));
        host.Controls.Add(panel);
        host.Show();
        try
        {
            var device = new DeviceSnapshot("out", "Fones", EndpointDirection.Playback, EndpointState.Active, false, false, null, null);
            panel.SelectDevice(device);
            await panel.PendingRead;
            var controls = PreferencesUiTests.Descendants(panel).OfType<MixerStrip>().ToArray();
            var chosen = Assert.Single(controls, item => item.Binding?.Snapshot.Key == second.Key);
            var other = Assert.Single(controls, item => item.Binding?.Snapshot.Key == first.Key);
            var master = Assert.Single(controls, item => item.Binding is null);
            Assert.Equal(40, master.Fader.Slider.Value);
            Assert.Equal(25, chosen.Controls.OfType<ProgressBar>().Single().Value);

            chosen.Fader.BeginGesture();
            chosen.Fader.Slider.Value = 72;
            Assert.Single(panel.Controls[0].Controls.OfType<Button>()).PerformClick();
            await panel.PendingRead;
            Assert.Equal(72, chosen.Fader.Slider.Value);
            Assert.Equal(0, first.Writes + second.Writes + deviceMaster.Writes);
            chosen.Fader.CommitGesture();
            await PreferencesUiTests.PumpUntilAsync(() => second.Writes == 1 && panel.MixerViews.Enabled);
            await panel.PendingRead;
            Assert.Equal(.72, second.VolumeScalar);
            chosen.Mute.PerformClick();
            await PreferencesUiTests.PumpUntilAsync(() => second.Writes == 2 && panel.MixerViews.Enabled);
            await panel.PendingRead;
            Assert.True(second.Muted);
            Assert.False(first.Muted);
            Assert.Equal(0, first.Writes + deviceMaster.Writes);

            master.Fader.BeginGesture();
            master.Fader.Slider.Value = 30;
            master.Fader.CommitGesture();
            await PreferencesUiTests.PumpUntilAsync(() => deviceMaster.Writes == 1 && panel.MixerViews.Enabled);
            await panel.PendingRead;
            Assert.Equal(.30, deviceMaster.Volume);
            Assert.Equal(.72, second.VolumeScalar);

            chosen.Controls.OfType<ComboBox>().Single().SelectedIndex = (int)MixerGroup.Music;
            var filter = Assert.Single(PreferencesUiTests.Descendants(panel).OfType<ComboBox>(),
                control => control.AccessibleName == StudioStrings.Get("Mixer.GroupFilter"));
            filter.SelectedIndex = (int)MixerGroup.Music + 1;
            Assert.True(chosen.Visible);
            Assert.False(other.Visible);
            Assert.True(master.Visible);

            chosen.Fader.BeginGesture();
            chosen.Fader.Slider.Value = 91;
            panel.SelectDevice(device with { Id = "other" });
            await panel.PendingRead;
            Assert.True(chosen.IsDisposed);
            Assert.Equal(2, second.Writes);
        }
        finally { await panel.StopAsync(); }
    });

    [Fact]
    public Task SettingsPagePersistsFollowWindowsAndDisplaysItsFallback() => PreferencesUiTests.OnSta(async () =>
    {
        using var directory = new TemporarySettingsDirectory();
        var source = new MicrophoneTestTests.FakeBackend();
        using var form = new MainForm(new SettingsStore(directory.FilePath), _ => { },
            microphoneSession: new MicrophoneTestSession(source), windowsUiCulture: CultureInfo.GetCultureInfo("fr-CA"))
            { ShowInTaskbar = false, Opacity = 0 };
        form.Show();
        try
        {
            await form.StartupTask;
            var tabs = form.Controls[0].Controls.OfType<TabControl>().Single();
            tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(tab => tab.Text == "Configurações");
            var language = Assert.Single(PreferencesUiTests.Descendants(tabs.SelectedTab!).OfType<ComboBox>(),
                control => control.AccessibleName == "Idioma");
            await PreferencesUiTests.PumpUntilAsync(() => language.Enabled);
            Assert.Equal(4, language.Items.Count);
            Assert.Equal(1, language.SelectedIndex);
            Assert.True(language.Enabled);
            language.SelectedIndex = 0;
            await PreferencesUiTests.PumpUntilAsync(() => File.Exists(directory.FilePath));
            var saved = SettingsJson.Decode(File.ReadAllBytes(directory.FilePath));
            Assert.Equal(StudioLanguage.FollowWindows, saved.Language);
            Assert.Contains(PreferencesUiTests.Descendants(tabs.SelectedTab!).OfType<Label>(),
                label => label.Text.Contains("ainda não tem tradução completa"));
            Assert.Empty(source.Calls);
        }
        finally { form.Close(); await PreferencesUiTests.PumpUntilAsync(() => form.IsDisposed); }
    });

    [Fact]
    public async Task DuplicateIdentityIsHiddenAndCannotReceiveWrites()
    {
        var first = new FakeSession("shared");
        var second = new FakeSession("shared");
        await using var service = new ApplicationSessionService(_ => new FakeSessions([first, second]));
        var target = service.Select("out");
        var catalog = await service.ReadAsync(target);
        Assert.Empty(catalog.Sessions);
        Assert.Single(catalog.Warnings);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(target, first.Key, new SetSessionVolume(1)));
        Assert.Equal(0, first.Writes + second.Writes);
    }

    private sealed class FakeSessions(FakeSession[] sessions) : IApplicationSessionEndpoint
    {
        public int Count => sessions.Length;
        public IApplicationSessionAccess Open(int index) => sessions[index];
        public void Dispose() { }
    }

    private sealed class FakeSession(string id) : IApplicationSessionAccess
    {
        private double volume = .35;
        private bool muted;
        public int Writes { get; private set; }
        public SessionKey Key { get; } = new("out", id);
        public SessionActivity Activity => SessionActivity.Active;
        public double VolumeScalar { get => volume; set { volume = value; Writes++; } }
        public bool Muted { get => muted; set { muted = value; Writes++; } }

        public ApplicationSessionSnapshot Read() => new(Key, "Aplicativo", 5, false, Activity,
            VolumeScalar * 100, Muted, null, PeakReading.FromScalar(.25), System.Diagnostics.Stopwatch.GetTimestamp());
        public void Dispose() { }
    }
}
