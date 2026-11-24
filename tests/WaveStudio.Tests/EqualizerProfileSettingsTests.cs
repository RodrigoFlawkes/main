using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WaveStudio.Audio;
using WaveStudio.Desktop.Settings;
using Xunit;

namespace WaveStudio.Tests;

public sealed class EqualizerProfileSettingsTests
{
    internal static EqualizerValues Values() => new(true, new double[] { -3, 2, 0, 0, 0, -9, 0, 0, 0, 0 }, -2.5);
    internal static EqualizerPreferences Named() => new EqualizerPreferences().WithCurrent(Values()).Create("Voz");
    [Fact]
    public void RoundTripPreservesCurrentDraftAndSavedProfileIndependently()
    {
        var named = Named(); var edited = named.WithCurrent(new(false, new double[10], -3));
        var model = new StudioSettings { Playback = new(false, "output"), Window = new(10, 20, 900, 700),
            Endpoints = [new("output", EndpointDirection.Playback, 25, true, -5)], Equalizer = edited };
        var bytes = SettingsJson.Encode(model); var restored = SettingsJson.Decode(bytes);
        Assert.Equal(StudioSettings.CurrentVersion, restored.SchemaVersion); Assert.True(edited.SameAs(restored.Equalizer)); Assert.True(restored.Equalizer.HasUnsavedChanges());
        Assert.True(restored.Equalizer.Profiles.Single().Values.Enabled); Assert.False(restored.Equalizer.Current.Enabled);
        Assert.Equal(model.Playback, restored.Playback); Assert.Equal(model.Endpoints, restored.Endpoints); Assert.Equal(model.Window, restored.Window);
        Assert.DoesNotContain("headroom", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clip", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("digitalLevel", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void ModelCopiesCollectionsAndCrudDoesNotOverwriteSavedDraftImplicitly()
    {
        var gains = new double[10]; var values = new EqualizerValues(false, gains, 0); gains[0] = 12;
        Assert.Equal(0, values.GainsDb[0]); Assert.Throws<NotSupportedException>(() => ((IList<double>)values.GainsDb)[0] = 1);
        var original = Named(); var id = original.SelectedProfileId;
        var changed = original.WithCurrent(new(false, new double[10], -4));
        Assert.True(changed.HasUnsavedChanges()); Assert.True(original.Current.Enabled); Assert.True(changed.Profiles.Single().Values.Enabled);
        var renamed = changed.Rename("Minha voz"); Assert.Equal(id, renamed.SelectedProfileId); Assert.True(renamed.HasUnsavedChanges());
        var saved = renamed.SaveSelected(); Assert.False(saved.HasUnsavedChanges()); Assert.Equal(id, saved.Profiles.Single().Id);
        var duplicated = saved.Create("Jogos"); Assert.Equal(2, duplicated.Profiles.Count); Assert.NotEqual(id, duplicated.SelectedProfileId);
        var applied = duplicated.Apply(id); Assert.Equal("Minha voz", applied.Profiles.Single(p => p.Id == id).Name);
        Assert.True(applied.Current.SameAs(saved.Current));
        var deleted = applied.DeleteSelected(); Assert.Null(deleted.SelectedProfileId); Assert.Single(deleted.Profiles);
        Assert.True(deleted.Current.SameAs(applied.Current)); Assert.True(deleted.HasUnsavedChanges());
        var neutral = deleted.Apply(null); Assert.False(neutral.Current.Enabled); Assert.All(neutral.Current.GainsDb, gain => Assert.Equal(0, gain));
        var array = original.Profiles.ToArray(); var copied = new EqualizerPreferences(original.Current, array, original.SelectedProfileId); array[0] = duplicated.Profiles[1];
        Assert.Equal(id, copied.Profiles.Single().Id); Assert.NotSame(original.Current, copied.Current);
        Assert.NotSame(original.Profiles[0].Values, copied.Profiles[0].Values);
    }
    [Theory]
    [InlineData("")] [InlineData("   ")] [InlineData("Padrão")] [InlineData("VOZ")]
    [InlineData("../Voz")] [InlineData("Voz\nOutra")] [InlineData("Voz\tOutra")] [InlineData("Ｖｏｚ")]
    public void FriendlyNamesRejectInvalidOrAmbiguousDuplicates(string name) =>
        Assert.Throws<ArgumentException>(() => Named().Create(name));
    [Fact]
    public void ProfileAndParameterBoundsAreEnforced()
    {
        Assert.Throws<ArgumentException>(() => Named().Create(new string('a', 49)));
        var model = new EqualizerPreferences(); for (int i = 0; i < 20; i++) model = model.Create($"Perfil {i}");
        Assert.Throws<ArgumentException>(() => model.Create("Outro"));
        Assert.Throws<ArgumentException>(() => new EqualizerValues(true, new double[9], 0));
        Assert.Throws<ArgumentException>(() => new EqualizerValues(true, Enumerable.Repeat(double.NaN, 10).ToArray(), 0));
        Assert.Throws<ArgumentException>(() => new EqualizerValues(true, Enumerable.Repeat(1.5, 10).ToArray(), 0));
        Assert.Throws<ArgumentException>(() => new EqualizerValues(true, new double[10], -.05));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EqualizerValues(true, new double[10], 1));
        Assert.Throws<ArgumentException>(() => new EqualizerProfile("bad", "Voz", Values()));
    }
    [Fact]
    public async Task V1MigrationReadsWithoutWritingAndNextSavePreservesAllExistingFields()
    {
        using var directory = new TemporarySettingsDirectory();
        byte[] legacy = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"playback\":{\"followDefault\":false,\"endpointId\":\"out\"},\"capture\":{\"followDefault\":true},\"window\":{\"x\":10,\"y\":20,\"width\":900,\"height\":700},\"endpoints\":[{\"endpointId\":\"out\",\"direction\":\"Playback\",\"volumePercent\":37,\"muted\":true,\"balance\":-8}]}");
        File.WriteAllBytes(directory.FilePath, legacy); var store = new SettingsStore(directory.FilePath);
        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SettingsFileState.Ready, loaded.State); Assert.Equal(StudioSettings.CurrentVersion, loaded.Settings.SchemaVersion); Assert.Equal(legacy, File.ReadAllBytes(directory.FilePath));
        Assert.False(loaded.Settings.Equalizer.Current.Enabled); Assert.Empty(loaded.Settings.Equalizer.Profiles);
        Assert.All(loaded.Settings.Equalizer.Current.GainsDb, gain => Assert.Equal(0, gain));
        var next = loaded.Settings with { Equalizer = Named() }; Assert.True((await store.SaveAsync(next, TestContext.Current.CancellationToken)).Success);
        Assert.Equal(legacy, File.ReadAllBytes(directory.FilePath + ".bak"));
        var actual = SettingsJson.Decode(File.ReadAllBytes(directory.FilePath));
        Assert.Equal(loaded.Settings.Playback, actual.Playback); Assert.Equal(loaded.Settings.Capture, actual.Capture);
        Assert.Equal(loaded.Settings.Window, actual.Window); Assert.Equal(loaded.Settings.Endpoints, actual.Endpoints); Assert.True(next.Equalizer.SameAs(actual.Equalizer));
    }
    [Fact]
    public void LegacyWindowDefaultsRemainCompatibleDuringMigration()
    {
        var migrated = SettingsJson.Decode(Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"window\":{\"width\":800,\"height\":600}}"));
        Assert.Equal(new WindowPlacement(0, 0, 800, 600), migrated.Window); Assert.False(migrated.Equalizer.Current.Enabled);
    }
    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task BackupRecoveryAcceptsBothSchemas(int version)
    {
        using var directory = new TemporarySettingsDirectory();
        var backup = version == 1 ? Encoding.UTF8.GetBytes("{\"schemaVersion\":1}") : SettingsJson.Encode(new() { Equalizer = Named() });
        File.WriteAllBytes(directory.FilePath, Encoding.UTF8.GetBytes("corrupt")); File.WriteAllBytes(directory.FilePath + ".bak", backup);
        var store = new SettingsStore(directory.FilePath); await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.True(store.CanRecoverBackup); Assert.True((await store.RecoverBackupAsync(TestContext.Current.CancellationToken)).Success);
        var actual = SettingsJson.Decode(File.ReadAllBytes(directory.FilePath)); Assert.Equal(StudioSettings.CurrentVersion, actual.SchemaVersion);
        Assert.Equal(version == 2, actual.Equalizer.Current.Enabled); Assert.Equal(backup, File.ReadAllBytes(directory.FilePath + ".bak"));
    }
    [Theory]
    [InlineData("missingEq")] [InlineData("nullEq")] [InlineData("oldVersionEq")] [InlineData("unknown")]
    [InlineData("short")] [InlineData("long")] [InlineData("range")] [InlineData("fraction")]
    [InlineData("preamp")] [InlineData("missingEnabled")] [InlineData("duplicate")] [InlineData("computed")]
    [InlineData("duplicateId")] [InlineData("duplicateName")] [InlineData("badSelected")]
    public void StrictJsonRejectsInvalidEqAndV2FieldsPretendingToBeV1(string mutation)
    {
        var root = JsonNode.Parse(SettingsJson.Encode(new() { Equalizer = Named() }))!.AsObject();
        var eq = root["equalizer"]!.AsObject(); var current = eq["current"]!.AsObject();
        switch (mutation)
        {
            case "missingEq": root.Remove("equalizer"); break;
            case "nullEq": root["equalizer"] = null; break;
            case "oldVersionEq": root["schemaVersion"] = 1; break;
            case "unknown": eq["clip"] = "not allowed"; break;
            case "computed": eq["hasUnsavedChanges"] = true; break;
            case "short": current["gainsDb"] = new JsonArray(0); break;
            case "long": current["gainsDb"]!.AsArray().Add(0); break;
            case "range": current["gainsDb"]![0] = 16; break;
            case "fraction": current["gainsDb"]![0] = 1.5; break;
            case "preamp": current["preampDb"] = 1; break;
            case "missingEnabled": current.Remove("enabled"); break;
            case "duplicateId": eq["profiles"]!.AsArray().Add(eq["profiles"]![0]!.DeepClone()); break;
            case "duplicateName": var profile = eq["profiles"]![0]!.DeepClone(); profile["id"] = Guid.NewGuid().ToString("N"); eq["profiles"]!.AsArray().Add(profile); break;
            case "badSelected": eq["selectedProfileId"] = Guid.NewGuid().ToString("N"); break;
        }
        string json = root.ToJsonString(); if (mutation == "duplicate") json = json.Replace("\"enabled\":true", "\"enabled\":true,\"enabled\":false");
        var error = Record.Exception(() => SettingsJson.Decode(Encoding.UTF8.GetBytes(json)));
        Assert.NotNull(error); Assert.True(SettingsJson.IsValidationError(error));
    }
    [Fact]
    public async Task ProfileSavesRespectMultiInstanceConflictsAndProtectedFutureFile()
    {
        using var directory = new TemporarySettingsDirectory(); var first = new SettingsStore(directory.FilePath); var second = new SettingsStore(directory.FilePath);
        await first.LoadAsync(TestContext.Current.CancellationToken); await second.LoadAsync(TestContext.Current.CancellationToken);
        Assert.True((await first.SaveAsync(new() { Equalizer = Named() }, TestContext.Current.CancellationToken)).Success);
        var saved = File.ReadAllBytes(directory.FilePath);
        Assert.False((await second.SaveAsync(new() { Equalizer = new() }, TestContext.Current.CancellationToken)).Success); Assert.Equal(saved, File.ReadAllBytes(directory.FilePath));
        byte[] future = Encoding.UTF8.GetBytes("{\"schemaVersion\":4,\"equalizer\":{\"anything\":true}}"); File.WriteAllBytes(directory.FilePath, future);
        var protectedStore = new SettingsStore(directory.FilePath); Assert.Equal(SettingsFileState.Unsupported, (await protectedStore.LoadAsync(TestContext.Current.CancellationToken)).State);
        Assert.False((await protectedStore.SaveAsync(new() { Equalizer = Named() }, TestContext.Current.CancellationToken)).Success);
        Assert.False((await protectedStore.ResetAsync(TestContext.Current.CancellationToken)).Success); Assert.Equal(future, File.ReadAllBytes(directory.FilePath));
    }
}
