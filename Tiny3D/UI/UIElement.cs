using Tiny3D.Graphics;
using Tiny3D.Input;
using Tiny3D.Math;
using Tiny3D.Scene;
using Tiny3D.Tools;

namespace Tiny3D.UI;

/// <summary>Base class for screen-space UI constructs.</summary>
public abstract class UIElement : Construct
{
    private int _opacity = byte.MaxValue;
    private int _outlineThickness;
    private float _ssaaStrength = 1f;

    protected UIElement()
    {
    }

    [Inspect("UI")]
    public UIVector2 Scale { get; set; } = new(Vector2.Zero, new Vector2(100, 50));
    [Inspect("UI")]
    public UIVector2 Position { get; set; } = UIVector2.Zero;
    [Inspect("UI")]
    public int Opacity
    {
        get => _opacity;
        set => _opacity = ValidateByte(value, nameof(value));
    }
    [Inspect("UI")]
    public UIVector2 CornerRadius { get; set; } = UIVector2.Zero;
    [Inspect("UI")]
    public Color32 Color { get; set; } = new(240, 240, 240);
    [Inspect("UI")]
    public int OutlineThickness
    {
        get => _outlineThickness;
        set => _outlineThickness = value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }
    [Inspect("UI")]
    public Color32 OutlineColor { get; set; } = new(20, 20, 20);
    [Inspect("UI")]
    public UIVector2 AnchorPoint { get; set; } = UIVector2.Zero;
    [Inspect("UI")]
    public float SSAAStrength
    {
        get => _ssaaStrength;
        set => _ssaaStrength = value is >= 0f and <= 1f
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Anti-aliasing strength must be between 0 and 1.");
    }
    [Inspect("UI")]
    public bool AntiAliasing
    {
        get => SSAAStrength > 0f;
        set => SSAAStrength = value ? 1f : 0f;
    }
    [Inspect("UI")]
    public float AntiAliasingStrength
    {
        get => SSAAStrength;
        set => SSAAStrength = ValidateSsaStrength(value, nameof(value));
    }
    [Inspect("UI")]
    public int Rotation { get; set; }
    [Inspect("UI")]
    public bool Rendered { get; set; } = true;
    [Inspect("UI")]
    public int Layer { get; set; }
    [Inspect("UI")]
    public bool ClipDescendants { get; set; }

    /// <summary>Raised once when a keyboard key is pressed while the pointer hovers this element.</summary>
    public event Action<Key>? OnButtonDown;
    /// <summary>Raised once when a keyboard key is released while the pointer hovers this element.</summary>
    public event Action<Key>? OnButtonUp;
    /// <summary>Raised when the pointer enters this element.</summary>
    public event Action? OnElementMouseHover;
    /// <summary>Raised when the pointer leaves this element.</summary>
    public event Action? OnElementMouseExit;

    public void AddChild(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        AddDescendant(child);
    }

    public bool RemoveChild(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return RemoveDescendant(child);
    }

    internal void RaiseButtonDown(Key key) => OnButtonDown?.Invoke(key);
    internal void RaiseButtonUp(Key key) => OnButtonUp?.Invoke(key);
    internal void RaiseMouseHover() => OnElementMouseHover?.Invoke();
    internal void RaiseMouseExit() => OnElementMouseExit?.Invoke();

    protected void CopyTo(UIElement element)
    {
        element.Scale = Scale;
        element.Position = Position;
        element.Opacity = Opacity;
        element.CornerRadius = CornerRadius;
        element.Color = Color;
        element.OutlineThickness = OutlineThickness;
        element.OutlineColor = OutlineColor;
        element.AnchorPoint = AnchorPoint;
        element.SSAAStrength = SSAAStrength;
        element.Rotation = Rotation;
        element.Rendered = Rendered;
        element.Layer = Layer;
        element.ClipDescendants = ClipDescendants;
    }

    private static int ValidateByte(int value, string parameterName) =>
        (uint)value <= byte.MaxValue ? value : throw new ArgumentOutOfRangeException(parameterName);

    private static float ValidateSsaStrength(float value, string parameterName) =>
        value is >= 0f and <= 1f
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, "Anti-aliasing strength must be between 0 and 1.");
}
