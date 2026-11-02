using System.Runtime.InteropServices;

namespace Tiny3D.Tools;

internal sealed class UiGlyph(byte[] coverage, int width, int height)
{
    public byte[] Coverage { get; } = coverage;
    public int Width { get; } = width;
    public int Height { get; } = height;
}

internal sealed class UiGlyphAtlas(byte[] coverage, int width, int height, int resolution)
{
    public byte[] Coverage { get; } = coverage;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Resolution { get; } = resolution;
}

internal static class UiFont
{
    private const int GlyphWidth = 12;
    private const int GlyphHeight = 20;
    private const int AtlasColumns = 16;
    private const int AtlasRows = 6;
    private const int TextOriginY = 0;
    private static readonly object GlyphCacheLock = new();
    private static readonly Dictionary<int, IReadOnlyDictionary<char, UiGlyph>> GlyphCache = [];
    private static readonly Dictionary<int, UiGlyphAtlas> AtlasCache = [];
    private static int _fontResolution = 14;
    internal const int VerticalOffset = 0;

    public static void SetResolution(int resolution)
    {
        lock (GlyphCacheLock)
        {
            _fontResolution = resolution;
        }
    }

    public static IReadOnlyDictionary<char, UiGlyph> GetGlyphs()
    {
        lock (GlyphCacheLock)
        {
            if (!GlyphCache.TryGetValue(_fontResolution, out var glyphs))
            {
                glyphs = CreateGlyphs(_fontResolution);
                GlyphCache.Add(_fontResolution, glyphs);
            }

            return glyphs;
        }
    }

    public static UiGlyphAtlas GetGlyphAtlas()
    {
        lock (GlyphCacheLock)
        {
            if (!GlyphCache.TryGetValue(_fontResolution, out var glyphs))
            {
                glyphs = CreateGlyphs(_fontResolution);
                GlyphCache.Add(_fontResolution, glyphs);
            }

            if (AtlasCache.TryGetValue(_fontResolution, out var cachedAtlas))
            {
                return cachedAtlas;
            }

            var width = GlyphWidth * AtlasColumns;
            var height = GlyphHeight * AtlasRows;
            var coverage = new byte[width * height];
            foreach (var (character, glyph) in glyphs)
            {
                var glyphIndex = character - 32;
                var glyphX = glyphIndex % AtlasColumns * GlyphWidth;
                var glyphY = glyphIndex / AtlasColumns * GlyphHeight;
                for (var row = 0; row < glyph.Height; row++)
                {
                    Array.Copy(
                        glyph.Coverage,
                        row * glyph.Width,
                        coverage,
                        (glyphY + row) * width + glyphX,
                        glyph.Width);
                }
            }

            var atlas = new UiGlyphAtlas(coverage, width, height, _fontResolution);
            AtlasCache.Add(_fontResolution, atlas);
            return atlas;
        }
    }

    public static bool TryGetGlyphUv(char character, out float u0, out float v0, out float u1, out float v1)
    {
        if (character is < (char)32 or > (char)126)
        {
            u0 = v0 = u1 = v1 = 0;
            return false;
        }

        var glyphIndex = character - 32;
        u0 = (glyphIndex % AtlasColumns * GlyphWidth) / (float)(GlyphWidth * AtlasColumns);
        v0 = (glyphIndex / AtlasColumns * GlyphHeight) / (float)(GlyphHeight * AtlasRows);
        u1 = u0 + 1f / AtlasColumns;
        v1 = v0 + 1f / AtlasRows;
        return true;
    }

    private static IReadOnlyDictionary<char, UiGlyph> CreateGlyphs(int fontResolution)
    {
        var deviceContext = CreateCompatibleDC(IntPtr.Zero);
        if (deviceContext == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not create a device context for UI text rendering.");
        }

        var font = CreateFontW(
            -fontResolution, 0, 0, 0, 400, 0, 0, 0,
            1, 0, 0, 4, 0, "Consolas");
        if (font == IntPtr.Zero)
        {
            DeleteDC(deviceContext);
            throw new InvalidOperationException("Could not create the UI text font.");
        }

        var bitmapInfo = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = GlyphWidth,
                Height = -GlyphHeight,
                Planes = 1,
                BitCount = 32,
                Compression = 0
            }
        };
        var bitmap = CreateDIBSection(
            deviceContext,
            ref bitmapInfo,
            0,
            out var bitmapBits,
            IntPtr.Zero,
            0);
        if (bitmap == IntPtr.Zero || bitmapBits == IntPtr.Zero)
        {
            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }
            DeleteObject(font);
            DeleteDC(deviceContext);
            throw new InvalidOperationException("Could not create the UI text bitmap.");
        }

        var previousBitmap = SelectObject(deviceContext, bitmap);
        var previousFont = SelectObject(deviceContext, font);
        try
        {
            if (!IsValidGdiObject(previousBitmap) || !IsValidGdiObject(previousFont) ||
                SetBkMode(deviceContext, 1) == 0)
            {
                throw new InvalidOperationException("Could not configure the UI text device context.");
            }

            if (SetTextColor(deviceContext, 0x00FFFFFF) == uint.MaxValue)
            {
                throw new InvalidOperationException("Could not set the UI text color.");
            }

            var pixels = new int[GlyphWidth * GlyphHeight];
            var glyphs = new Dictionary<char, UiGlyph>();
            for (var code = 32; code <= 126; code++)
            {
                Array.Clear(pixels);
                Marshal.Copy(pixels, 0, bitmapBits, pixels.Length);
                var character = (char)code;
                if (!TextOutW(deviceContext, 0, TextOriginY, character.ToString(), 1))
                {
                    throw new InvalidOperationException($"Could not rasterize UI character '{character}'.");
                }

                Marshal.Copy(bitmapBits, pixels, 0, pixels.Length);
                var glyphCoverage = new byte[pixels.Length];
                for (var index = 0; index < pixels.Length; index++)
                {
                    glyphCoverage[index] = (byte)((pixels[index] >> 16) & 0xFF);
                }

                glyphs.Add(character, new UiGlyph(glyphCoverage, GlyphWidth, GlyphHeight));
            }

            return glyphs;
        }
        finally
        {
            if (IsValidGdiObject(previousFont))
            {
                SelectObject(deviceContext, previousFont);
            }
            if (IsValidGdiObject(previousBitmap))
            {
                SelectObject(deviceContext, previousBitmap);
            }
            DeleteObject(bitmap);
            DeleteObject(font);
            DeleteDC(deviceContext);
        }
    }

    private static bool IsValidGdiObject(IntPtr graphicsObject) =>
        graphicsObject != IntPtr.Zero && graphicsObject != new IntPtr(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFontW(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        uint italic,
        uint underline,
        uint strikeOut,
        uint characterSet,
        uint outputPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string faceName);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr deviceContext,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int SetBkMode(IntPtr deviceContext, int backgroundMode);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern uint SetTextColor(IntPtr deviceContext, uint color);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool TextOutW(
        IntPtr deviceContext,
        int x,
        int y,
        string text,
        int length);
}
