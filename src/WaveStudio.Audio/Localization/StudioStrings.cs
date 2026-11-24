using System.Globalization;
using System.Resources;

namespace WaveStudio.Audio.Localization;

internal static class StudioStrings
{
    internal static CultureInfo NeutralCulture { get; } = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly ResourceManager Resources = new("WaveStudio.Audio.Localization.StudioStrings", typeof(StudioStrings).Assembly);

    internal static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new MissingManifestResourceException(key);

    internal static string Format(string key, params object?[] values) => string.Format(CultureInfo.CurrentCulture, Get(key), values);
}
