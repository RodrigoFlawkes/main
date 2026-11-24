using System.Text;
using System.Text.Json.Serialization;
using WaveStudio.Audio.Dsp;

namespace WaveStudio.Desktop.Settings;

internal sealed class EqualizerValues
{
    private readonly GraphicEqualizerSettings settings;
    public bool Enabled => settings.Enabled;
    public IReadOnlyList<double> GainsDb => settings.GainsDb;
    public double PreampDb => settings.PreampDb;
    [JsonConstructor]
    public EqualizerValues(bool enabled, IReadOnlyList<double> gainsDb, double preampDb)
    {
        settings = new(enabled, gainsDb, preampDb);
        if (GainsDb.Any(value => value != Math.Truncate(value)) || Math.Abs(preampDb * 10 - Math.Round(preampDb * 10)) > 1e-8)
            throw new ArgumentException(StudioStrings.Get("EqualizerPreferences.UsePassosDe1DB"));
    }
    public static EqualizerValues Neutral() => new(false, new double[10], 0);
    public EqualizerValues Copy() => new(Enabled, GainsDb, PreampDb);
    public GraphicEqualizerSettings ToDsp() => settings;
    public bool SameAs(EqualizerValues other) => Enabled == other.Enabled && PreampDb == other.PreampDb && GainsDb.SequenceEqual(other.GainsDb);
}

internal sealed class EqualizerProfile
{
    public string Id { get; }
    public string Name { get; }
    public EqualizerValues Values { get; }
    [JsonConstructor]
    public EqualizerProfile(string id, string name, EqualizerValues values)
    {
        if (!Guid.TryParseExact(id, "N", out var guid) || guid == Guid.Empty || id != guid.ToString("N"))
            throw new ArgumentException(StudioStrings.Get("EqualizerPreferences.IdentificacaoDePerfilInvalida"));
        if (name != EqualizerPreferences.NormalizeName(name)) throw new ArgumentException(StudioStrings.Get("EqualizerPreferences.NomeDePerfilInvalido"));
        ArgumentNullException.ThrowIfNull(values);
        Id = id; Name = name; Values = values.Copy();
    }
}

internal sealed class EqualizerPreferences
{
    public const int MaximumProfiles = 20, MaximumNameLength = 48;
    public EqualizerValues Current { get; }
    public IReadOnlyList<EqualizerProfile> Profiles { get; }
    public string? SelectedProfileId { get; }
    public bool HasUnsavedChanges() => !Current.SameAs(SelectedProfileId is null
        ? EqualizerValues.Neutral() : Profiles.Single(p => p.Id == SelectedProfileId).Values);
    public EqualizerPreferences() : this(EqualizerValues.Neutral(), [], null) { }
    [JsonConstructor]
    public EqualizerPreferences(EqualizerValues current, IReadOnlyList<EqualizerProfile> profiles, string? selectedProfileId = null)
    {
        ArgumentNullException.ThrowIfNull(current); ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Count > MaximumProfiles || profiles.Any(p => p is null)) throw new ArgumentException(StudioStrings.Get("EqualizerPreferences.LimiteDe20Perfis"));
        var copy = profiles.Select(p => new EqualizerProfile(p.Id, p.Name, p.Values)).ToArray();
        if (copy.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != copy.Length ||
            copy.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != copy.Length)
            throw new ArgumentException(StudioStrings.Get("EqualizerPreferences.PerfisComNomeOuIdentificacao"));
        if (selectedProfileId is not null && !copy.Any(p => p.Id == selectedProfileId)) throw new ArgumentException(StudioStrings.Get("EqualizerPreferences.PerfilSelecionadoNaoExiste"));
        Current = current.Copy(); Profiles = Array.AsReadOnly(copy); SelectedProfileId = selectedProfileId;
    }
    public EqualizerPreferences WithCurrent(EqualizerValues values) => new(values, Profiles, SelectedProfileId);
    public bool SameAs(EqualizerPreferences other) => SelectedProfileId == other.SelectedProfileId && Current.SameAs(other.Current) &&
        Profiles.Count == other.Profiles.Count && Profiles.Zip(other.Profiles).All(pair => pair.First.Id == pair.Second.Id &&
            pair.First.Name == pair.Second.Name && pair.First.Values.SameAs(pair.Second.Values));
    public EqualizerPreferences Apply(string? id) => new(id is null ? EqualizerValues.Neutral() : Find(id).Values, Profiles, id);
    public EqualizerPreferences Create(string name)
    {
        var profile = new EqualizerProfile(Guid.NewGuid().ToString("N"), NormalizeName(name), Current);
        return new(Current, Profiles.Append(profile).ToArray(), profile.Id);
    }
    public EqualizerPreferences SaveSelected()
    {
        var selected = Find(SelectedProfileId);
        return new(Current, Profiles.Select(p => p.Id == selected.Id ? new(p.Id, p.Name, Current) : p).ToArray(), selected.Id);
    }
    public EqualizerPreferences Rename(string name)
    {
        var selected = Find(SelectedProfileId);
        return new(Current, Profiles.Select(p => p.Id == selected.Id ? new(p.Id, NormalizeName(name), p.Values) : p).ToArray(), selected.Id);
    }
    public EqualizerPreferences DeleteSelected()
    {
        var selected = Find(SelectedProfileId);
        // Excluir um nome não altera os parâmetros que estão sendo ouvidos nem inicia reprodução.
        return new(Current, Profiles.Where(p => p.Id != selected.Id).ToArray(), null);
    }
    private EqualizerProfile Find(string? id) => Profiles.SingleOrDefault(p => p.Id == id)
        ?? throw new InvalidOperationException(StudioStrings.Get("EqualizerPreferences.SelecioneUmPerfilPersonalizado"));
    public static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string result = string.Join(' ', name.Normalize(NormalizationForm.FormKC).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (result.Length is < 1 or > MaximumNameLength || result.Equals("Padrão", StringComparison.OrdinalIgnoreCase) ||
            result.Any(c => !char.IsLetterOrDigit(c) && c is not (' ' or '-' or '_' or '(' or ')')))
            throw new ArgumentException(StudioStrings.Get("EqualizerPreferences.UseUmNomeDe1"));
        return result;
    }
}
