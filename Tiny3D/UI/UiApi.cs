using Tiny3D.Graphics;
using Tiny3D.Input;
using Tiny3D.Math;
using Tiny3D.Scene;
using Tiny3D.Tools;
using SceneGraph = Tiny3D.Scene.Scene;

namespace Tiny3D.UI;

/// <summary>Owns and renders screen-space UI elements belonging to a scene.</summary>
public sealed class UiApi
{
    private readonly SceneGraph _scene;
    private readonly List<ResolvedElement> _elements = [];
    private readonly HashSet<Key> _seenKeyPresses = [];
    private readonly HashSet<Key> _seenKeyReleases = [];
    private readonly Dictionary<Key, UIElement> _keyRecipients = [];
    private UIElement? _hoveredElement;
    private long _sequence;

    internal UiApi(SceneGraph scene) => _scene = scene;

    /// <summary>Adds a UI element as a scene root.</summary>
    public void Add(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        _scene.Add(element);
    }

    /// <summary>Removes a UI element from its parent or scene root.</summary>
    public bool Remove(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.Branch is { } parent)
        {
            return parent.RemoveDescendant(element);
        }

        return _scene.Remove(element);
    }

    /// <summary>Updates pointer hover and keyboard events for the current viewport.</summary>
    public void Update(InputState input, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateSize(width, height);
        ResolveElements(width, height);

        UIElement? hovered = null;
        foreach (var resolved in _elements)
        {
            if (resolved.Clip.Contains(input.MouseX, input.MouseY) &&
                Contains(resolved, input.MouseX, input.MouseY))
            {
                hovered = resolved.Element;
            }
        }

        if (!ReferenceEquals(hovered, _hoveredElement))
        {
            _hoveredElement?.RaiseMouseExit();
            hovered?.RaiseMouseHover();
            _hoveredElement = hovered;
        }

        _seenKeyPresses.RemoveWhere(key => !input.WasKeyPressed(key));
        _seenKeyReleases.RemoveWhere(key => !input.WasKeyReleased(key));

        foreach (var key in input.PressedKeys)
        {
            if (_seenKeyPresses.Add(key) && hovered is not null)
            {
                _keyRecipients[key] = hovered;
                hovered.RaiseButtonDown(key);
            }
        }
        foreach (var key in input.ReleasedKeys)
        {
            if (_seenKeyReleases.Add(key) &&
                _keyRecipients.Remove(key, out var recipient) &&
                !recipient.IsDisposed &&
                ReferenceEquals(recipient.OwningScene, _scene))
            {
                recipient.RaiseButtonUp(key);
            }
        }
    }

    /// <summary>Draws the scene's UI into a caller-provided ARGB pixel buffer.</summary>
    public void Render(int[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ValidateSize(width, height);
        if (pixels.Length != checked(width * height))
        {
            throw new ArgumentException("The pixel buffer dimensions do not match the UI dimensions.", nameof(pixels));
        }

        UiFont.SetResolution(14);
        Render(new SoftwareUiDrawTarget(pixels, width, height), width, height);
    }

    internal void Render(IUiDrawTarget target, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateSize(width, height);
        ResolveElements(width, height);

        foreach (var resolved in _elements)
        {
            if (!resolved.Clip.HasArea)
            {
                continue;
            }

            target.PushClipRect(resolved.Clip.X, resolved.Clip.Y, resolved.Clip.Width, resolved.Clip.Height);
            DrawElement(target, resolved);
            target.PopClipRect();
        }
    }

    private void ResolveElements(int width, int height)
    {
        _elements.Clear();
        _sequence = 0;
        var viewport = new UiRect(0, 0, width, height);
        foreach (var root in _scene.Roots)
        {
            Collect(root, viewport, viewport, visible: true);
        }

        _elements.Sort(static (left, right) =>
        {
            var layerOrder = left.Element.Layer.CompareTo(right.Element.Layer);
            return layerOrder != 0 ? layerOrder : left.Sequence.CompareTo(right.Sequence);
        });
    }

    private void Collect(Construct construct, UiRect parentBounds, UiRect inheritedClip, bool visible)
    {
        visible &= construct.Visible;
        if (!visible)
        {
            return;
        }

        var bounds = parentBounds;
        var clip = inheritedClip;
        if (construct is UIElement element)
        {
            if (!element.Rendered)
            {
                return;
            }

            var size = element.Scale.Resolve(parentBounds.Width, parentBounds.Height);
            var position = element.Position.Resolve(parentBounds.Width, parentBounds.Height);
            var anchor = element.AnchorPoint.Resolve(size.X, size.Y);
            bounds = new UiRect(
                ToCoordinate(parentBounds.X + position.X - anchor.X),
                ToCoordinate(parentBounds.Y + position.Y - anchor.Y),
                ToDimension(size.X),
                ToDimension(size.Y));
            _elements.Add(new ResolvedElement(element, bounds, clip, _sequence++));
            if (element.ClipDescendants)
            {
                clip = UiRect.Intersect(clip, bounds);
            }
        }

        foreach (var child in construct.Descendants)
        {
            Collect(child, bounds, clip, visible);
        }
    }

    private static void DrawElement(IUiDrawTarget target, ResolvedElement resolved)
    {
        var element = resolved.Element;
        var bounds = resolved.Bounds;
        if (!bounds.HasArea)
        {
            return;
        }

        if (element.Rotation != 0)
        {
            target.PushRotation(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f,
                element.Rotation % 360 * (MathF.PI / 180));
        }
        var radius = element.CornerRadius.Resolve(bounds.Width, bounds.Height);
        var radiusX = ToDimension(radius.X);
        var radiusY = ToDimension(radius.Y);
        var inset = System.Math.Min(element.OutlineThickness, System.Math.Min(bounds.Width, bounds.Height) / 2);
        if (inset > 0)
        {
            DrawRoundedRectangle(
                target, bounds, radiusX, radiusY,
                ApplyOpacity(element.OutlineColor, element.Opacity), element.SSAAStrength);
        }
        var inner = new UiRect(
            bounds.X + inset,
            bounds.Y + inset,
            bounds.Width - inset * 2,
            bounds.Height - inset * 2);
        DrawRoundedRectangle(
            target,
            inner,
            System.Math.Max(0, radiusX - inset),
            System.Math.Max(0, radiusY - inset),
            ApplyOpacity(element.Color, element.Opacity), element.SSAAStrength);

        if (element is not TextBox2D textBox || string.IsNullOrEmpty(textBox.TextContent))
        {
            if (element.Rotation != 0)
            {
                target.PopRotation();
            }
            return;
        }

        var clip = element.Rotation == 0 ? bounds : resolved.Clip;
        if (textBox.TextFitsBoundary)
        {
            target.PushClipRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }
        else
        {
            target.PushClipRect(clip.X, clip.Y, clip.Width, clip.Height);
        }
        DrawText(target, textBox, bounds);
        target.PopClipRect();
        if (element.Rotation != 0)
        {
            target.PopRotation();
        }
    }

    private static void DrawText(IUiDrawTarget target, TextBox2D textBox, UiRect bounds)
    {
        var color = ApplyOpacity(textBox.TextColor, textBox.Opacity);
        color = ApplyOpacity(color, textBox.TextOpacity);
        var baseScale = textBox.TextSize / 16f;
        var lines = BuildTextLines(textBox, bounds.Width, baseScale);
        var lineHeight = MathF.Max(1, textBox.TextLineSpacing * baseScale);
        var availableHeight = bounds.Height;
        var longestLine = lines.Count == 0 ? 0 :
            lines.Max(line => line.Length * (UiCanvas.CharacterAdvance * baseScale + textBox.TextGlyphSpacing));
        var scale = baseScale;
        if (textBox.TextFitsBoundary)
        {
            var widthScale = longestLine > 0 ? bounds.Width / longestLine : 1f;
            var heightScale = lines.Count > 0 ? availableHeight / (lines.Count * lineHeight) : 1f;
            scale *= MathF.Min(1f, MathF.Min(widthScale, heightScale));
            lineHeight *= scale / baseScale;
        }

        var totalHeight = lines.Count * lineHeight;
        var cursorY = bounds.Y + (textBox.TextAlignmentY switch
        {
            TextAlignmentY.Center => (bounds.Height - totalHeight) / 2,
            TextAlignmentY.Bottom => bounds.Height - totalHeight,
            _ => 0
        });
        var glyphCount = 0;
        foreach (var line in lines)
        {
            var lineWidth = line.Length * (UiCanvas.CharacterAdvance * scale + textBox.TextGlyphSpacing);
            var cursorX = bounds.X + (textBox.TextAlignmentX switch
            {
                TextAlignmentX.Center => (bounds.Width - lineWidth) / 2,
                TextAlignmentX.Right => bounds.Width - lineWidth,
                _ => 0
            });
            foreach (var character in line)
            {
                if (textBox.MaxGlyphsVisible >= 0 && glyphCount >= textBox.MaxGlyphsVisible)
                {
                    return;
                }
                target.DrawText((int)MathF.Round(cursorX), (int)MathF.Round(cursorY),
                    character.ToString(), color, scale);
                cursorX += UiCanvas.CharacterAdvance * scale + textBox.TextGlyphSpacing;
                glyphCount++;
            }
            cursorY += lineHeight;
        }
    }

    private static List<string> BuildTextLines(TextBox2D textBox, int maxWidth, float scale)
    {
        var lines = new List<string>();
        var advance = UiCanvas.CharacterAdvance * scale + textBox.TextGlyphSpacing;
        var maxCharacters = textBox.TextWrapping && advance > 0
            ? System.Math.Max(1, (int)MathF.Floor(maxWidth / advance))
            : int.MaxValue;
        foreach (var sourceLine in textBox.TextContent.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            if (!textBox.TextWrapping || sourceLine.Length <= maxCharacters)
            {
                lines.Add(sourceLine);
                continue;
            }

            var remaining = sourceLine;
            while (remaining.Length > maxCharacters)
            {
                var breakAt = remaining.LastIndexOf(' ', maxCharacters - 1, maxCharacters);
                if (breakAt <= 0)
                {
                    breakAt = maxCharacters;
                }
                lines.Add(remaining[..breakAt].TrimEnd());
                remaining = remaining[breakAt..].TrimStart();
            }
            lines.Add(remaining);
        }
        return lines;
    }

    private static bool Contains(ResolvedElement resolved, int x, int y)
    {
        var element = resolved.Element;
        if (element.Rotation == 0)
        {
            return resolved.Bounds.Contains(x, y);
        }

        var centerX = resolved.Bounds.X + resolved.Bounds.Width / 2f;
        var centerY = resolved.Bounds.Y + resolved.Bounds.Height / 2f;
        var radians = -element.Rotation * (MathF.PI / 180);
        var dx = x + 0.5f - centerX;
        var dy = y + 0.5f - centerY;
        var localX = centerX + dx * MathF.Cos(radians) - dy * MathF.Sin(radians);
        var localY = centerY + dx * MathF.Sin(radians) + dy * MathF.Cos(radians);
        return resolved.Bounds.Contains((int)MathF.Floor(localX), (int)MathF.Floor(localY));
    }

    private static void DrawRoundedRectangle(
        IUiDrawTarget target,
        UiRect bounds,
        int radiusX,
        int radiusY,
        Color32 color,
        float ssaaStrength)
    {
        if (!bounds.HasArea)
        {
            return;
        }

        radiusX = System.Math.Clamp(radiusX, 0, bounds.Width / 2);
        radiusY = System.Math.Clamp(radiusY, 0, bounds.Height / 2);
        var firstY = System.Math.Max(0, bounds.Y);
        var endY = System.Math.Min(target.Height, bounds.Bottom);
        if (ssaaStrength > 0)
        {
            DrawAntialiasedRoundedRectangle(
                target, bounds, radiusX, radiusY, color, 1 + (int)MathF.Round(ssaaStrength * 7));
            return;
        }

        for (var y = firstY; y < endY; y++)
        {
            var localY = y - bounds.Y;
            var edgeDistance = localY < radiusY
                ? radiusY - localY - 0.5f
                : localY >= bounds.Height - radiusY
                    ? localY - (bounds.Height - radiusY) + 0.5f
                    : 0;
            var inset = 0;
            if (edgeDistance > 0 && radiusY > 0 && edgeDistance < radiusY)
            {
                inset = (int)MathF.Ceiling(radiusX *
                    (1 - MathF.Sqrt(1 - edgeDistance * edgeDistance / (radiusY * radiusY))));
            }

            target.FillRect(
                bounds.X + inset,
                y,
                bounds.Width - inset * 2,
                1,
                color);
        }
    }

    private static void DrawAntialiasedRoundedRectangle(
        IUiDrawTarget target,
        UiRect bounds,
        int radiusX,
        int radiusY,
        Color32 color,
        int sampleCount)
    {
        var firstY = System.Math.Max(0, bounds.Y);
        var endY = System.Math.Min(target.Height, bounds.Bottom);
        for (var y = firstY; y < endY; y++)
        {
            var insetSum = 0f;
            for (var sample = 0; sample < sampleCount; sample++)
            {
                var localY = y - bounds.Y + (sample + 0.5f) / sampleCount;
                var edgeDistance = localY < radiusY
                    ? radiusY - localY
                    : localY >= bounds.Height - radiusY
                        ? localY - (bounds.Height - radiusY)
                        : 0;
                var inset = edgeDistance > 0 && radiusY > 0 && edgeDistance < radiusY
                    ? radiusX * (1 - MathF.Sqrt(1 - edgeDistance * edgeDistance / (radiusY * radiusY)))
                    : 0;
                insetSum += inset;
            }

            var insetAverage = insetSum / sampleCount;
            var left = bounds.X + insetAverage;
            var right = bounds.Right - insetAverage;
            var leftPixel = (int)MathF.Floor(left);
            var rightPixel = (int)MathF.Floor(right);
            if (leftPixel == rightPixel)
            {
                var coverage = System.Math.Clamp(right - left, 0, 1);
                FillCoverage(target, leftPixel, y, color, coverage);
                continue;
            }

            var leftCoverage = 1 - (left - leftPixel);
            var rightCoverage = right - rightPixel;
            if (leftCoverage < 1)
            {
                FillCoverage(target, leftPixel, y, color, leftCoverage);
            }
            var firstFullPixel = (int)MathF.Ceiling(left);
            var endFullPixel = (int)MathF.Floor(right);
            if (firstFullPixel < endFullPixel)
            {
                target.FillRect(firstFullPixel, y, endFullPixel - firstFullPixel, 1, color);
            }
            if (rightCoverage > 0)
            {
                FillCoverage(target, rightPixel, y, color, rightCoverage);
            }
        }
    }

    private static void FillCoverage(IUiDrawTarget target, int x, int y, Color32 color, float coverage)
    {
        if (coverage <= 0)
        {
            return;
        }
        var alpha = (byte)MathF.Round(color.A * System.Math.Clamp(coverage, 0, 1));
        target.FillRect(x, y, 1, 1, color with { A = alpha });
    }

    private static Color32 ApplyOpacity(Color32 color, int opacity) =>
        color with { A = (byte)((color.A * opacity + 127) / byte.MaxValue) };

    private static int ToCoordinate(float value)
    {
        if (!float.IsFinite(value) || (double)value < int.MinValue || (double)value > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Resolved UI coordinates must be finite and fit in an integer.");
        }

        return (int)MathF.Round(value);
    }

    private static int ToDimension(float value)
    {
        if (!float.IsFinite(value) || value < 0 || (double)value > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Resolved UI dimensions must be finite and non-negative.");
        }

        return (int)MathF.Round(value);
    }

    private static void ValidateSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "UI dimensions must be positive.");
        }
    }

    private readonly record struct ResolvedElement(UIElement Element, UiRect Bounds, UiRect Clip, long Sequence);

    private readonly record struct UiRect(int X, int Y, int Width, int Height)
    {
        public int Right => (int)System.Math.Clamp((long)X + Width, int.MinValue, int.MaxValue);
        public int Bottom => (int)System.Math.Clamp((long)Y + Height, int.MinValue, int.MaxValue);
        public bool HasArea => Width > 0 && Height > 0;
        public bool Contains(int x, int y) => HasArea && x >= X && x < Right && y >= Y && y < Bottom;

        public static UiRect Intersect(UiRect left, UiRect right)
        {
            var x = System.Math.Max(left.X, right.X);
            var y = System.Math.Max(left.Y, right.Y);
            var rightEdge = System.Math.Min(left.Right, right.Right);
            var bottomEdge = System.Math.Min(left.Bottom, right.Bottom);
            return new UiRect(
                x,
                y,
                (int)System.Math.Clamp((long)rightEdge - x, 0, int.MaxValue),
                (int)System.Math.Clamp((long)bottomEdge - y, 0, int.MaxValue));
        }
    }
}
