using System.Diagnostics;
using WaveStudio.Audio;

namespace WaveStudio.Desktop;

internal enum MixerGroup { Ungrouped, Game, Chat, Music, Media, System, Auxiliary }

internal sealed class MixerStrip : GroupBox
{
    private readonly Label detail = new() { Dock = DockStyle.Top, AutoEllipsis = true, Height = 28 };
    private readonly Label peakText = new() { Dock = DockStyle.Top, AutoEllipsis = true, Height = 22 };
    private readonly ProgressBar peak = new() { Dock = DockStyle.Top, Minimum = 0, Maximum = 100, Height = 11 };
    private readonly ComboBox? group;
    internal MixerFader Fader { get; } = new();
    internal Button Mute { get; } = new() { Dock = DockStyle.Top, Height = 29, Enabled = false };
    internal ApplicationSessionBinding? Binding { get; private set; }
    internal MixerGroup Group => (group?.SelectedItem as GroupChoice)?.Group ?? MixerGroup.Ungrouped;
    internal event Action? GroupChanged;

    internal MixerStrip(bool master = false)
    {
        Width = 156;
        Height = 310;
        Padding = new Padding(8, 4, 8, 6);
        if (!master)
        {
            group = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = StudioStrings.Get("Mixer.ChannelGroup") };
            foreach (var category in Enum.GetValues<MixerGroup>()) group.Items.Add(new GroupChoice(category));
            group.SelectedIndex = 0;
            group.SelectedIndexChanged += (_, _) => GroupChanged?.Invoke();
        }
        if (group is not null) Controls.Add(group);
        Controls.Add(Mute);
        Controls.Add(peakText);
        Controls.Add(peak);
        Controls.Add(Fader);
        Controls.Add(detail);
        peak.AccessibleName = StudioStrings.Get(master ? "Mixer.DevicePeak" : "Mixer.SessionPeak");
        detail.AccessibleRole = AccessibleRole.StaticText;
        if (master) SetTitle(StudioStrings.Get("Mixer.DeviceMaster"));
        ShowMute(null);
        ShowPeak(null, 0, 0);
    }

    internal void ShowSession(ApplicationSessionBinding current, string state, int channel)
    {
        Binding = current;
        var snapshot = current.Snapshot;
        SetTitle(snapshot.Name);
        AccessibleName = StudioStrings.Format("Mixer.ChannelTitle", channel, snapshot.Name);
        Fader.Slider.AccessibleName = StudioStrings.Format("Mixer.FaderName", AccessibleName);
        detail.Text = StudioStrings.Format("Mixer.ChannelState", channel, state);
        detail.AccessibleDescription = snapshot.Error;
        bool available = snapshot.Activity != SessionActivity.Expired;
        if (group is not null) group.Enabled = available;
        Fader.ShowVolume(available ? snapshot.VolumePercent : null);
        ShowMute(available ? snapshot.Muted : null);
    }

    internal void ShowMaster(EndpointVolumeState? levels)
    {
        detail.Text = StudioStrings.Get("Mixer.EndpointVolume");
        detail.AccessibleDescription = levels?.VolumeError ?? levels?.MuteError;
        Fader.ShowVolume(levels?.VolumeError is null ? levels?.VolumePercent : null);
        ShowMute(levels?.MuteError is null ? levels?.Muted : null);
    }

    internal void ShowPeak(PeakReading? sample, long recordedAt, long now, bool paused = false)
    {
        bool fresh = recordedAt > 0 && now >= recordedAt && Stopwatch.GetElapsedTime(recordedAt, now) <= TimeSpan.FromSeconds(2);
        bool available = !paused && fresh && sample?.Scalar is >= 0 and <= 1;
        peak.Value = available ? (int)Math.Round(sample!.Scalar!.Value * 100) : 0;
        peak.Enabled = available;
        var value = paused ? StudioStrings.Get("ApplicationMixerPanel.Pausado")
            : sample?.Scalar is null ? StudioStrings.Get("Mixer.Unavailable")
            : !fresh ? StudioStrings.Get("ApplicationMixerPanel.LeituraAtrasada")
            : StudioStrings.Format("Mixer.Percent", sample.Scalar.Value * 100);
        peakText.Text = StudioStrings.Format("Mixer.PeakStatus", peak.AccessibleName, value);
        peak.AccessibleDescription = peakText.Text;
    }

    private void SetTitle(string name)
    {
        if (Text != name) Text = name;
        AccessibleName = name;
        Fader.Slider.AccessibleName = StudioStrings.Format("Mixer.FaderName", name);
    }

    private void ShowMute(bool? muted)
    {
        Mute.Enabled = muted.HasValue;
        Mute.Text = StudioStrings.Get(muted switch { true => "ApplicationMixerPanel.DesativarMute",
            false => "ApplicationMixerPanel.AtivarMute", _ => "Mixer.Unavailable" });
        Mute.AccessibleName = StudioStrings.Format("Mixer.MuteName", Text, Mute.Text);
    }

    internal static string GroupName(MixerGroup group) => StudioStrings.Get(group switch
    {
        MixerGroup.Game => "Mixer.GroupGame",
        MixerGroup.Chat => "Mixer.GroupChat",
        MixerGroup.Music => "Mixer.GroupMusic",
        MixerGroup.Media => "Mixer.GroupMedia",
        MixerGroup.System => "Mixer.GroupSystem",
        MixerGroup.Auxiliary => "Mixer.GroupAuxiliary",
        _ => "Mixer.GroupUngrouped"
    });

    private sealed record GroupChoice(MixerGroup Group)
    {
        public override string ToString() => GroupName(Group);
    }
}
