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
                if (snapshot.Key.EndpointId != id || string.IsNullOrWhiteSpace(snapshot.Key.InstanceId) || session.Key != snapshot.Key)
                    throw new InvalidOperationException(StudioStrings.Get("ApplicationSessions.AIdentidadeDaSessaoNao"));
                sessions.Add(snapshot);
            }
            catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error))
            {
                warnings.Add(EndpointVolumeOperations.Describe(error));
            }
        }
        var unique = sessions.GroupBy(session => session.Key).Where(group => group.Count() == 1).Select(group => group.First()).ToArray();
        if (unique.Length != sessions.Count) warnings.Add(StudioStrings.Get("Mixer.Duplicate"));
        return new ApplicationSessionCatalog(unique, warnings.AsReadOnly());
    });

    public Task<ApplicationSessionSnapshot> ApplyAsync(EndpointSelection target, SessionKey key, ApplicationSessionCommand command)
    {
        if (target.DeviceId != key.EndpointId || string.IsNullOrWhiteSpace(key.InstanceId))
            throw new InvalidOperationException(StudioStrings.Get("ApplicationSessions.ASessaoPertenceAOutra"));
        if (command is SetSessionVolume volume)
            StereoLevels.ValidateLevel(volume.Percent / 100);
        return queue.RunAsync(target, (id, token) =>
        {
            using var endpoint = openEndpoint(id);
            IApplicationSessionAccess? selected = null;
            try
            {
                for (int index = 0; index < endpoint.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    IApplicationSessionAccess session;
                    try { session = endpoint.Open(index); }
                    catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error)) { continue; }
                    bool keep = false;
                    try
                    {
                        SessionKey observed;
                        try { observed = session.Key; }
                        catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error)) { continue; }
                        if (observed != key) continue;
                        if (selected is not null)
                            throw new InvalidOperationException(StudioStrings.Get("Mixer.Duplicate"));
                        selected = session;
                        keep = true;
                    }
                    finally { if (!keep) session.Dispose(); }
                }
                if (selected is null)
                    throw new InvalidOperationException(StudioStrings.Get("ApplicationSessions.ASessaoNaoEstaMais"));
                if (selected.Activity == SessionActivity.Expired || selected.Key != key)
                    throw new InvalidOperationException(StudioStrings.Get("ApplicationSessions.ASessaoFoiEncerradaAtualize"));

                token.ThrowIfCancellationRequested();
                switch (command)
                {
                    case SetSessionVolume requested:
                        selected.VolumeScalar = requested.Percent / 100;
                        break;
                    case SetSessionMute requested:
                        selected.Muted = requested.Muted;
                        break;
                    default: throw new ArgumentException(StudioStrings.Get("ApplicationSessions.ComandoDeSessaoDesconhecido"), nameof(command));
                }
                var actual = selected.Read();
                if (actual.Key != key || actual.Activity == SessionActivity.Expired)
                    throw new InvalidOperationException(StudioStrings.Get("Mixer.SessionChanged"));
                if (command is SetSessionVolume value && (actual.VolumePercent is not double readback ||
                        !double.IsFinite(readback) || Math.Abs(readback - value.Percent) > 2) ||
                    command is SetSessionMute mute && actual.Muted != mute.Muted)
                    throw new NotSupportedException(StudioStrings.Get("ApplicationSessions.OWindowsNaoConfirmouA"));
                return actual;
            }
            finally { selected?.Dispose(); }
        });
    }

    internal Task<GameChatResult> BalanceAsync(EndpointSelection selection, IReadOnlyList<GameChatMember> members,
        GameChatLease? lease, int position)
    {
        if (position is < -100 or > 100) throw new ArgumentOutOfRangeException(nameof(position));
        if (lease is not null && lease.Selection != selection)
            return Task.FromResult(new GameChatResult(lease, lease.Levels.Count(x => x.NeedsRestore), true));
        if (position == 0 && lease is null) return Task.FromResult(new GameChatResult(null, 0, false));
        var assigned = members.ToArray();
        return queue.RunAsync(selection, (id, token) =>
        {
            IApplicationSessionEndpoint? endpoint = null;
            try
            {
                endpoint = openEndpoint(id);
                return GameChatBalance.Apply(endpoint, selection, assigned, lease, position, token);
            }
            catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error))
            {
                // Sem leitura do endpoint, nenhum valor residual pode ser considerado restaurado.
                return new GameChatResult(lease is null ? null : lease with { Position = 0 },
                    lease?.Levels.Count(x => x.NeedsRestore) ?? 0, true);
            }
            finally { GameChatBalance.ReleaseAccess(endpoint); }
        });
    }

    public ValueTask DisposeAsync() => queue.DisposeAsync();
}
