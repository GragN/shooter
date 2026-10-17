namespace Sniper3D;

/// <summary>
/// Описание оружия и его боеприпаса. Прицеливание для всех видов оружия
/// остаётся одинаковым; меняются физические параметры выстрела.
/// </summary>
internal sealed class WeaponProfile
{
    public required string DisplayName { get; init; }
    public required string HudName { get; init; }
    public required BallisticProfile Ballistics { get; init; }

    public bool IsAutomatic { get; init; }
    public float RateOfFireRpm { get; init; }
    public float RecoilPitchDegrees { get; init; } = 0.55f;

    // Высота оптической оси над осью ствола. Нужна для физически корректной
    // пристрелки: пуля стартует ниже линии визирования и пересекает её на ZERO.
    public float SightHeightMeters { get; init; } = 0.05f;

    /// <summary>
    /// СВД / 7Н1. Для 7Н1 используется измеренный G7 BC около 0.216.
    /// G7 лучше соответствует boat-tail форме этой снайперской пули.
    /// </summary>
    public static WeaponProfile Svd() => new()
    {
        DisplayName = "SVD 7.62x54R",
        HudName = "SVD",
        IsAutomatic = false,
        RecoilPitchDegrees = 0.38f,
        Ballistics = new BallisticProfile
        {
            MassKg = 0.00956f,
            DiameterMeters = 0.00792f,
            MuzzleVelocityMps = 820f,
            Construction = ProjectileConstruction.MildSteelCoreFmj,
            MuzzleVelocityStdDevMps = 3.5f,
            RiflingTwistDirection = RiflingTwistDirection.Right,
            TwistRateMetersPerTurn = 0.240f,
            GyroscopicStabilityFactor = 1.60f,
            DragModel = DragModelKind.G7,
            BallisticCoefficient = 0.216f,
            AirDensityKgPerM3 = 1.225f,
            SpeedOfSoundMps = 340.294f,
            GravityMps2 = 9.80665f
        }
    };

    /// <summary>
    /// Пистолет Макарова, 9x18 мм, 95 gr / ~6.1 г FMJ.
    /// Для тупоносой пистолетной пули используем G1 BC 0.077.
    /// </summary>
    public static WeaponProfile MakarovPm() => new()
    {
        DisplayName = "Makarov PM",
        HudName = "MAKAROV PM",
        IsAutomatic = false,
        RecoilPitchDegrees = 0.40f,
        Ballistics = new BallisticProfile
        {
            MassKg = 0.0061f,
            DiameterMeters = 0.00927f,
            MuzzleVelocityMps = 315f,
            MuzzleVelocityStdDevMps = 4.5f,
            RiflingTwistDirection = RiflingTwistDirection.Right,
            TwistRateMetersPerTurn = 0.260f,
            GyroscopicStabilityFactor = 1.75f,
            DragModel = DragModelKind.G1,
            BallisticCoefficient = 0.077f,
            AirDensityKgPerM3 = 1.225f,
            SpeedOfSoundMps = 340.294f,
            GravityMps2 = 9.80665f
        }
    };

    /// <summary>
    /// Классический РПК 7.62x39. Для типичной 123 gr FMJ используем
    /// репрезентативный G1 BC 0.274; скорость оставлена для длинного ствола РПК.
    /// </summary>
    public static WeaponProfile Rpk() => new()
    {
        DisplayName = "RPK 7.62x39",
        HudName = "RPK 7.62",
        IsAutomatic = true,
        RateOfFireRpm = 600f,
        RecoilPitchDegrees = 0.22f,
        Ballistics = new BallisticProfile
        {
            MassKg = 0.0080f,
            DiameterMeters = 0.00792f,
            MuzzleVelocityMps = 745f,
            Construction = ProjectileConstruction.MildSteelCoreFmj,
            MuzzleVelocityStdDevMps = 5.0f,
            RiflingTwistDirection = RiflingTwistDirection.Right,
            TwistRateMetersPerTurn = 0.240f,
            GyroscopicStabilityFactor = 1.65f,
            DragModel = DragModelKind.G1,
            BallisticCoefficient = 0.274f,
            AirDensityKgPerM3 = 1.225f,
            SpeedOfSoundMps = 340.294f,
            GravityMps2 = 9.80665f
        }
    };

    /// <summary>
    /// Barrett M82A1 .50 BMG с M33 Ball. Barrett публикует G1 BC 0.62;
    /// поэтому здесь используется именно G1, а не искусственная конвертация G7.
    /// </summary>
    public static WeaponProfile BarrettM82A1() => new()
    {
        DisplayName = "Barrett M82A1 .50 BMG",
        HudName = "BARRETT .50",
        IsAutomatic = false,
        RecoilPitchDegrees = 0.72f,
        Ballistics = new BallisticProfile
        {
            MassKg = 0.0428f,
            DiameterMeters = 0.01295f,
            MuzzleVelocityMps = 838f,
            Construction = ProjectileConstruction.MildSteelCoreFmj,
            MuzzleVelocityStdDevMps = 3.0f,
            RiflingTwistDirection = RiflingTwistDirection.Right,
            TwistRateMetersPerTurn = 0.381f,
            GyroscopicStabilityFactor = 1.70f,
            DragModel = DragModelKind.G1,
            BallisticCoefficient = 0.620f,
            AirDensityKgPerM3 = 1.225f,
            SpeedOfSoundMps = 340.294f,
            GravityMps2 = 9.80665f
        }
    };
}
