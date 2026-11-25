using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class BakeChineseAtlas
{
    private const int Columns = 16;
    private const int CellWidth = 44;
    private const int CellHeight = 48;
    private const int EmSize = 32;
    private const int LeftPadding = 5;
    private const int Baseline = 34;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Bake(args);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static void Bake(string[] args)
    {
        var face = new GlyphTypeface(new Uri(Path.GetFullPath(args[0])));
        var ui = File.ReadAllText(args[1]);
        var characters = Enumerable.Range(32, 95).Select(value => (char)value)
            .Concat(ui.Where(character => character > 126 && !char.IsWhiteSpace(character)))
            .Distinct().OrderBy(character => character).ToArray();
        var width = Columns * CellWidth;
        var height = (characters.Length + Columns - 1) / Columns * CellHeight;
        var advances = new List<float>(characters.Length);
        var visual = new DrawingVisual();
        using (var graphics = visual.RenderOpen())
        {
            for (var slot = 0; slot < characters.Length; slot++)
            {
                var character = characters[slot];
                if (!face.CharacterToGlyphMap.TryGetValue(character, out var glyph) || glyph == 0)
                    throw new InvalidDataException($"Typeface has no glyph for U+{(int)character:X4}.");
                advances.Add((float)(face.AdvanceWidths[glyph] * EmSize));

                var outline = face.GetGlyphOutline(glyph, EmSize, EmSize);
                if (outline.Bounds.IsEmpty)
                {
                    if (character != ' ')
                        throw new InvalidDataException($"Glyph U+{(int)character:X4} has no outline.");
                    continue;
                }
                var bounds = outline.Bounds;
                if (bounds.Left + LeftPadding < 1 || bounds.Right + LeftPadding > CellWidth - 1 ||
                    bounds.Top + Baseline < 1 || bounds.Bottom + Baseline > CellHeight - 1)
                    throw new InvalidDataException($"Glyph U+{(int)character:X4} clips its cell: {bounds}.");

                var x = slot % Columns * CellWidth;
                var y = slot / Columns * CellHeight;
                graphics.PushTransform(new TranslateTransform(x + LeftPadding, y + Baseline));
                graphics.DrawGeometry(Brushes.White, null, outline);
                graphics.Pop();
            }
        }

        var render = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        render.Render(visual);
        var stride = width * 4;
        var bitmap = new byte[stride * height];
        render.CopyPixels(bitmap, stride, 0);
        var glyphPixels = new byte[bitmap.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (height - 1 - y) * stride + x * 4;
                glyphPixels[offset] = glyphPixels[offset + 1] = glyphPixels[offset + 2] = 255;
                glyphPixels[offset + 3] = bitmap[y * stride + x * 4 + 3];
            }
        }

        using (var output = File.Create(args[2]))
        using (var compressed = new GZipStream(output, CompressionLevel.Optimal))
        using (var binary = new BinaryWriter(compressed))
        {
            binary.Write(0x31415A4Bu);
            binary.Write(width);
            binary.Write(height);
            binary.Write(CellWidth);
            binary.Write(CellHeight);
            binary.Write(LeftPadding);
            binary.Write(EmSize);
            binary.Write(characters.Length);
            for (var slot = 0; slot < characters.Length; slot++)
            {
                binary.Write((ushort)characters[slot]);
                binary.Write(advances[slot]);
            }
            binary.Write(glyphPixels);
        }

        var preview = new PngBitmapEncoder();
        preview.Frames.Add(BitmapFrame.Create(render));
        using (var output = File.Create(args[3]))
            preview.Save(output);
        Console.WriteLine($"Rendered {characters.Length} glyphs in a {width}x{height} atlas.");
    }
}
