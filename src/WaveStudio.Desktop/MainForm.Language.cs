using System.Globalization;
using WaveStudio.Desktop.Settings;

namespace WaveStudio.Desktop;

internal sealed partial class MainForm
{
    private readonly ComboBox language = new() { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
    private readonly Label languageStatus = new() { AutoSize = true };
    private readonly CultureInfo windowsUiCulture;
    private readonly SettingsLoadResult? initialSettings;
    private readonly CultureInfo openingLanguage = StudioLanguage.Resolve(CultureInfo.CurrentUICulture.Name, CultureInfo.CurrentUICulture);
    private bool bindingLanguage;

    private void AddLanguageControls(FlowLayoutPanel layout)
    {
        string title = StudioStrings.Get("Language.Title");
        layout.Controls.Add(new Label { AutoSize = true, Text = StudioStrings.Get("Language.FocusLabel"), AccessibleName = title });
        language.AccessibleName = title;
        language.AccessibleDescription = StudioStrings.Get("Language.Hint");
        language.Items.Add(new LanguageOption(StudioLanguage.FollowWindows, StudioStrings.Get("Language.FollowWindows")));
        foreach (var culture in StudioLanguage.SupportedCultures)
            language.Items.Add(new LanguageOption(culture.Name, culture.NativeName));
        language.DropDownWidth = Math.Max(language.Width, language.Items.Cast<LanguageOption>()
            .Max(option => TextRenderer.MeasureText(option.Caption, language.Font).Width + 30));
        layout.Controls.Add(language);
        layout.Controls.Add(languageStatus);
        language.SelectedIndexChanged += (_, _) =>
        {
            if (bindingLanguage || !settingsLoaded || stopping || preferenceOperation || !store.CanSave ||
                language.SelectedItem is not LanguageOption option || option.Preference == settings.Language) return;
            settings = settings with { Language = option.Preference };
            ShowLanguagePreference();
            QueueSettingsSave();
        };
    }

    private void ShowLanguagePreference()
    {
        var available = StudioLanguage.FindAvailable(settings.Language, windowsUiCulture);
        var wanted = available ?? StudioStrings.NeutralCulture;
        bindingLanguage = true;
        try
        {
            var options = language.Items.Cast<LanguageOption>();
            language.SelectedItem = options.FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Preference, settings.Language))
                ?? options.First(item => item.Preference == StudioLanguage.PortugueseBrazil);
            string status = StudioStrings.Format("Language.Active", openingLanguage.NativeName);
            if (available is null)
                status += " " + StudioStrings.Format("Language.Fallback", wanted.NativeName);
            if (!wanted.Equals(openingLanguage))
                status += " " + StudioStrings.Format("Language.NextOpening", wanted.NativeName);
            languageStatus.Text = status;
            languageStatus.AccessibleName = status;
        }
        finally { bindingLanguage = false; }
    }

    private sealed record LanguageOption(string Preference, string Caption)
    {
        public override string ToString() => Caption;
    }
}
