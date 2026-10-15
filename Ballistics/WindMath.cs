using OpenTK.Mathematics;

namespace Sniper3D;

/// <summary>
/// Единственная точка, где ограничивается скорость ветра.
/// Генератор, HUD и баллистика используют одну и ту же реализацию.
/// </summary>
internal static class WindMath
{
    public const float AbsoluteMaxWindSpeedMps = 50f;

    public static float SanitizeLimit(float limitMps)
    {
        if (!float.IsFinite(limitMps))
            return 0f;

        return MathHelper.Clamp(limitMps, 0f, AbsoluteMaxWindSpeedMps);
    }

    public static Vector3 ClampVelocity(Vector3 velocity, float limitMps)
    {
        float limit = SanitizeLimit(limitMps);
        if (limit <= 0f)
            return Vector3.Zero;

        float lengthSquared = velocity.LengthSquared;
        if (!float.IsFinite(lengthSquared) || lengthSquared <= 0.0000000001f)
            return Vector3.Zero;

        float limitSquared = limit * limit;
        if (lengthSquared <= limitSquared)
            return velocity;

        float length = MathF.Sqrt(lengthSquared);
        return velocity * (limit / length);
    }
}
