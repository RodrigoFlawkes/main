using System.Globalization;
using System.Runtime.CompilerServices;
using WaveStudio.Desktop.Settings;

[assembly: InternalsVisibleTo("WaveStudio.Tests")]

namespace WaveStudio.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        CultureInfo windowsUi = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = StudioStrings.NeutralCulture;
        if (args.Length != 0 && (args.Length != 2 || args[0] != "--settings-file" || !Path.IsPathFullyQualified(args[1])))
        {
            MessageBox.Show(StudioStrings.Get("Program.UseSettingsFileSeguidoDe"), "Wave Studio");
            return;
        }
        SettingsStore store;
        try { store = new SettingsStore(args.Length == 2 ? args[1] : SettingsStore.DefaultPath); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or InvalidOperationException)
        {
            MessageBox.Show(StudioStrings.Get("Program.NaoFoiPossivelDefinirO") + error.Message, "Wave Studio");
            return;
        }
        var loaded = store.LoadAsync().GetAwaiter().GetResult();
        var uiLanguage = StudioLanguage.Resolve(loaded.Settings.Language, windowsUi);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = uiLanguage;
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(store, initialSettings: loaded, windowsUiCulture: windowsUi));
    }
}
