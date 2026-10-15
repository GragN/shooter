namespace Sniper3D;

/// <summary>
/// Физические параметры патрона и атмосферы.
/// Все величины в SI: метры, секунды, килограммы.
/// </summary>
internal sealed class BallisticProfile
{
    public float MassKg { get; init; } = 0.00956f;
    public float DiameterMeters { get; init; } = 0.00792f;
    public float MuzzleVelocityMps { get; init; } = 820f;

    // Реальный боеприпас не выдаёт абсолютно одинаковую скорость выстрел за выстрелом.
    // Это стандартное отклонение начальной скорости для гауссового распределения.
    public float MuzzleVelocityStdDevMps { get; init; } = 0f;

    // Spin drift. TwistRateMetersPerTurn хранится для физического профиля оружия
    // и будущей более полной 6-DOF модели. Текущая модель Литца использует
    // номинальный muzzle Sg и время полёта.
    public RiflingTwistDirection RiflingTwistDirection { get; init; } = RiflingTwistDirection.Right;
    public float TwistRateMetersPerTurn { get; init; } = 0.24f;
    public float GyroscopicStabilityFactor { get; init; } = 1.6f;

    // BC должен соответствовать выбранной reference-модели.
    // G1 BC нельзя напрямую использовать с G7 и наоборот.
    public DragModelKind DragModel { get; init; } = DragModelKind.G7;
    public float BallisticCoefficient { get; init; } = 0.216f;

    // Пока атмосфера фиксированная; следующим этапом её можно сделать
    // зависящей от температуры, давления и высоты.
    public float AirDensityKgPerM3 { get; init; } = 1.225f;
    public float SpeedOfSoundMps { get; init; } = 340.294f;
    public float GravityMps2 { get; init; } = 9.80665f;

    public float CrossSectionAreaM2
    {
        get
        {
            float radius = DiameterMeters * 0.5f;
            return MathF.PI * radius * radius;
        }
    }

    /// <summary>
    /// Sectional density в традиционных единицах BC: lb / in^2.
    /// </summary>
    public float SectionalDensity
    {
        get
        {
            const float kilogramsToPounds = 2.20462262f;
            const float metersPerInch = 0.0254f;
            float massLb = MassKg * kilogramsToPounds;
            float diameterInches = DiameterMeters / metersPerInch;
            return massLb / (diameterInches * diameterInches);
        }
    }

    /// <summary>
    /// Form factor i = SD / BC. Он масштабирует reference Cd G1/G7 к
    /// реальному снаряду. Для reference projectile при BC=1 получаем i=1.
    /// </summary>
    public float FormFactor
        => BallisticCoefficient > 0.000001f
            ? SectionalDensity / BallisticCoefficient
            : 1f;

    public float MachNumber(float speedMps)
        => SpeedOfSoundMps > 0.001f ? MathF.Max(0f, speedMps) / SpeedOfSoundMps : 0f;

    public float EffectiveDragCoefficient(float speedMps)
    {
        float referenceCd = StandardDragModel.ReferenceCd(DragModel, MachNumber(speedMps));
        return referenceCd * FormFactor;
    }

    public float KineticEnergyJoules(float speedMps)
        => 0.5f * MassKg * speedMps * speedMps;

    /// <summary>
    /// Семплирует фактическую начальную скорость конкретного патрона.
    /// Распределение нормальное и ограничено ±3σ, чтобы исключить нереалистичные выбросы.
    /// ZERO, T-прогноз и сетка прицела используют номинальную среднюю MuzzleVelocityMps.
    /// </summary>
    public float SampleMuzzleVelocity(Random random)
    {
        if (MuzzleVelocityStdDevMps <= 0.0001f)
            return MuzzleVelocityMps;

        double u1 = Math.Max(1e-12, 1.0 - random.NextDouble());
        double u2 = 1.0 - random.NextDouble();
        double normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        normal = Math.Clamp(normal, -3.0, 3.0);

        return MathF.Max(1f, MuzzleVelocityMps + MuzzleVelocityStdDevMps * (float)normal);
    }
}
