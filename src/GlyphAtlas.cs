using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace KanzenChikan2.ProgressPanel;

internal sealed class GlyphAtlasData
{
    internal const string ResourceName = "KanzenChikan2.ProgressPanel.ChineseAtlas";
    private const uint FormatSignature = 0x31415A4B;

    internal int Width { get; }
    internal int Height { get; }
    internal int CellWidth { get; }
    internal int CellHeight { get; }
    internal int LeftPadding { get; }
    internal int EmSize { get; }
    internal Dictionary<char, (int Slot, float Advance)> Glyphs { get; }
    internal byte[] Pixels { get; }

    private GlyphAtlasData(BinaryReader reader)
    {
        if (reader.ReadUInt32() != FormatSignature)
            throw new InvalidDataException("Unsupported glyph atlas format.");
        Width = reader.ReadInt32();
        Height = reader.ReadInt32();
        CellWidth = reader.ReadInt32();
        CellHeight = reader.ReadInt32();
        LeftPadding = reader.ReadInt32();
        EmSize = reader.ReadInt32();
        var glyphCount = reader.ReadInt32();
        if (Width <= 0 || Width > 2048 || Height <= 0 || Height > 2048 ||
            CellWidth <= 0 || CellHeight <= 0 || Width % CellWidth != 0 || Height % CellHeight != 0 ||
            LeftPadding < 0 || LeftPadding >= CellWidth || EmSize <= 0 || EmSize > CellHeight ||
            glyphCount <= 0 || glyphCount > Width / CellWidth * (Height / CellHeight))
            throw new InvalidDataException("Invalid glyph atlas dimensions.");

        Glyphs = new Dictionary<char, (int, float)>(glyphCount);
        for (var slot = 0; slot < glyphCount; slot++)
        {
            var character = (char)reader.ReadUInt16();
            var advance = reader.ReadSingle();
            if (!float.IsFinite(advance) || advance <= 0f || advance > CellWidth ||
                !Glyphs.TryAdd(character, (slot, advance)))
                throw new InvalidDataException("Invalid or duplicate glyph in the Chinese atlas.");
        }

        var bytes = checked(Width * Height * 4);
        Pixels = reader.ReadBytes(bytes);
        if (Pixels.Length != bytes || reader.BaseStream.ReadByte() != -1 || !Glyphs.ContainsKey('?'))
            throw new InvalidDataException("Incomplete Chinese glyph atlas.");
    }

    internal static GlyphAtlasData Read(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"Embedded atlas {ResourceName} is missing.");
        using var inflater = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new BinaryReader(inflater);
        return new GlyphAtlasData(reader);
    }
}

internal sealed class GlyphAtlas
{
    private readonly Texture2D texture;
    private readonly Dictionary<char, (Rect Uv, float Advance)> glyphs;
    private readonly int cellWidth;
    private readonly int cellHeight;
    private readonly int leftPadding;
    private readonly int emSize;

    private GlyphAtlas(Texture2D texture, GlyphAtlasData data)
    {
        this.texture = texture;
        cellWidth = data.CellWidth;
        cellHeight = data.CellHeight;
        leftPadding = data.LeftPadding;
        emSize = data.EmSize;
        var columns = data.Width / cellWidth;
        glyphs = new Dictionary<char, (Rect, float)>(data.Glyphs.Count);
        foreach (var entry in data.Glyphs)
        {
            var column = entry.Value.Slot % columns;
            var row = entry.Value.Slot / columns;
            var uv = new Rect(column * cellWidth / (float)data.Width,
                (data.Height - (row + 1) * cellHeight) / (float)data.Height,
                cellWidth / (float)data.Width, cellHeight / (float)data.Height);
            glyphs.Add(entry.Key, (uv, entry.Value.Advance));
        }
    }

    internal static GlyphAtlas Create()
    {
        var data = GlyphAtlasData.Read(typeof(GlyphAtlas).Assembly);
        var texture = new Texture2D(data.Width, data.Height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        try
        {
            var pin = GCHandle.Alloc(data.Pixels, GCHandleType.Pinned);
            try { texture.LoadRawTextureData(pin.AddrOfPinnedObject(), data.Pixels.Length); }
            finally { pin.Free(); }
            texture.Apply(false, true);
            return new GlyphAtlas(texture, data);
        }
        catch
        {
            UnityEngine.Object.Destroy(texture);
            throw;
        }
    }

    internal void Destroy()
    {
        if (texture != null)
            UnityEngine.Object.Destroy(texture);
    }

    internal void Draw(Rect area, string value, float pointSize, Color tint, bool centered)
    {
        if (Event.current.type != EventType.Repaint || value.Length == 0)
            return;

        var scale = pointSize / emSize;
        var advances = 0f;
        foreach (var character in value)
            advances += GetGlyph(character).Advance;

        const float horizontalPadding = 4f;
        scale = MathF.Min(scale, (area.width - horizontalPadding * 2f) / advances);
        if (scale <= 0f)
            return;

        var x = centered ? area.x + (area.width - advances * scale) / 2f : area.x + horizontalPadding;
        var y = area.y + (area.height - cellHeight * scale) / 2f;
        var previous = GUI.color;
        GUI.color = new Color(previous.r * tint.r, previous.g * tint.g, previous.b * tint.b, previous.a * tint.a);
        try
        {
            foreach (var character in value)
            {
                var glyph = GetGlyph(character);
                if (character != ' ')
                {
                    var position = new Rect(x - leftPadding * scale, y, cellWidth * scale, cellHeight * scale);
                    GUI.DrawTextureWithTexCoords(position, texture, glyph.Uv, true);
                }
                x += glyph.Advance * scale;
            }
        }
        finally
        {
            GUI.color = previous;
        }
    }

    private (Rect Uv, float Advance) GetGlyph(char character) =>
        glyphs.TryGetValue(character, out var glyph) ? glyph : glyphs['?'];
}
