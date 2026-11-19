using System.Diagnostics;
using System.Runtime.CompilerServices;
using Xunit;

namespace WaveStudio.Tests;

// O CoreCLR 10 pode contabilizar a sobra de um contexto como alocação quando outro trabalho dispara BGC.
// Cada medição exata usa os mesmos binários em um processo sem outras classes de teste. O GC e o JIT mantêm a configuração padrão.
internal static class AllocationIsolation
{
    private const string MethodVariable = "WAVESTUDIO_ALLOCATION_MEASUREMENT_METHOD";

    public static Task Run<T>(Action measure, CancellationToken token, [CallerMemberName] string method = "") =>
        RunAsync<T>(() => { measure(); return Task.CompletedTask; }, token, method);

    public static Task RunAsync<T>(Func<Task> measure, CancellationToken token, [CallerMemberName] string method = "")
    {
        string name = typeof(T).FullName + "." + method;
        if (!string.Equals(Environment.GetEnvironmentVariable(MethodVariable), name, StringComparison.Ordinal))
            return RunChild<T>(method, token);
        VerifyCounter();
        return measure();
    }

    public static async Task RunChild<T>(string method, CancellationToken token)
    {
        string name = typeof(T).FullName + "." + method;
        var start = new ProcessStartInfo(Path.ChangeExtension(typeof(AllocationIsolation).Assembly.Location, ".exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string arg in new[] { "--explicit", "off", "--filter-method", name, "--minimum-expected-tests", "1", "--timeout", "30s" })
            start.ArgumentList.Add(arg);
        start.Environment[MethodVariable] = name;
        start.Environment["TESTINGPLATFORM_TELEMETRY_OPTOUT"] = "1";

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(40));
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Apphost de medição indisponível.");
        Task<string> output = child.StandardOutput.ReadToEndAsync(deadline.Token);
        Task<string> errors = child.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await child.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            string stdout = await output.ConfigureAwait(false);
            string stderr = await errors.ConfigureAwait(false);
            Assert.True(child.ExitCode == 0, $"{name} saiu com código {child.ExitCode}.\n{stdout}\n{stderr}");
        }
        finally
        {
            if (!child.HasExited) child.Kill();
        }
    }

    private static void VerifyCounter()
    {
        var anchor = Allocate();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long negative = GC.GetAllocatedBytesForCurrentThread() - before;
        var value = Allocate();
        long positive = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(anchor);
        GC.KeepAlive(value);
        Assert.Equal(0, negative);
        Assert.Equal(64, positive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] Allocate() => new byte[37];
}

public sealed class AllocationIsolationTests
{
    [Fact]
    public Task PublicationMeasuresZeroWithBackgroundCollectionsInAnotherProcess() =>
        AllocationIsolation.RunAsync<AllocationIsolationTests>(MeasureUnderPressure, TestContext.Current.CancellationToken);

    private static async Task MeasureUnderPressure()
    {
        using var stop = new ManualResetEventSlim();
        using var firstRequest = new ManualResetEventSlim();
        var buffers = new byte[2048][];
        for (int index = 0; index < buffers.Length; index++) buffers[index] = new byte[2048];
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        var worker = new Thread(() =>
        {
            int request = 0;
            do
            {
                for (int index = 0; index < 256; index++) buffers[(request * 256 + index) % buffers.Length] = new byte[2048];
                GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false);
                if (++request == 2) firstRequest.Set();
            } while (!stop.Wait(3));
            GC.KeepAlive(buffers);
        }) { IsBackground = true };
        worker.Start();
        try
        {
            Assert.True(firstRequest.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            long before = GC.GetGCMemoryInfo(GCKind.Background).Index;
            await AllocationIsolation.RunChild<WasapiCheckpointTests>(
                nameof(WasapiCheckpointTests.PublicationUsesBoundedPreallocatedMemoryAndNoRecurringAllocations),
                TestContext.Current.CancellationToken);
            long after = GC.GetGCMemoryInfo(GCKind.Background).Index;
            Assert.True(after > before, $"O processo de carga precisa completar BGC durante a medição externa ({before} → {after}).");
        }
        finally
        {
            stop.Set();
            Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        }
    }
}
