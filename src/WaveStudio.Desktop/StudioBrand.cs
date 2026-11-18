namespace WaveStudio.Desktop;

internal static class StudioBrand
{
    public const string ProductName = "Wave Studio";

    // Cada chamador é responsável por seu clone. Os streams de recursos são fechados imediatamente.
    public static Icon CreateIcon()
    {
        using var stream = typeof(StudioBrand).Assembly.GetManifestResourceStream("WaveStudio.Brand.Icon")
            ?? throw new InvalidOperationException("Ícone oficial incorporado indisponível.");
        using var icon = new Icon(stream, new Size(32, 32));
        return (Icon)icon.Clone();
    }

    public static Bitmap CreateLogo()
    {
        using var stream = typeof(StudioBrand).Assembly.GetManifestResourceStream("WaveStudio.Brand.Logo")
            ?? throw new InvalidOperationException("Logo oficial incorporada indisponível.");
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
