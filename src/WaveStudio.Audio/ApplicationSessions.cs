namespace WaveStudio.Audio;

public enum SessionActivity { Inactive, Active, Expired }
public sealed record SessionKey(string EndpointId, string InstanceId);
public sealed record ApplicationSessionSnapshot(SessionKey Key, string Name, uint? ProcessId,
    bool IsSystemSounds, SessionActivity Activity, double? VolumePercent, bool? Muted, string? Error,
    PeakReading? Peak = null, long PeakRecordedAt = 0);
public sealed record ApplicationSessionCatalog(IReadOnlyList<ApplicationSessionSnapshot> Sessions, IReadOnlyList<string> Warnings);
public sealed record ApplicationSessionBinding(EndpointSelection Selection, ApplicationSessionSnapshot Snapshot)
{
    public bool IsCurrent(EndpointSelection current) => Selection == current && Snapshot.Key.EndpointId == current.DeviceId;
}
public abstract record ApplicationSessionCommand;
public sealed record SetSessionVolume(double Percent) : ApplicationSessionCommand;
public sealed record SetSessionMute(bool Muted) : ApplicationSessionCommand;

public interface IApplicationSessionAccess : IDisposable
{
    SessionKey Key { get; }
    SessionActivity Activity { get; }
    ApplicationSessionSnapshot Read();
    double VolumeScalar { get; set; }
    bool Muted { get; set; }
}

public interface IApplicationSessionEndpoint : IDisposable
{
    int Count { get; }
    IApplicationSessionAccess Open(int index);
}

public sealed class ApplicationSessionService : IAsyncDisposable
{
    private readonly EndpointOperationQueue queue = new();
    private readonly Func<string, IApplicationSessionEndpoint> openEndpoint;
    public ApplicationSessionService() : this(id => new CoreAudioSessionEndpoint(id)) { }
    public ApplicationSessionService(Func<string, IApplicationSessionEndpoint> openEndpoint) => this.openEndpoint = openEndpoint;
    public EndpointSelection Select(string? endpointId) => queue.Select(endpointId);

    public Task<ApplicationSessionCatalog> ReadAsync(EndpointSelection target) => queue.RunAsync(target, (id, token) =>
    {
        using var endpoint = openEndpoint(id);
        var sessions = new List<ApplicationSessionSnapshot>();
        var warnings = new List<string>();
        for (int index = 0; index < endpoint.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var session = endpoint.Open(index);
                var snapshot = session.Read();
                if (snapshot.Key.EndpointId != id || string.IsNullOrWhiteSpace(snapshot.Key.InstanceId))
                    throw new InvalidOperationException("A identidade da sessão não pôde ser confirmada.");
                sessions.Add(snapshot);
            }
            catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error))
            {
                warnings.Add(EndpointVolumeOperations.Describe(error));
            }
        }
        return new ApplicationSessionCatalog(sessions.AsReadOnly(), warnings.AsReadOnly());
    });

    public Task<ApplicationSessionSnapshot> ApplyAsync(EndpointSelection target, SessionKey key, ApplicationSessionCommand command)
    {
        if (target.DeviceId != key.EndpointId || string.IsNullOrWhiteSpace(key.InstanceId))
            throw new InvalidOperationException("A sessão pertence a outra seleção de dispositivo.");
        if (command is SetSessionVolume volume)
            StereoLevels.ValidateLevel(volume.Percent / 100);
        return queue.RunAsync(target, (id, token) =>
        {
            using var endpoint = openEndpoint(id);
            for (int index = 0; index < endpoint.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                IApplicationSessionAccess session;
                try { session = endpoint.Open(index); }
                catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error)) { continue; }
                using (session)
                {
                    SessionKey observed;
                    try { observed = session.Key; }
                    catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error)) { continue; }
                    if (observed != key) continue;
                    if (session.Activity == SessionActivity.Expired || session.Key != key)
                        throw new InvalidOperationException("A sessão foi encerrada. Atualize o mixer.");
                    token.ThrowIfCancellationRequested();
                    switch (command)
                    {
                        case SetSessionVolume requested:
                            session.VolumeScalar = requested.Percent / 100;
                            break;
                        case SetSessionMute requested:
                            session.Muted = requested.Muted;
                            break;
                        default: throw new ArgumentException("Comando de sessão desconhecido.", nameof(command));
                    }
                    var actual = session.Read();
                    if (command is SetSessionVolume value && (actual.VolumePercent is null || Math.Abs(actual.VolumePercent.Value - value.Percent) > 2)
                        || command is SetSessionMute mute && actual.Muted != mute.Muted)
                        throw new NotSupportedException("O Windows não confirmou a alteração da sessão. Atualize o mixer.");
                    return actual;
                }
            }
            throw new InvalidOperationException("A sessão não está mais disponível. Atualize o mixer.");
        });
    }

    public ValueTask DisposeAsync() => queue.DisposeAsync();
}
