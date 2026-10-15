using OpenTK.Mathematics;

namespace Sniper3D;

/// <summary>
/// Физически мотивированная терминальная модель для обычного бетона.
/// Она связывает геометрию повреждения и остаточную энергию с калибром,
/// площадью пули, массой, скоростью и свойствами материала.
/// </summary>
internal static class ConcreteImpactModel
{
    public static ConcreteImpactSolution Solve(
        MaterialProfile material,
        ProjectileImpactData projectile,
        float contiguousConcreteThicknessMeters,
        float cellSizeMeters)
    {
        float diameter = MathF.Max(projectile.DiameterMeters, 0.001f);
        float area = MathF.Max(projectile.CrossSectionAreaM2, 0.000000001f);
        float energy = MathF.Max(projectile.KineticEnergyJoules, 0f);

        // Эффективная динамическая прочность учитывает confinement вокруг
        // узкого высокоскоростного канала. Работа проникновения на метр:
        // W/L = sigma_dynamic * A / efficiency.
        float dynamicStrength = material.CompressiveStrengthPa
            * material.DynamicConfinementFactor;
        float resistanceForce = dynamicStrength * area;
        float workPerMeter = resistanceForce
            / MathF.Max(material.PenetrationEfficiency, 0.01f);

        float ballisticPenetrationDepth = workPerMeter > 0.001f
            ? energy / workPerMeter
            : 0f;

        // Небольшая добавка от высокой sectional mass density. Она не заменяет
        // энергетический расчёт, а отражает то, что длинная/тяжёлая пуля того же
        // калибра лучше сохраняет направление в канале.
        float referenceSectionalMass = 190f; // kg/m^2, порядка винтовочной пули
        float sectionalFactor = MathF.Pow(
            MathHelper.Clamp(projectile.SectionalMassKgPerM2 / referenceSectionalMass, 0.45f, 2.2f),
            0.16f);
        ballisticPenetrationDepth *= sectionalFactor;

        ballisticPenetrationDepth = MathHelper.Clamp(ballisticPenetrationDepth, 0f, 2.5f);

        float solidThickness = MathF.Max(0f, contiguousConcreteThicknessMeters);
        bool perforates = solidThickness > 0.0001f
            && ballisticPenetrationDepth >= solidThickness * 1.015f;

        float materialTravel = perforates
            ? solidThickness
            : MathF.Min(ballisticPenetrationDepth, solidThickness > 0f ? solidThickness : ballisticPenetrationDepth);

        float usedFraction = ballisticPenetrationDepth > 0.000001f
            ? MathHelper.Clamp(materialTravel / ballisticPenetrationDepth, 0f, 1f)
            : 1f;

        // При полном пробитии часть энергии остаётся у пули. Если расчётная
        // глубина меньше толщины массива, считаем, что пуля останавливается.
        float residualEnergy = perforates
            ? energy * MathF.Max(0f, 1f - usedFraction)
            : 0f;
        float depositedEnergy = MathF.Max(0f, energy - residualEnergy);

        // Размер входного разрушения зависит как от калибра, так и от энергии.
        // Крупный .50 BMG при той же глубине создаёт заметно более широкий
        // кратер, чем 7.62 мм, а пистолетная пуля даёт небольшой поверхностный скол.
        float energyKJ = energy * 0.001f;
        float caliberScale = MathF.Pow(diameter / 0.00762f, 0.55f);
        float craterRadius = 0.018f
            + 0.026f * MathF.Sqrt(MathF.Max(0f, energyKJ)) * caliberScale
            + diameter * 1.15f;
        craterRadius = MathHelper.Clamp(craterRadius, diameter * 1.4f, 0.28f);

        // Хрупкий бетон образует зону трещин шире собственно канала.
        // Более высокая tensile strength и fracture energy уменьшают радиус
        // раскалывания, более хрупкий материал — увеличивает.
        float tensileFactor = MathF.Sqrt(
            3_600_000f / MathF.Max(100_000f, material.TensileStrengthPa));
        float fractureFactor = MathF.Sqrt(
            120f / MathF.Max(10f, material.FractureEnergyJPerM2));
        float brittlenessFactor = MathHelper.Clamp(
            0.5f * (tensileFactor + fractureFactor),
            0.70f,
            1.45f);
        float damageRadius = craterRadius * (1.48f * brittlenessFactor);

        // Выходной скол шире входного только если пуля действительно прошла
        // через непрерывную толщу бетона.
        float exitSpallRadius = perforates
            ? damageRadius * MathHelper.Clamp(1.55f + energyKJ * 0.018f, 1.55f, 2.25f)
            : damageRadius;

        // Базовая величина повреждения одной воксельной ячейки. Она нормирована
        // на отношение физической глубины проникновения к размеру ячейки —
        // поэтому крупные 0.5-м блоки дома не исчезают от одного попадания ПМ,
        // а мелкие клетки куба могут реально выкрашиваться одиночным винтовочным.
        float cellDamageScale = cellSizeMeters > 0.0001f
            ? ballisticPenetrationDepth / cellSizeMeters
            : 0f;

        float residualSpeed = residualEnergy > 0f && projectile.MassKg > 0.000001f
            ? MathF.Sqrt(2f * residualEnergy / projectile.MassKg)
            : 0f;

        return new ConcreteImpactSolution(
            ballisticPenetrationDepth,
            materialTravel,
            craterRadius,
            damageRadius,
            exitSpallRadius,
            depositedEnergy,
            residualEnergy,
            residualSpeed,
            cellDamageScale,
            perforates);
    }
}

internal readonly record struct ConcreteImpactSolution(
    float BallisticPenetrationDepthMeters,
    float MaterialTravelMeters,
    float CraterRadiusMeters,
    float DamageRadiusMeters,
    float ExitSpallRadiusMeters,
    float DepositedEnergyJoules,
    float ResidualEnergyJoules,
    float ResidualSpeedMps,
    float CellDamageScale,
    bool Perforated);
