using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using WaveStudio.Audio;

namespace WaveStudio.Desktop.Settings;

internal sealed record SelectionPreference(bool FollowDefault = true, string? EndpointId = null);
internal sealed record EndpointPreference([property: JsonRequired] string EndpointId, [property: JsonRequired] EndpointDirection Direction,
    double? VolumePercent = null, bool? Muted = null, double? Balance = null)
{
    [JsonIgnore]
    public EndpointRestoreValues Values => new(VolumePercent, Muted, Balance);

    public static EndpointPreference FromSnapshot(string endpointId, EndpointDirection direction, EndpointVolumeState state)
    {
        DeviceSelection.ValidatePreference(direction, endpointId);
        var preference = new EndpointPreference(endpointId, direction,
            state.VolumeError is null ? state.VolumePercent : null,
            state.MuteError is null ? state.Muted : null,
            state.BalanceError is null && state.Stereo is { BaseLevel: > 0 } stereo ? stereo.Balance : null);
        preference.Values.Validate();
        if (!preference.Values.HasValues)
            throw new InvalidOperationException(StudioStrings.Get("StudioSettings.OsControlesEstaoIndisponiveisAtualize"));
        return preference;
    }
}

internal sealed record StudioSettings
{
    public const int CurrentVersion = 3;
    public int SchemaVersion { get; init; } = CurrentVersion;
    [JsonRequired] public string Language { get; init; } = StudioLanguage.PortugueseBrazil;
    public SelectionPreference Playback { get; init; } = new();
    public SelectionPreference Capture { get; init; } = new();
    public WindowPlacement? Window { get; init; }
    public EndpointPreference[] Endpoints { get; init; } = [];
    [JsonRequired] public EqualizerPreferences Equalizer { get; init; } = new();

    public StudioSettings WithEndpoint(EndpointPreference preference) => this with
    {
        Endpoints = Endpoints.Where(item => item.EndpointId != preference.EndpointId || item.Direction != preference.Direction)
            .Append(preference).ToArray()
    };
}

internal static class SettingsJson
{
    public const int MaximumBytes = 1024 * 1024;
    public static bool IsValidationError(Exception error) => error is JsonException or InvalidDataException or ArgumentException or NotSupportedException;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter<EndpointDirection>(allowIntegerValues: false) }
    };
    private static readonly JsonSerializerOptions LegacyWindowOptions = new(Options) { RespectRequiredConstructorParameters = false };

    public static StudioSettings Decode(byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException(StudioStrings.Get("StudioSettings.ConfiguracaoExcedeOTamanhoPermitido"));
        ReadOnlyMemory<byte> payload = bytes;
        if (payload.Span.StartsWith(Encoding.UTF8.Preamble)) payload = payload[Encoding.UTF8.Preamble.Length..];
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number))
            throw new InvalidDataException(StudioStrings.Get("StudioSettings.AConfiguracaoPrecisaInformarSchemaVersion"));
        if (number is not (1 or 2 or StudioSettings.CurrentVersion)) throw new NotSupportedException(StudioStrings.Get("StudioSettings.VersaoDeConfiguracaoIncompativelArquivo"));
        CheckDuplicateProperties(root);
        if (number < StudioSettings.CurrentVersion)
        {
            if (root.TryGetProperty("language", out _))
                throw new InvalidDataException(StudioStrings.Get("Language.InvalidPreference"));
            if (number == 1 && root.TryGetProperty("equalizer", out _)) throw new InvalidDataException(StudioStrings.Get("StudioSettings.CamposDeEQExigemA"));
            // Migra em memória. SettingsStore conserva o arquivo original até a primeira gravação válida.
            using var migrated = new MemoryStream();
            using (var writer = new Utf8JsonWriter(migrated))
            {
                writer.WriteStartObject();
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Name == "schemaVersion") writer.WriteNumber("schemaVersion", StudioSettings.CurrentVersion);
                    else if (number == 1 && property.Name == "window" && property.Value.ValueKind == JsonValueKind.Object)
                    {
                        // Na v1, a omissão de X/Y significava zero. Preenche explicitamente esses valores padrão do formato anterior.
                        writer.WritePropertyName("window");
                        JsonSerializer.Serialize(writer, property.Value.Deserialize<WindowPlacement>(LegacyWindowOptions), Options);
                    }
                    else property.WriteTo(writer);
                }
                if (number == 1)
                {
                    writer.WritePropertyName("equalizer");
                    JsonSerializer.Serialize(writer, new EqualizerPreferences(), Options);
                }
                writer.WriteString("language", StudioLanguage.PortugueseBrazil);
                writer.WriteEndObject();
            }
            payload = migrated.ToArray();
        }
        var settings = JsonSerializer.Deserialize<StudioSettings>(payload.Span, Options)
            ?? throw new InvalidDataException(StudioStrings.Get("StudioSettings.ConfiguracaoVazia"));
        Validate(settings);
        return settings;
    }

    public static byte[] Encode(StudioSettings settings)
    {
        Validate(settings);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(settings, Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException(StudioStrings.Get("StudioSettings.ConfiguracaoExcedeOTamanhoPermitido"));
        return bytes;
    }

    public static void Validate(StudioSettings settings)
    {
        if (settings.SchemaVersion != StudioSettings.CurrentVersion) throw new InvalidDataException(StudioStrings.Get("StudioSettings.VersaoInvalidaParaGravacao"));
        if (settings.Equalizer is null) throw new InvalidDataException(StudioStrings.Get("StudioSettings.AjustesDoEqualizadorAusentes"));
        if (!StudioLanguage.IsValidPreference(settings.Language))
            throw new InvalidDataException(StudioStrings.Get("Language.InvalidPreference"));
        ValidateSelection(settings.Playback);
        ValidateSelection(settings.Capture);
        if (settings.Endpoints is null || settings.Endpoints.Length > 1000)
            throw new InvalidDataException(StudioStrings.Get("StudioSettings.ListaDePreferenciasInvalida"));
        var keys = new HashSet<(EndpointDirection, string)>();
        foreach (var endpoint in settings.Endpoints)
        {
            if (endpoint is null) throw new InvalidDataException(StudioStrings.Get("StudioSettings.PreferenciaDeEndpointInvalida"));
            DeviceSelection.ValidatePreference(endpoint.Direction, endpoint.EndpointId);
            if (!keys.Add((endpoint.Direction, endpoint.EndpointId)))
                throw new InvalidDataException(StudioStrings.Get("StudioSettings.PreferenciasDuplicadasParaOMesmo"));
            endpoint.Values.Validate();
        }
        settings.Window?.Validate();
    }

    private static void ValidateSelection(SelectionPreference selection)
    {
        if (selection is null || selection.FollowDefault != (selection.EndpointId is null))
            throw new InvalidDataException(StudioStrings.Get("StudioSettings.SelecaoDeDispositivoInvalida"));
        if (selection.EndpointId is not null)
            DeviceSelection.ValidatePreference(EndpointDirection.Playback, selection.EndpointId);
    }

    private static void CheckDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException(StudioStrings.Get("StudioSettings.PropriedadesJSONDuplicadas"));
                CheckDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) CheckDuplicateProperties(item);
        }
    }
}
