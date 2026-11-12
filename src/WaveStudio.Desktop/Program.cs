using System.Globalization;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("WaveStudio.Tests")]

namespace WaveStudio.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
