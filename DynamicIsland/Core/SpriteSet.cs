using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Core;

/// <summary>
/// The pet from the ChatGPT (Codex) app, cut from its own sprite sheet (<c>~/.codex/pets/&lt;name&gt;/spritesheet.webp</c>).
/// Sheet layout (sprite version 2): 8 columns of 192x208 cells; row 0 idle, row 3 waving, row 6 thinking, row 7 working at a laptop.
/// Only those frames are kept, so the big decoded sheet can be freed.
/// </summary>
public sealed class SpriteSet
{
    const int CellW = 192, CellH = 208;

    public IReadOnlyList<BitmapSource> Idle { get; }
    public IReadOnlyList<BitmapSource> Wave { get; }
    public IReadOnlyList<BitmapSource> Thinking { get; }
    public IReadOnlyList<BitmapSource> Working { get; }
    /// <summary>One neutral frame (used for the notch badge, and when nothing is animating).</summary>
    public BitmapSource Still => Idle[0];
    /// <summary>Eyes closed: used when the app isn't running.</summary>
    public BitmapSource Asleep => Idle[Math.Min(3, Idle.Count - 1)];

    SpriteSet(BitmapSource sheet)
    {
        Idle = Cut(sheet, 0, 7);
        Wave = Cut(sheet, 3, 4);
        Thinking = Cut(sheet, 6, 6);
        Working = Cut(sheet, 7, 6);
    }

    /// <summary>The user's pet, or null when the app has none (or its sheet isn't the layout this knows).</summary>
    public static SpriteSet? LoadFromCodex()
    {
        try
        {
            var pets = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "pets");
            if (!Directory.Exists(pets)) return null;

            foreach (var dir in new DirectoryInfo(pets).EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc))
            {
                var sheetPath = Path.Combine(dir.FullName, "spritesheet.webp");
                var manifest = Path.Combine(dir.FullName, "pet.json");
                if (!File.Exists(sheetPath)) continue;
                if (File.Exists(manifest))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                    if (doc.RootElement.TryGetProperty("spriteVersionNumber", out var v) && v.GetInt32() != 2) continue;
                }

                var decoder = BitmapDecoder.Create(new Uri(sheetPath), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                var sheet = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
                if (sheet.PixelWidth < CellW * 8 || sheet.PixelHeight < CellH * 8) continue;
                return new SpriteSet(sheet);
            }
        }
        catch { /* no pet, or no WebP support: the logo tile is used instead */ }
        return null;
    }

    static BitmapSource[] Cut(BitmapSource sheet, int row, int count)
    {
        var frames = new BitmapSource[count];
        for (var col = 0; col < count; col++)
        {
            var stride = CellW * 4;
            var pixels = new byte[stride * CellH];
            sheet.CopyPixels(new Int32Rect(col * CellW, row * CellH, CellW, CellH), pixels, stride, 0);

            // The sheet is drawn on solid black with no real transparency. Treat black as see-through: opacity = the brightest colour channel,
            // colours left as they are (premultiplied). Over a dark background this is identical to a cut-out.
            for (var i = 0; i < pixels.Length; i += 4)
                pixels[i + 3] = Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2]));

            var frame = BitmapSource.Create(CellW, CellH, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
            frame.Freeze();
            frames[col] = frame;
        }
        return frames;
    }
}
