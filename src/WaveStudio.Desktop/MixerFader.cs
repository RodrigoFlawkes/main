namespace WaveStudio.Desktop;

internal sealed class MixerFader : UserControl
{
    private readonly System.Windows.Forms.Timer settle = new() { Interval = 180 };
    private readonly Label percent = new() { Dock = DockStyle.Bottom, TextAlign = ContentAlignment.MiddleCenter, Height = 22 };
    private double? confirmed;
    private bool dragging;
    private bool keyboardPending;

    internal TrackBar Slider { get; } = new()
    {
        Dock = DockStyle.Fill, Orientation = Orientation.Vertical, Minimum = 0, Maximum = 100,
        TickFrequency = 25, SmallChange = 1, LargeChange = 5, Enabled = false
    };
    internal event Action<int>? Committed;

    internal MixerFader()
    {
        Height = 152;
        Dock = DockStyle.Top;
        Controls.Add(Slider);
        Controls.Add(percent);
        Slider.ValueChanged += (_, _) => ShowPercent();
        Slider.MouseDown += (_, args) => { if (args.Button == MouseButtons.Left) BeginGesture(); };
        Slider.MouseUp += (_, args) => { if (args.Button == MouseButtons.Left) CommitGesture(); };
        Slider.MouseCaptureChanged += (_, _) =>
        {
            if (dragging && Slider.IsHandleCreated && !Slider.Capture)
                Slider.BeginInvoke((Action)(() => { if (!IsDisposed && dragging && !Slider.Capture) CancelEdit(); }));
        };
        Slider.Scroll += (_, _) =>
        {
            if (dragging || confirmed is null) return;
            keyboardPending = true;
            ShowPercent();
            settle.Stop();
            settle.Start();
        };
        Slider.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Escape) { CancelEdit(); args.Handled = args.SuppressKeyPress = true; }
        };
        settle.Tick += (_, _) => Commit();
        ShowPercent();
    }

    internal void BeginGesture()
    {
        if (confirmed is null || !Slider.Enabled) return;
        settle.Stop();
        keyboardPending = false;
        dragging = true;
    }

    internal void CommitGesture()
    {
        if (dragging) Commit();
    }

    internal void ShowVolume(double? value)
    {
        confirmed = value is >= 0 and <= 100 ? value : null;
        Slider.Enabled = confirmed.HasValue;
        if (!Slider.Enabled) CancelEdit();
        else if (!dragging && !keyboardPending) RestoreValue();
    }

    internal void CancelPendingEdit()
    {
        if (dragging || keyboardPending) CancelEdit();
    }

    internal void CancelEdit()
    {
        settle.Stop();
        dragging = keyboardPending = false;
        RestoreValue();
    }

    private void Commit()
    {
        settle.Stop();
        dragging = keyboardPending = false;
        if (Slider.Enabled && confirmed is double value && Slider.Value != Rounded(value))
            Committed?.Invoke(Slider.Value);
        else RestoreValue();
    }

    private static int Rounded(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private void RestoreValue()
    {
        Slider.Value = confirmed is double value ? Rounded(value) : 0;
        ShowPercent();
    }

    private void ShowPercent()
    {
        percent.Text = confirmed is null ? StudioStrings.Get("Mixer.Unavailable") :
            StudioStrings.Format("Mixer.Percent", dragging || keyboardPending ? Slider.Value : confirmed.Value);
        percent.AccessibleName = percent.Text;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            dragging = keyboardPending = false;
            settle.Dispose();
        }
        base.Dispose(disposing);
    }
}
