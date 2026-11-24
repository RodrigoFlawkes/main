using System.Globalization;

namespace WaveStudio.Desktop.Settings;

internal static class StudioLanguage
{
    internal const string PortugueseBrazil = "pt-BR";
    internal const string EnglishUnitedStates = "en-US";
    internal const string SpanishSpain = "es-ES";
    internal const string FollowWindows = "windows";

    // Inclua uma cultura somente depois de conferir todo o conjunto de recursos e a interface.
    internal static IReadOnlyList<CultureInfo> SupportedCultures { get; } =
        Array.AsReadOnly(new[] { StudioStrings.NeutralCulture, CultureInfo.GetCultureInfo(EnglishUnitedStates),
            CultureInfo.GetCultureInfo(SpanishSpain) });

    internal static CultureInfo Resolve(string preference, CultureInfo windowsUi) =>
        FindAvailable(preference, windowsUi) ?? StudioStrings.NeutralCulture;

    internal static CultureInfo? FindAvailable(string preference, CultureInfo windowsUi)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(preference, FollowWindows))
            return Find(preference);
        for (var candidate = windowsUi; candidate.Name.Length > 0; candidate = candidate.Parent)
        {
            if (Find(candidate.Name) is { } supported) return supported;
            // A cultura-pai identifica as variantes regionais que usam uma tradução já revisada.
            if (candidate.Name == "en") return Find(EnglishUnitedStates);
            if (candidate.Name == "es") return Find(SpanishSpain);
        }
        return null;
    }

    private static CultureInfo? Find(string name) =>
        SupportedCultures.FirstOrDefault(culture => StringComparer.OrdinalIgnoreCase.Equals(culture.Name, name));

    internal static bool IsValidPreference(string? choice) =>
        choice is { Length: > 0 and <= 64 } && char.IsAsciiLetter(choice[0]) &&
        choice.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
}
