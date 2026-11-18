namespace WaveStudio.Audio;

/// <summary>Armazena preferências independentes de endpoints sem alterar as configurações do Windows.</summary>
public sealed class DeviceSelection
{
    public string? PlaybackId { get; private set; }
    public string? CaptureId { get; private set; }

    public void FollowDefault(EndpointDirection direction)
    {
        SetPreference(direction, null);
    }

    public void Select(DeviceSnapshot device)
    {
        SetPreference(device.Direction, device.Id);
    }

    public static void ValidatePreference(EndpointDirection direction, string? id)
    {
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (id is not null && (string.IsNullOrWhiteSpace(id) || id.Length > 2048 || id.Any(char.IsControl)))
            throw new ArgumentException("Identificador de dispositivo inválido.", nameof(id));
    }

    public void SetPreference(EndpointDirection direction, string? id)
    {
        ValidatePreference(direction, id);
        if (direction == EndpointDirection.Playback)
            PlaybackId = id;
        else
            CaptureId = id;
    }

    public DeviceSnapshot? Resolve(DeviceCatalog catalog, EndpointDirection direction)
    {
        ValidatePreference(direction, null);
        string? id = direction == EndpointDirection.Playback ? PlaybackId : CaptureId;
        var candidates = catalog.Devices.Where(device => device.Direction == direction);

        // Uma preferência ausente permanece ausente até o usuário escolher um substituto.
        if (id is not null)
            return candidates.FirstOrDefault(device => device.Id == id);

        return candidates.FirstOrDefault(device => device.IsDefaultMultimedia && device.State == EndpointState.Active)
            ?? candidates.FirstOrDefault(device => device.State == EndpointState.Active)
            ?? candidates.FirstOrDefault(device => device.IsDefaultMultimedia)
            ?? candidates.FirstOrDefault();
    }
}
