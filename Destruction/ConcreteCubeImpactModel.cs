using OpenTK.Mathematics;

namespace Sniper3D;

internal readonly record struct ConcreteCubeImpactSolution(
    float PenetrationDepth, float MaterialTravel, float EntryRadius, float EntryDepth,
    float BoreRadius, float ExitRadius, bool Perforated, bool Ricochet,
    float DepositedEnergy, float ResidualEnergy, float ResidualMass, Vector3 ExitVelocity);

/// <summary>
/// Strength + inertial cavity resistance, integrated analytically. This is a
/// reduced-order unreinforced-concrete model, not calibrated FEM or test data.
/// </summary>
internal static class ConcreteCubeImpactModel
{
    public static ConcreteCubeImpactSolution Solve(MaterialProfile material, ProjectileImpactData p,
        float solidThickness, Vector3 surfaceNormal, float localDamage = 0f)
    {
        if (p.MassKg <= 0f || p.DiameterMeters <= 0f || solidThickness <= 0f)
            throw new ArgumentOutOfRangeException(nameof(p));
        Vector3 direction = Vector3.Normalize(p.Direction);
        Vector3 normal = Vector3.Normalize(surfaceNormal);
        if (Vector3.Dot(direction, normal) > 0f) normal = -normal;
        float cosine = Math.Clamp(-Vector3.Dot(direction, normal), 0f, 1f);
        bool steelCore = p.Construction == ProjectileConstruction.MildSteelCoreFmj;
        float diameter = p.DiameterMeters * (steelCore ? 1.05f : 1.4f);
        float area = MathF.PI * diameter * diameter * 0.25f;
        float resistance = material.CompressiveStrengthPa * 10f * (1f - 0.35f * Math.Clamp(localDamage, 0f, 1f));
        float inertia = material.DensityKgPerM3 * (steelCore ? 0.22f : 0.35f);
        float coefficient = 2f * area * inertia / p.MassKg;
        float initialSquared = p.SpeedMps * p.SpeedMps;
        float penetration = MathF.Log(1f + inertia * initialSquared / resistance) / coefficient;
        float travel = MathF.Min(penetration, solidThickness);
        float remainingSquared = MathF.Max(0f,
            ((resistance + inertia * initialSquared) * MathF.Exp(-coefficient * travel) - resistance) / inertia);
        bool perforates = solidThickness > 0f && penetration > solidThickness && remainingSquared > 25f;
        float residualMass = p.MassKg * (steelCore ? 0.90f : 0.65f);
        float residualEnergy = perforates ? 0.5f * residualMass * remainingSquared : 0f;
        Vector3 velocity = perforates ? direction * MathF.Sqrt(remainingSquared) : Vector3.Zero;
        bool ricochet = !perforates && cosine < MathF.Cos(MathHelper.DegreesToRadians(70f))
            && p.SpeedMps * cosine < 260f && p.KineticEnergyJoules > 10f;
        if (ricochet)
        {
            Vector3 incoming = direction * p.SpeedMps;
            float vn = Vector3.Dot(incoming, normal);
            velocity = (incoming - normal * vn) * (steelCore ? 0.45f : 0.30f) - normal * vn * 0.05f;
            residualMass = p.MassKg * (steelCore ? 0.65f : 0.40f);
            residualEnergy = 0.5f * residualMass * velocity.LengthSquared;
            travel *= cosine * cosine;
        }
        float deposited = MathF.Max(0f, p.KineticEnergyJoules - residualEnergy);
        float entryDepth = MathF.Min(travel, diameter * 0.65f + penetration * 0.06f);
        float workDensity = CrushingWorkPerVolume(material);
        float radius = entryDepth > 1e-6f
            ? MathF.Sqrt(3f * deposited * 0.035f / (MathF.PI * entryDepth * workDensity)) : 0f;
        radius = Math.Clamp(radius, diameter * 0.5f, diameter * 6f);
        return new(penetration, travel, radius, entryDepth, diameter * 0.9f,
            radius * 1.7f, perforates, ricochet, deposited, residualEnergy,
            residualEnergy > 0f ? residualMass : 0f, velocity);
    }

    public static float CrushingWorkPerVolume(MaterialProfile material)
        => material.CompressiveStrengthPa * 0.08f;

    public static float FractureWorkPerCell(MaterialProfile material, float cell)
        => material.FractureEnergyJPerM2 * 6f * cell * cell
            + material.TensileStrengthPa * material.TensileStrengthPa / (2f * material.YoungModulusPa) * cell * cell * cell;
}
