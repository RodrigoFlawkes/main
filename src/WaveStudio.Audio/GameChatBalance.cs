namespace WaveStudio.Audio;

internal enum GameChatSide { Game, Chat }
internal sealed record GameChatMember(SessionKey Key, GameChatSide Side);
internal sealed record GameChatLevel(GameChatMember Member, double Baseline, double Applied, double? Attempted = null)
{
    internal bool NeedsRestore => !GameChatBalance.Same(Baseline, Applied) || Attempted.HasValue;
}
internal sealed record GameChatLease(EndpointSelection Selection, int Position, IReadOnlyList<GameChatLevel> Levels);
internal sealed record GameChatResult(GameChatLease? Lease, int Issues, bool Suspended);

internal static class GameChatBalance
{
    // A igualdade usa a precisão do float nativo de ISimpleAudioVolume.
    internal static bool Same(double a, double b) => (float)a == (float)b;

    internal static bool Complete(EndpointSelection selection, IReadOnlyList<GameChatMember> members) =>
        selection.DeviceId is not null && members.All(m => m.Key.EndpointId == selection.DeviceId &&
            !string.IsNullOrWhiteSpace(m.Key.InstanceId) && m.Side is GameChatSide.Game or GameChatSide.Chat) &&
        members.Any(m => m.Side == GameChatSide.Game) && members.Any(m => m.Side == GameChatSide.Chat) &&
        members.Select(m => m.Key).Distinct().Count() == members.Count;

    internal static bool Matches(GameChatLease lease, IReadOnlyList<GameChatMember> members) =>
        lease.Levels.Count == members.Count && lease.Levels.Select(x => x.Member).ToHashSet().SetEquals(members);

