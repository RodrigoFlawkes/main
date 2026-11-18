namespace WaveStudio.Audio.Engine;

// Control plane only. Unique COM children precede parents in inverse acquisition order.
internal sealed class WasapiResourceScope(IDisposable? lease = null) : IDisposable
{
    private static readonly List<object> retained = [];
    private readonly List<IDisposable> resources = [];
    private bool attempted;
    public bool Released { get; private set; }
    public string? CleanupError { get; private set; }
    public T Add<T>(T resource) where T : IDisposable { resources.Add(resource); return resource; }
    public static void Retain(object resource) { lock (retained) retained.Add(resource); }
    public void Dispose()
    {
        if (attempted) return;
        attempted = true;
        for (int i = resources.Count - 1; i >= 0; i--)
        {
            try { resources[i].Dispose(); resources.RemoveAt(i); }
            catch (Exception error) { CleanupError = error.GetType().Name; Retain(this); return; }
        }
        lease?.Dispose(); Released = true;
    }
}
