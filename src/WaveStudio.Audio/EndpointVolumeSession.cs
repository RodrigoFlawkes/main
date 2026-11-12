namespace WaveStudio.Audio;

public sealed class EndpointVolumeSession : IAsyncDisposable
{
    private readonly EndpointOperationQueue queue = new();
    private readonly Func<string, IEndpointVolumeAccess> openEndpoint;

    public EndpointVolumeSession() : this(id => new CoreAudioVolumeAccess(id)) { }
    public EndpointVolumeSession(Func<string, IEndpointVolumeAccess> openEndpoint) => this.openEndpoint = openEndpoint;
    internal int RetainedGenerationCount => queue.RetainedGenerationCount;
    public EndpointSelection Select(string? deviceId) => queue.Select(deviceId);
    public Task<EndpointVolumeState> ReadAsync(EndpointSelection target) => Execute(target, null);
    public Task<EndpointVolumeState> ApplyAsync(EndpointSelection target, EndpointCommand command) => Execute(target, command);

    public Task<EndpointRestorePlan> PreviewRestoreAsync(EndpointSelection target, EndpointRestoreValues values) =>
        queue.RunAsync(target, (id, token) =>
        {
            using var endpoint = openEndpoint(id);
            token.ThrowIfCancellationRequested();
            var current = EndpointVolumeOperations.Read(endpoint);
            values.RequireSupport(current);
            return new EndpointRestorePlan(target, values, current);
        });

    public Task<EndpointRestoreResult> RestoreAsync(EndpointRestorePlan plan) => queue.RunAsync(plan.Selection, (id, token) =>
    {
        using var endpoint = openEndpoint(id);
        token.ThrowIfCancellationRequested();
        return EndpointPreferenceRestore.Apply(endpoint, plan, token);
    });

    private Task<EndpointVolumeState> Execute(EndpointSelection target, EndpointCommand? command) =>
        queue.RunAsync(target, (id, token) =>
        {
            using var endpoint = openEndpoint(id);
            token.ThrowIfCancellationRequested();
            if (command is not null)
                EndpointVolumeOperations.Apply(endpoint, command, token);
            return EndpointVolumeOperations.Read(endpoint);
        });

    public ValueTask DisposeAsync() => queue.DisposeAsync();
}
