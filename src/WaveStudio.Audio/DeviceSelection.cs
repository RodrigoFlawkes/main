namespace WaveStudio.Audio;

/// <summary>Stores independent endpoint preferences without changing Windows settings.</summary>
public sealed class DeviceSelection
{
    public string? PlaybackId { get; private set; }
    public string? CaptureId { get; private set; }

    public void FollowDefault(EndpointDirection direction)
    {
        if (direction == EndpointDirection.Playback) PlaybackId = null;
        else CaptureId = null;
    }

    public void Select(DeviceSnapshot device)
    {
        if (device.Direction == EndpointDirection.Playback)
            PlaybackId = device.Id;
        else
            CaptureId = device.Id;
    }

    public DeviceSnapshot? Resolve(DeviceCatalog catalog, EndpointDirection direction)
    {
        string? id = direction == EndpointDirection.Playback ? PlaybackId : CaptureId;
        var candidates = catalog.Devices.Where(device => device.Direction == direction);

        // An absent preference stays absent until the user chooses a replacement.
        if (id is not null)
            return candidates.FirstOrDefault(device => device.Id == id);

        return candidates.FirstOrDefault(device => device.IsDefaultMultimedia)
            ?? candidates.FirstOrDefault(device => device.State == EndpointState.Active)
            ?? candidates.FirstOrDefault();
    }
}
