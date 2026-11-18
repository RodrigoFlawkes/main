using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("WaveStudio.Tests")]

namespace WaveStudio.Audio;

internal sealed class EndpointOperationQueue : IAsyncDisposable
{
    private sealed class Generation
    {
        public readonly CancellationTokenSource Source = new();
        public int Pending;
        public bool Retired;
        public bool Released;
    }

    private readonly object sync = new();
    private Generation generation = new();
    private EndpointSelection selection = new(null, 0);
    private Task tail = Task.CompletedTask;
    private Task? disposal;
    private bool disposed;
    internal int RetainedGenerationCount { get; private set; } = 1;

    public EndpointSelection Select(string? id)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Retire(generation);
            generation = new Generation();
            RetainedGenerationCount++;
            selection = new EndpointSelection(id, selection.Revision + 1);
            return selection;
        }
    }

    private void Retire(Generation value)
    {
        value.Retired = true;
        value.Source.Cancel();
        ReleaseIfFinished(value);
    }

    private void ReleaseIfFinished(Generation value)
    {
        if (value.Retired && value.Pending == 0 && !value.Released)
        {
            value.Released = true;
            value.Source.Dispose();
            RetainedGenerationCount--;
        }
    }

    public Task<T> RunAsync<T>(EndpointSelection target, Func<string, CancellationToken, T> operation)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (target != selection || target.DeviceId is null)
                return Task.FromCanceled<T>(new CancellationToken(true));
            generation.Pending++;
            var request = ExecuteAsync(tail, generation, target.DeviceId, operation);
            tail = request;
            return request;
        }
    }

    private async Task<T> ExecuteAsync<T>(Task previous, Generation owner, string id, Func<string, CancellationToken, T> operation)
    {
        var token = owner.Source.Token;
        try
        {
            try { await previous.ConfigureAwait(false); }
            catch (Exception) { /* Uma falha anterior não deve afetar solicitações posteriores. */ }
            previous = Task.CompletedTask;
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var result = operation(id, token);
                token.ThrowIfCancellationRequested();
                return result;
            }, token).ConfigureAwait(false);
        }
        finally
        {
            lock (sync)
            {
                owner.Pending--;
                ReleaseIfFinished(owner);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (disposed)
                return new ValueTask(disposal!);
            disposed = true;
            Retire(generation);
            disposal = ObserveAsync(tail);
            return new ValueTask(disposal);
        }
    }

    private static async Task ObserveAsync(Task pending)
    {
        try { await pending.ConfigureAwait(false); }
        catch (Exception) { /* Cada geração libera seus recursos após a última operação. */ }
    }
}
