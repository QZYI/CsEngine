using Tiny3D.Graphics;
using Tiny3D.Scene;
using Tiny3D.Tools;

namespace Tiny3D.UI;

public enum TextAlignmentX
{
    Left,
    Center,
    Right
}

public enum TextAlignmentY
{
    Top,
    Center,
    Bottom
}

/// <summary>A screen-space rectangle that displays text using Tiny3D's built-in bitmap font.</summary>
public sealed class TextBox2D : UIElement
{
    private const int MaximumTextSize = 256;
    private int _textOpacity = byte.MaxValue;
    private int _maxGlyphsVisible = -1;
    private int _textSize = 16;

    [Inspect("Text")]
    public string TextContent { get; set; } = string.Empty;
    [Inspect("Text")]
    public int TextOpacity
    {
        get => _textOpacity;
        set => _textOpacity = (uint)value <= byte.MaxValue
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }
    [Inspect("Text")]
    public int TextGlyphSpacing { get; set; }
    [Inspect("Text")]
    public int TextLineSpacing { get; set; } = 20;
    [Inspect("Text")]
    public int MaxGlyphsVisible
    {
        get => _maxGlyphsVisible;
        set => _maxGlyphsVisible = value >= -1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }
    [Inspect("Text")]
    public int TextSize
    {
        get => _textSize;
        set => _textSize = value is > 0 and <= MaximumTextSize
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value),
                $"Text size must be between 1 and {MaximumTextSize}.");
    }
    [Inspect("Text")]
    public bool TextFitsBoundary { get; set; } = true;
    [Inspect("Text")]
    public bool TextWrapping { get; set; } = true;
    [Inspect("Text")]
    public TextAlignmentX TextAlignmentX { get; set; } = TextAlignmentX.Left;
    [Inspect("Text")]
    public TextAlignmentY TextAlignmentY { get; set; } = TextAlignmentY.Top;
    [Inspect("Text")]
    public Color32 TextColor { get; set; } = new(255, 255, 255);

    public TextAlignmentX HorizontalTextAlignment
    {
        get => TextAlignmentX;
        set => TextAlignmentX = value;
    }

    public TextAlignmentY VerticalTextAlignment
    {
        get => TextAlignmentY;
        set => TextAlignmentY = value;
    }

    protected override Construct CreateDuplicate()
    {
        var duplicate = new TextBox2D
        {
            TextContent = TextContent,
            TextOpacity = TextOpacity,
            TextGlyphSpacing = TextGlyphSpacing,
            TextLineSpacing = TextLineSpacing,
            MaxGlyphsVisible = MaxGlyphsVisible,
            TextSize = TextSize,
            TextFitsBoundary = TextFitsBoundary,
            TextWrapping = TextWrapping,
            TextAlignmentX = TextAlignmentX,
            TextAlignmentY = TextAlignmentY,
            TextColor = TextColor
        };
        CopyTo(duplicate);
        return duplicate;
    }
}
