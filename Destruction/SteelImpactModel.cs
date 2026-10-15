using OpenTK.Mathematics;

namespace Sniper3D;

internal enum SteelImpactOutcome { Stopped, Perforated, Ricochet }

internal readonly record struct SteelImpactResult(
    SteelImpactOutcome Outcome,
    float IncidenceDegrees,
    float BallisticLimitMps,
    float DepositedEnergyJoules,
    float ResidualEnergyJoules,
    float ResidualMassKg,
    Vector3 ExitVelocity,
    float HoleRadiusMeters,
    float DentDepthMeters);

/// <summary>
/// Reduced-order ductile plate model: shear plugging + local plastic work,
/// projectile deformation, obliquity, and a Recht-Ipson-style residual velocity.
/// Coefficients are engineering estimates; see docs/steel-model.md.
/// </summary>
internal static class SteelImpactModel
{
    public static SteelImpactResult Solve(SteelMaterialProfile steel,
        ProjectileImpactData projectile, Vector3 surfaceNormal, float thicknessMeters)
    {
        if (projectile.MassKg <= 0f || projectile.DiameterMeters <= 0f ||
            !float.IsFinite(projectile.SpeedMps) || projectile.SpeedMps < 0f ||
            !float.IsFinite(thicknessMeters) || thicknessMeters <= 0f || surfaceNormal.LengthSquared < 1e-10f)
            throw new ArgumentOutOfRangeException(nameof(projectile));

        Vector3 direction = Vector3.Normalize(projectile.Direction);
        Vector3 normal = Vector3.Normalize(surfaceNormal);
        if (Vector3.Dot(direction, normal) > 0f)
            normal = -normal;
        float cosine = Math.Clamp(-Vector3.Dot(direction, normal), 0.02f, 1f);
        float angle = MathHelper.RadiansToDegrees(MathF.Acos(cosine));
        bool steelCore = projectile.Construction == ProjectileConstruction.MildSteelCoreFmj;
        float diameter = projectile.DiameterMeters * (steelCore ? 1.05f : 1.35f);
        float pathThickness = thicknessMeters / cosine;
        float area = MathF.PI * diameter * diameter * 0.25f;

        // Work to shear a plug over one plate thickness, plus local dishing/stretching.
        float shearWork = MathF.PI * diameter * pathThickness * pathThickness * steel.DynamicShearStrengthPa;
        float plasticWork = 0.65f * area * pathThickness * steel.DynamicFlowStressPa;
        float deformationLoss = steelCore ? 0.18f : 0.48f;
        float requiredEnergy = (shearWork + plasticWork) / (1f - deformationLoss);
        float limit = MathF.Sqrt(2f * requiredEnergy / projectile.MassKg);
        float energy = projectile.KineticEnergyJoules;
        float elasticEnergy = 0.5f * steel.YieldStrengthPa * steel.YieldStrengthPa
            / steel.YoungModulusPa * area * thicknessMeters * 12f;
        float dent = MathF.Min(thicknessMeters * 0.75f,
            MathF.Max(0f, energy * cosine * cosine - elasticEnergy)
            / MathF.Max(steel.DynamicFlowStressPa * area * 6f, 1f));

        // A grazing impact failing to perforate sheds mass and loses its normal
        // momentum. It is not an elastic mirror reflection at the incoming speed.
        float ricochetAngle = steelCore ? 65f : 60f;
        if (angle >= ricochetAngle && energy < requiredEnergy)
        {
            Vector3 incoming = direction * projectile.SpeedMps;
            float vn = Vector3.Dot(incoming, normal);
            Vector3 tangent = incoming - normal * vn;
            Vector3 outgoing = tangent * (steelCore ? 0.72f : 0.48f) - normal * vn * 0.10f;
            float mass = projectile.MassKg * (steelCore ? 0.65f : 0.40f);
            float remaining = 0.5f * mass * outgoing.LengthSquared;
            return new(SteelImpactOutcome.Ricochet, angle, limit, energy - remaining,
                remaining, mass, outgoing, 0f, dent);
        }

        if (energy <= requiredEnergy)
            return new(SteelImpactOutcome.Stopped, angle, limit, energy, 0f, 0f, Vector3.Zero, 0f, dent);

        float plugMass = area * pathThickness * steel.DensityKgPerM3;
        float momentumFactor = projectile.MassKg / (projectile.MassKg + plugMass);
        float residualEnergy = (energy - requiredEnergy) * momentumFactor * momentumFactor;
        float residualMass = projectile.MassKg * (steelCore ? 0.85f : 0.60f);
        float residualSpeed = MathF.Sqrt(2f * residualEnergy / residualMass);
        return new(SteelImpactOutcome.Perforated, angle, limit, energy - residualEnergy,
            residualEnergy, residualMass, direction * residualSpeed, diameter * 0.5f, dent);
    }
}
