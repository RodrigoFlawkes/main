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
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
        ApplicationConfiguration.Initialize();
        if (args.Length != 0 && (args.Length != 2 || args[0] != "--settings-file" || !Path.IsPathFullyQualified(args[1])))
        {
            MessageBox.Show("Use --settings-file seguido de um caminho absoluto para armazenamento isolado.", "Wave Studio");
            return;
        }
        SettingsStore store;
        try { store = new SettingsStore(args.Length == 2 ? args[1] : SettingsStore.DefaultPath); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or InvalidOperationException)
        {
            MessageBox.Show("Não foi possível definir o arquivo de preferências. " + error.Message, "Wave Studio");
            return;
        }
        Application.Run(new MainForm(store));
    }
}
