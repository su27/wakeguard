using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WakeGuard.IconGenerator;

internal static class Program
{
    private static readonly int[] ApplicationIconSizes = [16, 20, 24, 32, 48, 64, 128, 256];
    private static readonly int[] TrayIconSizes = [16, 20, 24, 32, 48, 64];

    private static readonly IconArtwork[] Artwork =
    [
        new(
            SourceFileName: "normal.png",
            OutputName: "TrayInactive"),
        new(
            SourceFileName: "awake.png",
            OutputName: "TrayKeepAwake"),
        new(
            SourceFileName: "light.png",
            OutputName: "TrayDisplayOn"),
    ];

    private static void Main(string[] args)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var sourceDirectory = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.Combine(projectRoot, "assets", "icon-source");
        var outputDirectory = args.Length > 1
            ? Path.GetFullPath(args[1])
            : Path.Combine(projectRoot, "src", "WakeGuard.Tray", "Assets");
        Directory.CreateDirectory(outputDirectory);

        foreach (var artwork in Artwork)
        {
            using var source = new Bitmap(Path.Combine(sourceDirectory, artwork.SourceFileName));
            var trayImages = TrayIconSizes
                .Select(size => RenderPng(source, size))
                .ToArray();
            WriteIcon(
                Path.Combine(outputDirectory, $"{artwork.OutputName}.ico"),
                TrayIconSizes,
                trayImages);
            File.WriteAllBytes(
                Path.Combine(outputDirectory, $"{artwork.OutputName}.png"),
                RenderPng(source, 256));
        }

        var awakeArtwork = Artwork.Single(item => item.OutputName == "TrayKeepAwake");
        using (var source = new Bitmap(Path.Combine(sourceDirectory, awakeArtwork.SourceFileName)))
        {
            var applicationImages = ApplicationIconSizes
                .Select(size => RenderPng(source, size))
                .ToArray();
            WriteIcon(
                Path.Combine(outputDirectory, "WakeGuard.ico"),
                ApplicationIconSizes,
                applicationImages);
            File.WriteAllBytes(
                Path.Combine(outputDirectory, "WakeGuard.png"),
                RenderPng(source, 512));
        }

        Console.WriteLine($"Icon sources: {sourceDirectory}");
        Console.WriteLine($"Generated assets: {outputDirectory}");
    }

    private static byte[] RenderPng(Bitmap source, int size)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.Clear(Color.Transparent);

        var padding = Math.Max(0.5f, size * 0.025f);
        var destination = new RectangleF(
            padding,
            padding,
            size - (2 * padding),
            size - (2 * padding));
        graphics.DrawImage(
            source,
            destination,
            new RectangleF(0, 0, source.Width, source.Height),
            GraphicsUnit.Pixel);

        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    private static void WriteIcon(string path, int[] sizes, byte[][] images)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Length);

        var offset = 6 + (16 * images.Length);
        for (var index = 0; index < images.Length; index++)
        {
            writer.Write((byte)(sizes[index] >= 256 ? 0 : sizes[index]));
            writer.Write((byte)(sizes[index] >= 256 ? 0 : sizes[index]));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)images[index].Length);
            writer.Write((uint)offset);
            offset += images[index].Length;
        }

        foreach (var image in images)
        {
            writer.Write(image);
        }
    }

    private sealed record IconArtwork(
        string SourceFileName,
        string OutputName);
}
