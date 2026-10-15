using OpenTK.Graphics.OpenGL4;

namespace Sniper3D;

internal static class GlLineWidth
{
    private static float _maximum = 1f;

    public static void Initialize()
    {
        GL.GetInteger(GetPName.ContextFlags, out int flags);
        // Wide lines are removed from forward-compatible contexts, even if
        // ALIASED_LINE_WIDTH_RANGE still reports the hardware's broader range.
        if ((flags & 1) != 0)
            _maximum = 1f;
        else
        {
            float[] range = new float[2];
            GL.GetFloat(GetPName.AliasedLineWidthRange, range);
            _maximum = MathF.Max(1f, range[1]);
        }
    }

    public static void Set(float width) => GL.LineWidth(Math.Clamp(width, 1f, _maximum));
}
