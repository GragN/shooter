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
    Vector3 Direction)
{
    public static ProjectileImpactData From(BallisticProfile profile, Vector3 velocity)
    {
        float speed = velocity.Length;
        Vector3 direction = speed > 0.000001f
            ? velocity / speed
            : -Vector3.UnitZ;

        float area = MathF.Max(profile.CrossSectionAreaM2, 0.000000001f);
        float energy = profile.KineticEnergyJoules(speed);
        float momentum = profile.MassKg * speed;

        return new ProjectileImpactData(
            profile.MassKg,
            profile.DiameterMeters,
            area,
            speed,
            energy,
            momentum,
            energy / area,
            profile.MassKg / area,
            direction);
    }
}
