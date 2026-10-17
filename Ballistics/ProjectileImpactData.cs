using OpenTK.Mathematics;

namespace Sniper3D;

/// <summary>
/// Состояние конкретной пули непосредственно в момент удара.
/// В отличие от старой модели, материал видит не только энергию, но и
/// калибр, площадь поперечного сечения, массу и импульс.
/// </summary>
internal readonly record struct ProjectileImpactData(
    float MassKg,
    float DiameterMeters,
    float CrossSectionAreaM2,
    float SpeedMps,
    float KineticEnergyJoules,
    float MomentumKgMps,
    float EnergyPerAreaJPerM2,
    float SectionalMassKgPerM2,
    Vector3 Direction,
    ProjectileConstruction Construction = ProjectileConstruction.LeadCoreFmj)
{
    // Steel-body training rounds use the existing steel response as a proxy,
    // not a new calibrated AP/HE terminal model.
    public bool UsesSteelResponse => Construction is ProjectileConstruction.MildSteelCoreFmj
        or ProjectileConstruction.InertSteelBody;

    public static ProjectileImpactData From(BallisticProfile profile, Vector3 velocity, float? remainingMassKg = null)
    {
        float speed = velocity.Length;
        Vector3 direction = speed > 0.000001f
            ? velocity / speed
            : -Vector3.UnitZ;

        float area = MathF.Max(profile.CrossSectionAreaM2, 0.000000001f);
        float mass = remainingMassKg ?? profile.MassKg;
        float energy = 0.5f * mass * speed * speed;
        float momentum = mass * speed;

        return new ProjectileImpactData(
            mass,
            profile.DiameterMeters,
            area,
            speed,
            energy,
            momentum,
            energy / area,
            mass / area,
            direction,
            profile.Construction);
    }
}
