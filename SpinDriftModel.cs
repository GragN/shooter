namespace Sniper3D;

internal enum RiflingTwistDirection
{
    None = 0,
    Right = 1,
    Left = -1
}

/// <summary>
/// Эмпирическая модель гироскопического spin drift по Bryan Litz.
/// Это не полноценная 6-DOF аэродинамика, но она воспроизводит накопленный
/// боковой увод пули из-за yaw of repose существенно реалистичнее, чем
/// постоянная искусственная боковая сила.
///
/// SD(inches) = 1.25 * (Sg + 1.2) * TOF^1.83
///
/// Знак определяется направлением нарезов: правые нарезы уводят вправо,
/// левые — влево относительно начального направления выстрела.
/// </summary>
internal static class SpinDriftModel
{
    private const float InchesToMeters = 0.0254f;

    public static float CumulativeDriftMeters(
        float gyroscopicStabilityFactor,
        float timeOfFlightSeconds,
        RiflingTwistDirection twistDirection)
    {
        if (twistDirection == RiflingTwistDirection.None ||
            gyroscopicStabilityFactor <= 0f ||
            timeOfFlightSeconds <= 0f)
            return 0f;

        float sg = MathF.Max(0.5f, gyroscopicStabilityFactor);
        float t = MathF.Max(0f, timeOfFlightSeconds);
        float driftInches = 1.25f * (sg + 1.2f) * MathF.Pow(t, 1.83f);
        float sign = (float)twistDirection;
        return sign * driftInches * InchesToMeters;
    }
}
