using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WaveStudio.Audio;
using WaveStudio.Audio.Localization;
using WaveStudio.Desktop;
using WaveStudio.Desktop.Settings;
using Xunit;

namespace WaveStudio.Tests;

public sealed class StudioLanguageTests
{
    [Theory]
    [InlineData("en-US", "en-US")]
    [InlineData("en-GB", "en-US")]
    [InlineData("en", "en-US")]
    [InlineData("es-ES", "es-ES")]
    [InlineData("es-MX", "es-ES")]
    [InlineData("es-419", "es-ES")]
    [InlineData("es", "es-ES")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt-PT", null)]
    [InlineData("fr-CA", null)]
    [InlineData("", null)]
    public void FollowWindowsUsesOnlyVerifiedLanguages(string windowsName, string? available)
    {
        var windows = CultureInfo.GetCultureInfo(windowsName);
        Assert.Equal(available, StudioLanguage.FindAvailable("windows", windows)?.Name);
        Assert.Equal(available ?? "pt-BR", StudioLanguage.Resolve("windows", windows).Name);
    }

    [Theory]
    [InlineData("PT-br", "pt-BR")]
    [InlineData("EN-us", "en-US")]
    [InlineData("es-ES", "es-ES")]
    [InlineData("en-GB", null)]
    [InlineData("es", null)]
    [InlineData("pt-PT", null)]
    [InlineData("zz-ZZ", null)]
    public void ManualChoiceHonorsOnlyExactPublishedCultures(string choice, string? available)
    {
        var windows = CultureInfo.GetCultureInfo("es-MX");
        Assert.Equal(available, StudioLanguage.FindAvailable(choice, windows)?.Name);
        Assert.Equal(available ?? "pt-BR", StudioLanguage.Resolve(choice, windows).Name);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    public void SatelliteContainsEachNeutralResourceWithIdenticalCompositePlaceholders(string cultureName)
    {
        var neutral = ReadCatalog(CultureInfo.InvariantCulture);
        var translated = ReadCatalog(CultureInfo.GetCultureInfo(cultureName));
        Assert.NotEmpty(neutral);
        Assert.Equal(neutral.Keys.Order(StringComparer.Ordinal), translated.Keys.Order(StringComparer.Ordinal));
        foreach (var (key, text) in neutral)
        {
            var translation = translated[key];
            Assert.False(string.IsNullOrWhiteSpace(translation), $"{cultureName}: {key}");
            var expected = Placeholders(text);
            var actual = Placeholders(translation);
            Assert.True(expected.SequenceEqual(actual), $"{cultureName}: {key}: [{string.Join(", ", expected)}] ≠ [{string.Join(", ", actual)}]");
            Assert.Equal(text.TakeWhile(char.IsWhiteSpace), translation.TakeWhile(char.IsWhiteSpace));
            Assert.Equal(text.Reverse().TakeWhile(char.IsWhiteSpace), translation.Reverse().TakeWhile(char.IsWhiteSpace));
        }
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    [InlineData("windows")]
    [InlineData("zz-ZZ")]
    public async Task SchemaThreePersistsTheChoiceAndNumbersAcrossWindowsRegions(string choice)
    {
        using var directory = new TemporarySettingsDirectory();
        var original = new StudioSettings
        {
            Language = choice,
            Playback = new(false, "Endpoint Démo"),
            Endpoints = [new("Endpoint Démo", EndpointDirection.Playback, 12.5, false, -25.5)]
        };
        var previousNumberCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var store = new SettingsStore(directory.FilePath);
            var token = TestContext.Current.CancellationToken;
            await store.LoadAsync(token);
            Assert.True((await store.SaveAsync(original, token)).Success);

            CultureInfo.CurrentUICulture = StudioLanguage.Resolve(choice, CultureInfo.GetCultureInfo("es-MX"));
            Assert.Equal("de-DE", CultureInfo.CurrentCulture.Name);
            Assert.Equal("12,5%", StudioStrings.Format("Mixer.Percent", 12.5));
            byte[] bytes = File.ReadAllBytes(directory.FilePath);
            using var payload = JsonDocument.Parse(bytes);
            var root = payload.RootElement;
            Assert.Equal(3, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(choice, root.GetProperty("language").GetString());
            Assert.Equal("12.5", root.GetProperty("endpoints")[0].GetProperty("volumePercent").GetRawText());
            var reread = (await new SettingsStore(directory.FilePath).LoadAsync(token)).Settings;
            Assert.Equal(original.Language, reread.Language);
            Assert.Equal(original.Playback, reread.Playback);
            Assert.Equal(original.Endpoints, reread.Endpoints);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousNumberCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [InlineData("pt-BR", "Configurações", "Idioma", 'I')]
    [InlineData("en-US", "Settings", "Language", 'L')]
    [InlineData("es-ES", "Configuración", "Idioma", 'I')]
    [Trait("Category", "WindowsIntegration")]
    public Task CompactSettingsWindowUsesPersistedLanguageAndReachableKeyboardControls(
        string cultureName, string settingsCaption, string languageCaption, char mnemonic) =>
        PreferencesUiTests.OnSta(async () =>
        {
            using var directory = new TemporarySettingsDirectory();
            File.WriteAllBytes(directory.FilePath, SettingsJson.Encode(new StudioSettings
            {
                Language = cultureName,
                Window = new(10, 10, 640, 460)
            }));
            var previousUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = StudioStrings.NeutralCulture;
                var store = new SettingsStore(directory.FilePath);
                var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);
                var windows = CultureInfo.GetCultureInfo("fr-CA");
                CultureInfo.CurrentUICulture = StudioLanguage.Resolve(loaded.Settings.Language, windows);
                var source = new MicrophoneTestTests.FakeBackend();
                using var form = new MainForm(store, _ => { }, microphoneSession: new MicrophoneTestSession(source),
                    workAreas: () => [new(0, 0, 700, 500)], initialSettings: loaded, windowsUiCulture: windows)
                    { ShowInTaskbar = false, Opacity = 0 };
                form.Show();
                try
                {
                    var tabs = PreferencesUiTests.Descendants(form).OfType<TabControl>().First();
                    var preferences = Assert.Single(tabs.TabPages.Cast<TabPage>(), tab => tab.Text == settingsCaption);
                    tabs.SelectedTab = preferences;
                    var controls = PreferencesUiTests.Descendants(preferences).ToArray();
                    var language = Assert.Single(controls.OfType<ComboBox>());
                    await PreferencesUiTests.PumpUntilAsync(() => language.Enabled);
                    Assert.Equal(4, language.Items.Count);
                    Assert.Equal(CultureInfo.CurrentUICulture.NativeName, language.Text);
                    Assert.Contains(PreferencesUiTests.Descendants(form).OfType<Label>(),
                        label => label.Text == StudioStrings.Get("SettingsStore.PreferenciasCarregadasOsNiveisAtuais"));
                    Assert.Equal(languageCaption, language.AccessibleName);

                    var title = Assert.Single(controls.OfType<Label>(), label => Control.IsMnemonic(mnemonic, label.Text));
                    Assert.True(form.SelectNextControl(title, forward: true, tabStopOnly: true, nested: true, wrap: false));
                    Assert.Same(language, form.ActiveControl);
                    var other = cultureName == "pt-BR" ? CultureInfo.GetCultureInfo("en-US") : StudioStrings.NeutralCulture;
                    language.SelectedIndex = cultureName == "pt-BR" ? 2 : 0;
                    var nextOpening = StudioStrings.Format("Language.NextOpening", other.NativeName);
                    var status = Assert.Single(controls.OfType<Label>(), label => label.Text.Contains(nextOpening, StringComparison.Ordinal));
                    Assert.Contains(StudioStrings.Format("Language.Active", CultureInfo.CurrentUICulture.NativeName), status.Text, StringComparison.Ordinal);
                    Assert.Equal(status.Text, status.AccessibleName);
                    Assert.InRange(status.Right, 1, preferences.ClientSize.Width);
                    Assert.True(status.Height >= status.GetPreferredSize(new Size(status.Width, 0)).Height);
                    Assert.False(preferences.HorizontalScroll.Visible);
                    foreach (var item in language.Items)
                        Assert.InRange(TextRenderer.MeasureText(item.ToString()!, language.Font).Width, 1, language.DropDownWidth - 20);
                    Assert.Equal(cultureName, CultureInfo.CurrentUICulture.Name);
                    Assert.Empty(source.Calls);
                }
                finally { form.Close(); await PreferencesUiTests.PumpUntilAsync(() => form.IsDisposed); }
                var result = await new SettingsStore(directory.FilePath).LoadAsync(TestContext.Current.CancellationToken);
                Assert.Equal(cultureName == "pt-BR" ? "en-US" : "windows", result.Settings.Language);
            }
            finally { CultureInfo.CurrentUICulture = previousUiCulture; }
        });

    private static Dictionary<string, string> ReadCatalog(CultureInfo culture)
    {
        const string baseName = "WaveStudio.Audio.Localization.StudioStrings";
        var manager = new ResourceManager(baseName, typeof(StudioStrings).Assembly);
        try
        {
            var resources = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
            Assert.NotNull(resources);
            return resources.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key,
                entry => Assert.IsType<string>(entry.Value), StringComparer.Ordinal);
        }
        finally { manager.ReleaseAllResources(); }
    }

    private static (string Index, string? Alignment, string? Format)[] Placeholders(string template)
    {
        _ = CompositeFormat.Parse(template);
        return Regex.Matches(template, @"\{\{|\}\}|\{([0-9]+) *(?:, *(-?[0-9]+) *)?(?::([^{}]*))?\}")
            .Where(match => match.Groups[1].Success)
            .Select(match => (match.Groups[1].Value, ReadGroup(match, 2), ReadGroup(match, 3)))
            .ToArray();

        static string? ReadGroup(Match match, int index) => match.Groups[index].Success ? match.Groups[index].Value : null;
    }
}
