namespace WaveStudio.Desktop.Settings;

internal readonly record struct WindowArea(int X, int Y, int Width, int Height);
internal sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized = false)
{
    public void Validate()
    {
        if (Width < 100 || Height < 100 || Width > 16384 || Height > 16384
            || Math.Abs((long)X) > 1000000 || Math.Abs((long)Y) > 1000000)
            throw new InvalidDataException("Tamanho ou posição de janela inválidos.");
    }

    public WindowPlacement Fit(IReadOnlyList<WindowArea> areas)
    {
        Validate();
        if (areas.Count == 0 || areas.Any(area => area.Width <= 0 || area.Height <= 0))
            throw new ArgumentException("É necessária uma área útil válida.", nameof(areas));
        var area = areas.OrderByDescending(Intersection).First();
        int width = Math.Clamp(Width, Math.Min(600, area.Width), area.Width);
        int height = Math.Clamp(Height, Math.Min(400, area.Height), area.Height);
        return this with
        {
            Width = width, Height = height,
            X = Math.Clamp(X, area.X, area.X + area.Width - width),
            Y = Math.Clamp(Y, area.Y, area.Y + area.Height - height)
        };
    }

    private long Intersection(WindowArea area) =>
        Math.Max(0L, Math.Min((long)X + Width, (long)area.X + area.Width) - Math.Max(X, area.X))
        * Math.Max(0L, Math.Min((long)Y + Height, (long)area.Y + area.Height) - Math.Max(Y, area.Y));
}
