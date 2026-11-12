using System.Text;
using WaveStudio.Audio;
using WaveStudio.Desktop.Settings;
using Xunit;

namespace WaveStudio.Tests;

internal sealed class TemporarySettingsDirectory : IDisposable
{
    private readonly string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WaveStudio.Tests"));
    public string Path { get; }
    public string FilePath => System.IO.Path.Combine(Path, "settings.json");
    public TemporarySettingsDirectory()
    {
        Path = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public void Dispose()
    {
        string resolved = System.IO.Path.GetFullPath(Path);
        if (!string.Equals(System.IO.Path.GetDirectoryName(resolved), root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Diretório de teste fora da raiz temporária específica.");
        Directory.Delete(resolved, recursive: true);
    }
}

public sealed class SettingsStoreTests
{
    private sealed class FaultWriter : ISettingsWriter
    {
        private readonly SettingsWriter inner = new();
        public Action? BeforeWrite;
        public Action? AfterWrite;
        public Action? BeforeCommit;
        public void WriteNew(string path, byte[] bytes, CancellationToken token)
        {
            BeforeWrite?.Invoke();
            inner.WriteNew(path, bytes, token);
            AfterWrite?.Invoke();
        }
        public void Commit(string temporary, string target, bool replacing, string? backup)
        {
            BeforeCommit?.Invoke();
            inner.Commit(temporary, target, replacing, backup);
        }
    }

    private static StudioSettings Sample => new()
    {
        Playback = new(false, "missing-output"), Capture = new(), Window = new(30, 50, 800, 600, true),
        Endpoints = [new("missing-output", EndpointDirection.Playback, 35.5, true, -40), new("mic", EndpointDirection.Capture, null, false)]
    };

    [Fact]
    public async Task FirstRunRoundTripAndIndependentMissingSelection()
    {
        using var directory = new TemporarySettingsDirectory();
        var store = new SettingsStore(directory.FilePath);
        var first = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SettingsFileState.Missing, first.State);
        Assert.True(first.Settings.Playback.FollowDefault);
        Assert.False(File.Exists(directory.FilePath));
        Assert.True((await store.SaveAsync(Sample, TestContext.Current.CancellationToken)).Success);
        var loaded = await new SettingsStore(directory.FilePath).LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SettingsFileState.Ready, loaded.State);
        Assert.Equal(Sample.Playback, loaded.Settings.Playback);
        Assert.Equal(Sample.Capture, loaded.Settings.Capture);
        Assert.Equal(Sample.Endpoints, loaded.Settings.Endpoints);
        Assert.Equal(Sample.Window, loaded.Settings.Window);
        var selection = new DeviceSelection();
        selection.SetPreference(EndpointDirection.Playback, loaded.Settings.Playback.EndpointId);
        selection.SetPreference(EndpointDirection.Capture, loaded.Settings.Capture.EndpointId);
        Assert.Null(selection.Resolve(new DeviceCatalog([], []), EndpointDirection.Playback));
        Assert.Equal("missing-output", selection.PlaybackId);
        Assert.Null(selection.CaptureId);
        Assert.DoesNotContain("process", File.ReadAllText(directory.FilePath), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public void OptionalFieldsUseSafeDefaults()
    {
        var settings = SettingsJson.Decode(Encoding.UTF8.GetBytes("{\"schemaVersion\":1}"));
        Assert.True(settings.Playback.FollowDefault);
        Assert.True(settings.Capture.FollowDefault);
        Assert.Empty(settings.Endpoints);
        Assert.Null(settings.Window);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":\"1\"}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"playback\":null}")]
    [InlineData("{\"schemaVersion\":1,\"capture\":{\"followDefault\":false}}")]
    [InlineData("{\"schemaVersion\":1,\"playback\":{\"endpointId\":\"x\"}}")]
    [InlineData("{\"schemaVersion\":1,\"endpoints\":null}")]
    [InlineData("{\"schemaVersion\":1,\"endpoints\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"endpoints\":[{\"endpointId\":\"x\",\"direction\":\"invalid\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"endpoints\":[{\"endpointId\":\"x\",\"direction\":99}]}")]
    [InlineData("{\"schemaVersion\":1,\"endpoints\":[{\"endpointId\":\"x\",\"volumePercent\":101}]}")]
    [InlineData("{\"schemaVersion\":1,\"sessions\":[{\"pid\":42,\"instanceId\":\"ephemeral\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"window\":{\"width\":0,\"height\":600}}")]
    [InlineData("invalid json")]
    public async Task InvalidStructureIsProtectedAndRequiresExplicitRecovery(string json)
    {
        using var directory = new TemporarySettingsDirectory();
        byte[] original = Encoding.UTF8.GetBytes(json);
        File.WriteAllBytes(directory.FilePath, original);
        var store = new SettingsStore(directory.FilePath);
        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SettingsFileState.Corrupt, loaded.State);
        Assert.False(store.CanSave);
        Assert.False((await store.SaveAsync(Sample, TestContext.Current.CancellationToken)).Success);
        Assert.Equal(original, File.ReadAllBytes(directory.FilePath));
        Assert.True(loaded.Settings.Playback.FollowDefault);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(100)]
    public async Task IncompatibleVersionSurvivesSaveResetRecoveryAndCloseStyleSave(int version)
    {
        using var directory = new TemporarySettingsDirectory();
        byte[] original = Encoding.UTF8.GetBytes($"{{\"schemaVersion\":{version},\"future\":{{\"preserve\":true}}}}\r\n");
        File.WriteAllBytes(directory.FilePath, original);
        File.WriteAllBytes(directory.FilePath + ".bak", SettingsJson.Encode(Sample));
        var store = new SettingsStore(directory.FilePath);
        Assert.Equal(SettingsFileState.Unsupported, (await store.LoadAsync(TestContext.Current.CancellationToken)).State);
        Assert.False((await store.SaveAsync(Sample, TestContext.Current.CancellationToken)).Success);
        Assert.False((await store.ResetAsync(TestContext.Current.CancellationToken)).Success);
        Assert.False((await store.RecoverBackupAsync(TestContext.Current.CancellationToken)).Success);
        Assert.False((await store.SaveAsync(new StudioSettings { Window = new(0, 0, 800, 600) }, TestContext.Current.CancellationToken)).Success);
        Assert.Equal(original, File.ReadAllBytes(directory.FilePath));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task CorruptionRecoveryPreservesOriginalAndLastValidBackup()
    {
        using var directory = new TemporarySettingsDirectory();
        var store = new SettingsStore(directory.FilePath);
        await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.True((await store.SaveAsync(Sample, TestContext.Current.CancellationToken)).Success);
        Assert.True((await store.SaveAsync(new StudioSettings(), TestContext.Current.CancellationToken)).Success);
        byte[] backup = File.ReadAllBytes(store.BackupPath);
        byte[] corrupt = [0xff, 0x00, 0x7b];
        File.WriteAllBytes(directory.FilePath, corrupt);
        var reader = new SettingsStore(directory.FilePath);
        Assert.True((await reader.LoadAsync(TestContext.Current.CancellationToken)).CanRecoverBackup);
        Assert.Equal(corrupt, File.ReadAllBytes(directory.FilePath));
        Assert.False((await reader.SaveAsync(new StudioSettings(), TestContext.Current.CancellationToken)).Success);
        Assert.True((await reader.RecoverBackupAsync(TestContext.Current.CancellationToken)).Success);
        Assert.Equal(backup, File.ReadAllBytes(directory.FilePath));
        Assert.Equal(backup, File.ReadAllBytes(reader.BackupPath));
        Assert.Equal(corrupt, File.ReadAllBytes(Assert.Single(Directory.GetFiles(directory.Path, "*.corrupt-*.json"))));
        Assert.Equal(Sample.Playback, (await reader.LoadAsync(TestContext.Current.CancellationToken)).Settings.Playback);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidOrMissingBackupAllowsOnlyExplicitLocalReset(bool invalidBackup)
    {
        using var directory = new TemporarySettingsDirectory();
        File.WriteAllText(directory.FilePath, "broken");
        if (invalidBackup) File.WriteAllText(directory.FilePath + ".bak", "{\"schemaVersion\":99}");
        var store = new SettingsStore(directory.FilePath);
        Assert.False((await store.LoadAsync(TestContext.Current.CancellationToken)).CanRecoverBackup);
        Assert.False((await store.RecoverBackupAsync(TestContext.Current.CancellationToken)).Success);
        Assert.Equal("broken", File.ReadAllText(directory.FilePath));
        Assert.True((await store.ResetAsync(TestContext.Current.CancellationToken)).Success);
        Assert.Equal("broken", File.ReadAllText(Assert.Single(Directory.GetFiles(directory.Path, "*.corrupt-*.json"))));
        Assert.True((await store.LoadAsync(TestContext.Current.CancellationToken)).Settings.Playback.FollowDefault);
    }

    [Fact]
    public async Task MissingPrimaryWithValidBackupWaitsForExplicitChoice()
    {
        using var directory = new TemporarySettingsDirectory();
        byte[] backup = SettingsJson.Encode(Sample);
        File.WriteAllBytes(directory.FilePath + ".bak", backup);
        var store = new SettingsStore(directory.FilePath);
        Assert.True((await store.LoadAsync(TestContext.Current.CancellationToken)).CanRecoverBackup);
        Assert.False(store.CanSave);
        Assert.False((await store.SaveAsync(new StudioSettings(), TestContext.Current.CancellationToken)).Success);
        Assert.True((await store.RecoverBackupAsync(TestContext.Current.CancellationToken)).Success);
        Assert.Equal(backup, File.ReadAllBytes(directory.FilePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteAndReplaceFailuresKeepGoodPrimaryAndBackupAndCleanTemporary(bool failCommit)
    {
        using var directory = new TemporarySettingsDirectory();
        var faults = new FaultWriter();
        var store = new SettingsStore(directory.FilePath, faults);
        await store.LoadAsync(TestContext.Current.CancellationToken);
        await store.SaveAsync(Sample, TestContext.Current.CancellationToken);
        await store.SaveAsync(new StudioSettings(), TestContext.Current.CancellationToken);
        byte[] good = File.ReadAllBytes(directory.FilePath);
        byte[] backup = File.ReadAllBytes(store.BackupPath);
        if (failCommit) faults.BeforeCommit = () => throw new IOException("Disco cheio na substituição simulada.");
        else faults.BeforeWrite = () => throw new UnauthorizedAccessException("Permissão negada simulada.");
        Assert.False((await store.SaveAsync(Sample, TestContext.Current.CancellationToken)).Success);
        Assert.Equal(good, File.ReadAllBytes(directory.FilePath));
        Assert.Equal(backup, File.ReadAllBytes(store.BackupPath));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
        faults.BeforeWrite = faults.BeforeCommit = null;
        Assert.True((await store.SaveAsync(Sample, TestContext.Current.CancellationToken)).Success);
    }

    [Fact]
    public async Task ConcurrentSavesAreSerializedAndAnotherInstancesChangesAreProtected()
    {
        using var directory = new TemporarySettingsDirectory();
        var first = new SettingsStore(directory.FilePath);
        await first.LoadAsync(TestContext.Current.CancellationToken);
        var writes = Enumerable.Range(0, 20).Select(index => first.SaveAsync(new StudioSettings
        {
            Playback = new(false, "output-" + index)
        }, TestContext.Current.CancellationToken));
        Assert.All(await Task.WhenAll(writes), result => Assert.True(result.Success));
        SettingsJson.Decode(File.ReadAllBytes(first.BackupPath));
        var second = new SettingsStore(directory.FilePath);
        await second.LoadAsync(TestContext.Current.CancellationToken);
        Assert.True((await first.SaveAsync(Sample, TestContext.Current.CancellationToken)).Success);
        byte[] current = File.ReadAllBytes(directory.FilePath);
        Assert.False((await second.SaveAsync(new StudioSettings(), TestContext.Current.CancellationToken)).Success);
        Assert.Equal(SettingsFileState.Conflict, second.State);
        Assert.Equal(current, File.ReadAllBytes(directory.FilePath));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task ActiveFileLeaseRejectsAnotherWriterWithoutChangingFiles()
    {
        using var directory = new TemporarySettingsDirectory();
        var store = new SettingsStore(directory.FilePath);
        await store.LoadAsync(TestContext.Current.CancellationToken);
        await store.SaveAsync(Sample, TestContext.Current.CancellationToken);
        byte[] original = File.ReadAllBytes(directory.FilePath);
        using (var lease = new FileStream(directory.FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            Assert.False((await store.SaveAsync(new StudioSettings(), TestContext.Current.CancellationToken)).Success);
        Assert.Equal(original, File.ReadAllBytes(directory.FilePath));
        Assert.True((await store.SaveAsync(new StudioSettings(), TestContext.Current.CancellationToken)).Success);
    }

    [Fact]
    public async Task ExternalFutureVersionAndCancellationBeforeCommitKeepOriginal()
    {
        using var directory = new TemporarySettingsDirectory();
        var faults = new FaultWriter();
        var store = new SettingsStore(directory.FilePath, faults);
        await store.LoadAsync(TestContext.Current.CancellationToken);
        await store.SaveAsync(Sample, TestContext.Current.CancellationToken);
        byte[] original = File.ReadAllBytes(directory.FilePath);
        using var cancellation = new CancellationTokenSource();
        faults.AfterWrite = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new StudioSettings(), cancellation.Token));
        Assert.Equal(original, File.ReadAllBytes(directory.FilePath));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
        faults.AfterWrite = null;
        byte[] future = Encoding.UTF8.GetBytes("{\"schemaVersion\":500}");
        File.WriteAllBytes(directory.FilePath, future);
        Assert.False((await store.SaveAsync(new StudioSettings(), TestContext.Current.CancellationToken)).Success);
        Assert.Equal(future, File.ReadAllBytes(directory.FilePath));
    }

    [Fact]
    public async Task UnreadableFileAndPreCanceledLoadRemainSafe()
    {
        using var directory = new TemporarySettingsDirectory();
        File.WriteAllBytes(directory.FilePath, SettingsJson.Encode(Sample));
        var store = new SettingsStore(directory.FilePath);
        using (var locked = new FileStream(directory.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(SettingsFileState.Unreadable, (await store.LoadAsync(TestContext.Current.CancellationToken)).State);
            Assert.False((await store.SaveAsync(new StudioSettings(), TestContext.Current.CancellationToken)).Success);
        }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.LoadAsync(canceled.Token));
        Assert.Equal(Sample.Playback, (await store.LoadAsync(TestContext.Current.CancellationToken)).Settings.Playback);
    }

    [Fact]
    public async Task ExternalEditDuringTemporaryWriteIsDetectedBeforeReplacement()
    {
        using var directory = new TemporarySettingsDirectory();
        var faults = new FaultWriter();
        var store = new SettingsStore(directory.FilePath, faults);
        await store.LoadAsync(TestContext.Current.CancellationToken);
        await store.SaveAsync(Sample, TestContext.Current.CancellationToken);
        byte[] future = Encoding.UTF8.GetBytes("{\"schemaVersion\":9,\"external\":true}");
        faults.AfterWrite = () => File.WriteAllBytes(directory.FilePath, future);
        Assert.False((await store.SaveAsync(new StudioSettings(), TestContext.Current.CancellationToken)).Success);
        Assert.Equal(SettingsFileState.Conflict, store.State);
        Assert.Equal(future, File.ReadAllBytes(directory.FilePath));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Utf8BomIsAcceptedAndIncompatibleFileStillPreserved(int version)
    {
        using var directory = new TemporarySettingsDirectory();
        byte[] bytes = Encoding.UTF8.Preamble.ToArray().Concat(Encoding.UTF8.GetBytes($"{{\"schemaVersion\":{version}}}")).ToArray();
        File.WriteAllBytes(directory.FilePath, bytes);
        var store = new SettingsStore(directory.FilePath);
        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(version == 1 ? SettingsFileState.Ready : SettingsFileState.Unsupported, loaded.State);
        if (version == 2) Assert.False((await store.ResetAsync(TestContext.Current.CancellationToken)).Success);
        Assert.Equal(bytes, File.ReadAllBytes(directory.FilePath));
    }

    [Fact]
    public async Task OversizedFileIsPreservedWithSafeDefaultsInMemory()
    {
        using var directory = new TemporarySettingsDirectory();
        byte[] bytes = new byte[SettingsJson.MaximumBytes + 1];
        File.WriteAllBytes(directory.FilePath, bytes);
        var store = new SettingsStore(directory.FilePath);
        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SettingsFileState.Corrupt, loaded.State);
        Assert.True(loaded.Settings.Playback.FollowDefault);
        Assert.False((await store.SaveAsync(new StudioSettings(), TestContext.Current.CancellationToken)).Success);
        Assert.Equal(bytes, File.ReadAllBytes(directory.FilePath));
    }
}
