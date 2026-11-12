namespace WaveStudio.Audio;

/// <summary>Serializes a panel's reads/writes and invalidates queued work when its selection changes.</summary>
public sealed class EndpointVolumeSession : IAsyncDisposable
{
    private readonly Func<string, IEndpointVolumeAccess> openEndpoint;
    private readonly object sync = new();
    private readonly List<CancellationTokenSource> lifetimes = [];
    private CancellationTokenSource selectionLifetime = new();
    private EndpointSelection selection = new(null, 0);
    private Task tail = Task.CompletedTask;
    private bool disposed;
    private Task? disposal;

    public EndpointVolumeSession() : this(id => new CoreAudioVolumeAccess(id)) { }

    public EndpointVolumeSession(Func<string, IEndpointVolumeAccess> openEndpoint)
    {
        this.openEndpoint = openEndpoint;
        lifetimes.Add(selectionLifetime);
    }

    public EndpointSelection Select(string? deviceId)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            selectionLifetime.Cancel();
            selectionLifetime = new CancellationTokenSource();
            lifetimes.Add(selectionLifetime);
            selection = new EndpointSelection(deviceId, selection.Revision + 1);
            return selection;
        }
    }

    public Task<EndpointVolumeState> ReadAsync(EndpointSelection target) => Queue(target, null);

    public Task<EndpointVolumeState> ApplyAsync(EndpointSelection target, EndpointCommand command) => Queue(target, command);

    private Task<EndpointVolumeState> Queue(EndpointSelection target, EndpointCommand? command)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (target != selection || target.DeviceId is null)
                return Task.FromCanceled<EndpointVolumeState>(new CancellationToken(true));
            var operation = ExecuteAfterAsync(tail, target.DeviceId, command, selectionLifetime.Token);
            tail = operation;
            return operation;
        }
    }

    private async Task<EndpointVolumeState> ExecuteAfterAsync(Task previous, string id,
        EndpointCommand? command, CancellationToken cancellationToken)
    {
        try { await previous.ConfigureAwait(false); }
        catch (Exception) { /* A failed request must not poison the next read. */ }
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var endpoint = openEndpoint(id);
            cancellationToken.ThrowIfCancellationRequested();
            if (command is not null)
                EndpointVolumeOperations.Apply(endpoint, command, cancellationToken);
            var state = EndpointVolumeOperations.Read(endpoint);
            cancellationToken.ThrowIfCancellationRequested();
            return state;
        }, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (disposed)
                return new ValueTask(disposal!);
            disposed = true;
            selectionLifetime.Cancel();
            disposal = FinishDisposalAsync(tail);
            return new ValueTask(disposal);
        }
    }

    private async Task FinishDisposalAsync(Task pending)
    {
        try { await pending.ConfigureAwait(false); }
        catch (Exception) { /* Observe the final operation before releasing lifetime resources. */ }
        foreach (var lifetime in lifetimes)
            lifetime.Dispose();
    }
}
