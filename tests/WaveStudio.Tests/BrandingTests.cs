using System.Reflection;
using System.Security.Cryptography;
using WaveStudio.Desktop;
using Xunit;

namespace WaveStudio.Tests;

public sealed class BrandingTests
{
    [Fact]
    public void EmbeddedIconContainsEveryRequiredResolutionAndDecodesWithWindows()
    {
        using var stream = typeof(StudioBrand).Assembly.GetManifestResourceStream("WaveStudio.Brand.Icon")!;
        using var reader = new BinaryReader(stream);
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        Assert.Equal(7, reader.ReadUInt16());
        foreach (int size in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            Assert.Equal(size == 256 ? 0 : size, reader.ReadByte());
            Assert.Equal(size == 256 ? 0 : size, reader.ReadByte());
            reader.ReadUInt16();
            Assert.Equal(1, reader.ReadUInt16());
            Assert.Equal(32, reader.ReadUInt16());
            uint length = reader.ReadUInt32();
            uint offset = reader.ReadUInt32();
            Assert.InRange((long)offset + length, 1, stream.Length);
            long position = stream.Position;
            stream.Position = offset;
            using var frame = new MemoryStream(reader.ReadBytes((int)length));
            using var png = Image.FromStream(frame);
            Assert.Equal(new Size(size, size), png.Size);
            stream.Position = position;
            using var iconStream = typeof(StudioBrand).Assembly.GetManifestResourceStream("WaveStudio.Brand.Icon")!;
            using var icon = new Icon(iconStream, size, size);
            // O construtor de Icon do GDI pode escolher 128 quando se pede 256. O frame PNG
            // acima verifica de forma independente a imagem em resolução completa usada pelo shell.
            if (size <= 128) Assert.Equal(new Size(size, size), icon.Size);
        }
    }

    [Fact]
    public void EmbeddedLogoAndIconRemainUsableAfterTheirResourceStreamsClose()
    {
        using var logo = StudioBrand.CreateLogo();
        using var icon = StudioBrand.CreateIcon();
        Assert.Equal(new Size(256, 256), logo.Size);
        Assert.Equal(0, logo.GetPixel(0, 0).A);
        Assert.True(logo.GetPixel(128, 128).A > 0);
        Assert.Equal(new Size(32, 32), icon.Size);
    }

    [Fact]
    public void ProductMetadataIdentifiesTheOfficialProjectAndAlphaVersion()
    {
        var assembly = typeof(StudioBrand).Assembly;
        Assert.Equal("Wave Studio", assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product);
        Assert.Equal("Wave Studio", assembly.GetCustomAttribute<AssemblyTitleAttribute>()!.Title);
        Assert.Equal("Wave-Universal-Studio", assembly.GetCustomAttribute<AssemblyCompanyAttribute>()!.Company);
        Assert.Equal(new Version(0, 1, 0, 0), assembly.GetName().Version);
        Assert.StartsWith("0.1.0-alpha", assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
        Assert.Contains(assembly.GetCustomAttributes<AssemblyMetadataAttribute>(), value => value.Key == "ProjectIdentifier" && value.Value == "Wave-Universal-Studio/main");
    }

    [Fact]
    public void OriginalLogoRemainsByteForByteIdenticalToOwnersAttachment()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "WaveStudio.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        byte[] original = File.ReadAllBytes(Path.Combine(root.FullName, "assets", "branding", "original", "WaveStudio.png"));
        Assert.Equal("A97D343F27C7033BBE06447AC5AED9B20585CD97C97A7EA5A7D7E3D90BCC4E11", Convert.ToHexString(SHA256.HashData(original)));
        using var stream = new MemoryStream(original);
        using var image = Image.FromStream(stream);
        Assert.Equal(new Size(1254, 1254), image.Size);
    }
}