    internal static GameChatResult Apply(IApplicationSessionEndpoint endpoint, EndpointSelection selection,
        IReadOnlyList<GameChatMember> members, GameChatLease? previous, int position, CancellationToken token)
    {
        var keys = members.Select(x => x.Key).Concat(previous?.Levels.Select(x => x.Member.Key) ?? []).ToHashSet();
        using var sessions = SessionSet.Open(endpoint, keys, token);
        if (position == 0)
            return Restore(sessions, previous, suspended: false);
        if (!Complete(selection, members) || previous is not null && (previous.Position == 0 || !Matches(previous, members)) ||
            !sessions.Complete)
            return Restore(sessions, previous, suspended: true);

        var levels = previous?.Levels.ToList() ?? [];
        try
        {
            if (previous is null)
            {
                // Capta e valida todos os níveis antes da primeira escrita.
                foreach (var member in members)
                {
                    token.ThrowIfCancellationRequested();
                    var baseline = Read(sessions.Get(member.Key), member.Key, activeOnly: true);
                    levels.Add(new(member, baseline, baseline));
                }
            }
            foreach (var item in levels)
                if (!Same(Read(sessions.Get(item.Member.Key), item.Member.Key, activeOnly: true), item.Applied))
                    return Restore(sessions, new(selection, position, levels), suspended: true);

            // Ao cruzar o centro, reduz o novo grupo antes de devolver volume ao grupo anterior.
            var changes = levels.Select((item, index) => (Index: index, Volume: DesiredLevel(item, position)))
                .OrderBy(change => change.Volume > levels[change.Index].Applied);
            foreach (var (index, desired) in changes)
            {
                token.ThrowIfCancellationRequested();
                var item = levels[index];
                var key = item.Member.Key;
                var session = sessions.Get(key);
                if (!Same(Read(session, key, activeOnly: true), item.Applied))
                    return Restore(sessions, new(selection, position, levels), suspended: true);
                if (Same(desired, item.Applied)) continue;
                // Uma falha COM pode acontecer depois que o setter produziu efeito.
                levels[index] = item with { Attempted = desired };
                session!.VolumeScalar = desired;
                var actual = Read(session, key, activeOnly: true);
                if (!Same(actual, desired))
                    return Restore(sessions, new(selection, position, levels), suspended: true);
                levels[index] = item with { Applied = actual };
            }
            foreach (var item in levels)
                if (!Same(Read(sessions.Get(item.Member.Key), item.Member.Key, activeOnly: true), item.Applied))
                    return Restore(sessions, new(selection, position, levels), suspended: true);
            token.ThrowIfCancellationRequested();
            return new(new(selection, position, levels.AsReadOnly()), 0, false);
        }
        catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error) || error is OperationCanceledException)
        {
            return Restore(sessions, new(selection, position, levels), suspended: true);
        }
    }

    private static double DesiredLevel(GameChatLevel item, int position)
    {
        int attenuation = item.Member.Side == GameChatSide.Game ? Math.Max(position, 0) : Math.Max(-position, 0);
        return item.Baseline * (1 - attenuation / 100d);
    }

    private static GameChatResult Restore(SessionSet sessions, GameChatLease? lease, bool suspended)
    {
        if (lease is null) return new(null, 0, suspended);
        var remaining = new List<GameChatLevel>();
        int issues = 0;
        foreach (var item in lease.Levels.Where(x => x.NeedsRestore))
        {
            var key = item.Member.Key;
            var session = sessions.Get(key);
            if (!sessions.Complete || session is null)
            {
                remaining.Add(item);
                issues++;
                continue;
            }
            try
            {
                if (session.Key == key && session.Activity == SessionActivity.Expired)
                {
                    // Uma instância expirada não pode voltar a receber comandos.
                    issues++;
                    continue;
                }
                var current = Read(session, key, activeOnly: false);
                if (Same(current, item.Baseline)) continue;
                bool stillApplied = !Same(item.Baseline, item.Applied) && Same(current, item.Applied) ||
                    item.Attempted is double attempted && Same(current, attempted);
                if (!stillApplied)
                {
                    issues++;
                    continue;
                }

                session.VolumeScalar = item.Baseline;
                var actual = Read(session, key, activeOnly: false);
                if (!Same(actual, item.Baseline))
                {
                    if (Same(actual, current)) remaining.Add(item);
                    issues++;
                }
            }
            catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error))
            {
                remaining.Add(item);
                issues++;
            }
        }
        return new(remaining.Count == 0 ? null : lease with { Position = 0, Levels = remaining.AsReadOnly() }, issues, suspended);
    }

    private static double Read(IApplicationSessionAccess? session, SessionKey key, bool activeOnly)
    {
        if (session is null || session.Key != key || !IsAvailable(session.Activity, activeOnly))
            throw new InvalidOperationException();
        var level = session.VolumeScalar;
        if (session.Key != key || !IsAvailable(session.Activity, activeOnly) || !double.IsFinite(level) || level is < 0 or > 1)
            throw new InvalidOperationException();
        return level;
    }

    private static bool IsAvailable(SessionActivity activity, bool activeOnly) =>
        activity == SessionActivity.Active || !activeOnly && activity == SessionActivity.Inactive;

    // A invalidação ao liberar um objeto COM não apaga o estado confirmado das escritas.
    internal static void ReleaseAccess(IDisposable? access)
    {
        try { access?.Dispose(); }
        catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error)) { }
    }

    private sealed class SessionSet : IDisposable
    {
        private readonly Dictionary<SessionKey, IApplicationSessionAccess> bound = [];
        private readonly HashSet<SessionKey> duplicate = [];
        public bool Complete { get; private set; } = true;

        public IApplicationSessionAccess? Get(SessionKey key) => duplicate.Contains(key) ? null : bound.GetValueOrDefault(key);

        public static SessionSet Open(IApplicationSessionEndpoint endpoint, HashSet<SessionKey> keys, CancellationToken token)
        {
            var set = new SessionSet();
            try
            {
                int count = endpoint.Count;
                for (int i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    IApplicationSessionAccess? opened = null;
                    try
                    {
                        opened = endpoint.Open(i);
                        var key = opened.Key;
                        if (!keys.Contains(key)) continue;
                        if (!set.bound.TryAdd(key, opened)) set.duplicate.Add(key);
                        else opened = null;
                    }
                    catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error)) { set.Complete = false; }
                    finally { ReleaseAccess(opened); }
                }
                return set;
            }
            catch
            {
                set.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (var session in bound.Values) ReleaseAccess(session);
        }
    }
}
