using System.Collections.Concurrent;
using System.Diagnostics;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class PipelineLifecycleConcurrencyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ParticipantsRaceStopCancelResetOrDisposeWithBoundedLifetime(int action)
    {
        var token = TestContext.Current.CancellationToken;
        // Threads dedicadas ao teste evitam que os laços ativos dependam do crescimento do pool de threads.
        for (int iteration = 0; iteration < 32; iteration++)
        {
            token.ThrowIfCancellationRequested();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var pipeline = new SyntheticAudioPipeline(new(48000, 2), new(48000, 2), 4096, 4096);
            long generation = pipeline.Start(cancellation.Token);
            using var start = new ManualResetEventSlim();
            var errors = new ConcurrentQueue<Exception>();
            int stop = 0, writes = 0, reads = 0;
            var producer = new Thread(() => Run(() =>
            {
                var samples = Enumerable.Repeat(.25f, 8192).ToArray();
                while (Volatile.Read(ref stop) == 0)
                {
                    int accepted = pipeline.Produce(generation, samples);
                    if (accepted is < 0 or > 4096) throw new InvalidOperationException("Contagem inválida.");
                    Interlocked.Increment(ref writes);
                }
            })) { IsBackground = true };
            var consumer = new Thread(() => Run(() =>
            {
                var samples = new float[8192];
                while (Volatile.Read(ref stop) == 0)
                {
                    int rendered = pipeline.Render(generation, samples);
                    if (rendered is < 0 or > 4096 || samples.Any(value => value != 0 && value != .25f))
                        throw new InvalidOperationException("Frame corrompido ou contagem inválida.");
                    Interlocked.Increment(ref reads);
                }
            })) { IsBackground = true };
            producer.Start();
            consumer.Start();
            start.Set();
            try
            {
                var clock = Stopwatch.StartNew();
                while (Volatile.Read(ref writes) < 2 || Volatile.Read(ref reads) < 2)
                {
                    token.ThrowIfCancellationRequested();
                    if (clock.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Participantes não avançaram.");
                    Thread.Yield();
                }
                if (action == 1) cancellation.Cancel();
                else pipeline.Stop();
                if (action == 2)
                {
                    try { pipeline.Dispose(); }
                    catch (InvalidOperationException) { /* Participante ativo: tente novamente somente após o término da thread. */ }
                }
                else pipeline.TryReset(); // False enquanto ocupado é um resultado explícito e válido.
            }
            finally
            {
                Volatile.Write(ref stop, 1);
                Assert.True(producer.Join(TimeSpan.FromSeconds(5)));
                Assert.True(consumer.Join(TimeSpan.FromSeconds(5)));
            }
            Assert.Empty(errors);
            if (pipeline.State != SyntheticPipelineState.Disposed)
            {
                Assert.True(pipeline.TryReset());
                Assert.Equal(0, pipeline.Snapshot().Buffer!.Value.WrittenFrames);
                long next = pipeline.Start(token);
                Assert.True(next > generation);
                Assert.Equal(0, pipeline.Produce(generation, [.9f, .9f]));
                Assert.Equal(1, pipeline.Produce(next, [.5f, -.5f]));
                var output = new float[2];
                Assert.Equal(0, pipeline.Render(generation, output));
                Assert.Equal(new float[] { 0, 0 }, output);
                Assert.Equal(1, pipeline.Render(next, output));
                Assert.Equal(new float[] { .5f, -.5f }, output);
            }
            pipeline.Dispose();
            pipeline.Dispose();
            void Run(Action body)
            {
                try
                {
                    if (!start.Wait(TimeSpan.FromSeconds(5), token)) throw new TimeoutException();
                    body();
                }
                catch (Exception error) { errors.Enqueue(error); Volatile.Write(ref stop, 1); }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedCallbackAfterRestartCannotPublishPreviousGeneration(bool pcm)
    {
        using var pipeline = new SyntheticAudioPipeline(new(48000, 1), new(48000, 1), 8, 8);
        var token = TestContext.Current.CancellationToken;
        long old = pipeline.Start(token);
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var pending = Task.Run(() =>
        {
            ready.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5), token)) throw new TimeoutException();
            return pcm ? pipeline.ProducePcm(old, new byte[] { 0, 64 }, PcmEncoding.Pcm16LittleEndian)
                : pipeline.Produce(old, [.5f]);
        }, token);
        try
        {
            Assert.True(ready.Wait(TimeSpan.FromSeconds(5), token));
            pipeline.Stop();
            Assert.True(pipeline.TryReset());
            long current = pipeline.Start(token);
            release.Set();
            Assert.Equal(0, await pending.WaitAsync(TimeSpan.FromSeconds(5), token));
            Assert.Equal(0, pipeline.Snapshot().Buffer!.Value.WrittenFrames);
            Assert.Equal(1, pipeline.Produce(current, [.75f]));
            var result = new float[1];
            Assert.Equal(0, pipeline.Render(old, result));
            Assert.Equal(0, result[0]);
            Assert.Equal(1, pipeline.Render(current, result));
            Assert.Equal(.75f, result[0]);
        }
        finally { release.Set(); }
    }
}
