namespace Tiny3D.Graphics;

public readonly record struct Color32(byte R, byte G, byte B, byte A = 255)
{
    internal int ToPixel() => A << 24 | R << 16 | G << 8 | B;

    internal Color32 Shade(float amount) =>
        new(Scale(R, amount), Scale(G, amount), Scale(B, amount), A);

    private static byte Scale(byte value, float amount) =>
        (byte)System.Math.Clamp((int)(value * amount), 0, 255);
}
