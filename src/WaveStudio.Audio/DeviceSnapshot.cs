namespace WaveStudio.Audio;

public enum EndpointDirection { Playback, Capture }

public enum EndpointState { Active, Disabled, NotPresent, Unplugged, Unknown }

public sealed record MixFormatSnapshot(int SampleRate, int Channels, int BitsPerSample, string Encoding);

public sealed record DeviceSnapshot(
    string Id,
    string Name,
    EndpointDirection Direction,
    EndpointState State,
    bool IsDefaultMultimedia,
    bool IsDefaultCommunications,
    MixFormatSnapshot? MixFormat,
    string? InformationError);

public sealed record DeviceCatalog(IReadOnlyList<DeviceSnapshot> Devices, IReadOnlyList<string> Warnings);
