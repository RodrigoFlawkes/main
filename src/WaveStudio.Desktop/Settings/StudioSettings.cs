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
}

internal sealed record StudioSettings
{
    public int SchemaVersion { get; init; } = 1;
    public SelectionPreference Playback { get; init; } = new();
    public SelectionPreference Capture { get; init; } = new();
    public WindowPlacement? Window { get; init; }
    public EndpointPreference[] Endpoints { get; init; } = [];

    public StudioSettings WithEndpoint(EndpointPreference preference) => this with
    {
        Endpoints = Endpoints.Where(item => item.EndpointId != preference.EndpointId || item.Direction != preference.Direction)
            .Append(preference).ToArray()
    };
}

internal static class SettingsJson
{
    public const int MaximumBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<EndpointDirection>(allowIntegerValues: false) }
    };

    public static StudioSettings Decode(byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Configuração excede o tamanho permitido.");
        ReadOnlyMemory<byte> payload = bytes;
        if (payload.Span.StartsWith(Encoding.UTF8.Preamble)) payload = payload[Encoding.UTF8.Preamble.Length..];
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number))
            throw new InvalidDataException("A configuração precisa informar schemaVersion.");
        if (number != 1) throw new NotSupportedException("Versão de configuração incompatível. Arquivo preservado.");
        CheckDuplicateProperties(root);
        var settings = JsonSerializer.Deserialize<StudioSettings>(payload.Span, Options)
            ?? throw new InvalidDataException("Configuração vazia.");
        Validate(settings);
        return settings;
    }

    public static byte[] Encode(StudioSettings settings)
    {
        Validate(settings);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(settings, Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Configuração excede o tamanho permitido.");
        return bytes;
    }

    public static void Validate(StudioSettings settings)
    {
        if (settings.SchemaVersion != 1) throw new InvalidDataException("Versão inválida para gravação.");
        ValidateSelection(settings.Playback);
        ValidateSelection(settings.Capture);
        if (settings.Endpoints is null || settings.Endpoints.Length > 1000)
            throw new InvalidDataException("Lista de preferências inválida.");
        var keys = new HashSet<(EndpointDirection, string)>();
        foreach (var endpoint in settings.Endpoints)
        {
            if (endpoint is null) throw new InvalidDataException("Preferência de endpoint inválida.");
            DeviceSelection.ValidatePreference(endpoint.Direction, endpoint.EndpointId);
            if (!keys.Add((endpoint.Direction, endpoint.EndpointId)))
                throw new InvalidDataException("Preferências duplicadas para o mesmo endpoint.");
            endpoint.Values.Validate();
        }
        settings.Window?.Validate();
    }

    private static void ValidateSelection(SelectionPreference selection)
    {
        if (selection is null || selection.FollowDefault != (selection.EndpointId is null))
            throw new InvalidDataException("Seleção de dispositivo inválida.");
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
                if (!names.Add(property.Name)) throw new InvalidDataException("Propriedades JSON duplicadas.");
                CheckDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) CheckDuplicateProperties(item);
        }
    }
}
