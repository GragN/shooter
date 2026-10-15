using OpenTK.Mathematics;

namespace Sniper3D;

internal readonly record struct SteelDent(Vector2 Center, float Radius, float Depth, float FrontSign);

/// <summary>An infinite bore, clipped by the finite plate. The axis supports oblique holes.</summary>
internal readonly record struct SteelHole(Vector3 Entry, Vector3 Axis, float Radius)
{
    public bool TryGetInterval(Vector3 from, Vector3 delta, float clearance, out float enter, out float exit)
    {
        Vector3 offset = from - Entry;
        Vector3 q = offset - Axis * Vector3.Dot(offset, Axis);
        Vector3 d = delta - Axis * Vector3.Dot(delta, Axis);
        return SteelPlateTarget.QuadraticInterval(d.LengthSquared, 2f * Vector3.Dot(q, d),
            q.LengthSquared - MathF.Pow(MathF.Max(0f, Radius - clearance), 2f), out enter, out exit);
    }
}

internal sealed class MetalFragment
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Age;
    public float Size;
    public bool Hot;
}

/// <summary>
/// One clamped 1 m x 10 mm S355 plate. Collision is an analytic finite cylinder;
/// permanent holes are subtracted as a union of bores, never a coarse voxel slab.
/// </summary>
internal sealed class SteelPlateTarget
{
    public const float DiameterMeters = 1f;
    public const float RadiusMeters = DiameterMeters * 0.5f;
    public const float ThicknessMeters = 0.01f;
    private readonly List<SteelHole> _holes = new();
    private readonly List<SteelDent> _dents = new();
    private readonly List<MetalFragment> _fragments = new();
    private readonly Random _random = new(31019);

    public SteelMaterialProfile Material { get; } = new();
    public Vector3 Position { get; private set; } = new(0f, 1.7f, -995.005f);
    public float DistanceMeters { get; private set; } = 1000f;
    public float YawDegrees { get; private set; }
    public int Revision { get; private set; }
    public int HitCount { get; private set; }
    public IReadOnlyList<SteelHole> Holes => _holes;
    public IReadOnlyList<SteelDent> Dents => _dents;
    public IReadOnlyList<MetalFragment> Fragments => _fragments;
    public float MassKg => MathF.PI * RadiusMeters * RadiusMeters * ThicknessMeters * Material.DensityKgPerM3;
    public Vector3 Normal => ToWorldDirection(Vector3.UnitZ);
    public Matrix4 Transform => Matrix4.CreateRotationY(MathHelper.DegreesToRadians(YawDegrees)) * Matrix4.CreateTranslation(Position);

    public void SetDistance(float meters, float shooterZ)
    {
        float distance = Math.Clamp(meters, 100f, 2000f);
        if (MathF.Abs(DistanceMeters - distance) > 0.001f)
            ResetDamage();
        DistanceMeters = distance;
        Position = new(0f, 1.7f, shooterZ - distance - ThicknessMeters * 0.5f);
    }

    public void SetYaw(float degrees) => YawDegrees = Math.Clamp(degrees, 0f, 75f);

    public void ResetDamage()
    {
        _holes.Clear();
        _dents.Clear();
        _fragments.Clear();
        HitCount = 0;
        Revision++;
    }

    public Vector3 ToLocalPoint(Vector3 point) => RotateY(point - Position, -YawDegrees);
    public Vector3 ToWorldPoint(Vector3 point) => Position + ToWorldDirection(point);
    public Vector3 ToWorldDirection(Vector3 direction) => RotateY(direction, YawDegrees);
    private static Vector3 RotateY(Vector3 v, float degrees)
    {
        float angle = MathHelper.DegreesToRadians(degrees);
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        return new(c * v.X + s * v.Z, v.Y, -s * v.X + c * v.Z);
    }

    public bool TryHitSegment(Vector3 from, Vector3 to, out float hitT, out Vector3 hitPoint, float bulletRadius = 0f)
    {
        hitT = 0f;
        hitPoint = default;
        Vector3 localFrom = ToLocalPoint(from);
        Vector3 delta = RotateY(to - from, -YawDegrees);
        if (!CylinderInterval(localFrom, delta, out float enter, out float exit))
            return false;
        float current = MathF.Max(0f, enter);
        exit = MathF.Min(1f, exit);
        if (current > exit)
            return false;

        // Remove the union of all hole intervals. An oblique ray entering a hole
        // but hitting its wall still produces the earliest remaining solid contact.
        for (int pass = 0; pass <= _holes.Count; pass++)
        {
            float openUntil = current;
            foreach (SteelHole hole in _holes)
            {
                if (hole.Radius <= bulletRadius)
                    continue;
                if (hole.TryGetInterval(localFrom, delta, bulletRadius, out float a, out float b)
                    && a <= current + 1e-7f && b >= current)
                    openUntil = MathF.Max(openUntil, b);
            }
            if (openUntil >= exit)
                return false;
            if (openUntil <= current)
            {
                hitT = current;
                hitPoint = from + (to - from) * hitT;
                return true;
            }
            current = openUntil + 1e-7f;
        }
        return false;
    }

    public SteelImpactResult ApplyImpact(Vector3 worldPoint, ProjectileImpactData projectile, out Vector3 exitPoint)
    {
        Vector3 local = ToLocalPoint(worldPoint);
        Vector3 direction = RotateY(projectile.Direction, -YawDegrees);
        float sign = direction.Z <= 0f ? 1f : -1f;
        Vector3 localNormal = Vector3.UnitZ * sign;
        float halfThickness = ThicknessMeters * 0.5f;
        if (MathF.Abs(local.Z) < halfThickness - 0.0002f)
        {
            // Rim contact or the wall of an existing bore: use the real local
            // surface normal rather than treating every contact as a face hit.
            if (local.Xy.LengthSquared >= (RadiusMeters - 0.0002f) * (RadiusMeters - 0.0002f))
                localNormal = Vector3.Normalize(new Vector3(local.X, local.Y, 0f));
            else
            {
                foreach (SteelHole hole in _holes)
                {
                    Vector3 offset = local - hole.Entry;
                    offset -= hole.Axis * Vector3.Dot(offset, hole.Axis);
                    if (offset.LengthSquared > 1e-10f && offset.Length <= hole.Radius + projectile.DiameterMeters)
                    {
                        localNormal = -Vector3.Normalize(offset);
                        break;
                    }
                }
            }
        }
        float thickness = ThicknessMeters;
        // Dishing bends the sheet; only a small fraction is local thinning.
        foreach (SteelDent dent in _dents)
        {
            float radiusSquared = (local.Xy - dent.Center).LengthSquared / (dent.Radius * dent.Radius);
            if (radiusSquared < 1f)
                thickness -= 0.10f * dent.Depth * MathF.Pow(1f - radiusSquared, 2f);
        }
        thickness = MathF.Max(ThicknessMeters * 0.25f, thickness);
        float exitDistance = 0f;
        if (CylinderInterval(local, direction, out _, out float end))
            exitDistance = MathF.Max(0f, end);
        thickness = MathF.Min(thickness, MathF.Max(0.0001f,
            exitDistance * MathF.Abs(Vector3.Dot(direction, localNormal))));
        SteelImpactResult result = SteelImpactModel.Solve(Material, projectile, ToWorldDirection(localNormal), thickness);
        HitCount++;
        Vector3 localExit = local;
        if (result.Outcome == SteelImpactOutcome.Perforated)
        {
            localExit = local + direction * exitDistance;
            _holes.Add(new(local, Vector3.Normalize(direction), result.HoleRadiusMeters));
        }
        else if (result.DentDepthMeters > 0.00001f && MathF.Abs(localNormal.Z) > 0.9f)
        {
            _dents.Add(new(local.Xy, projectile.DiameterMeters * 2.5f, result.DentDepthMeters, sign));
        }
        Revision++;
        exitPoint = ToWorldPoint(localExit) + Vector3.Normalize(
            result.Outcome == SteelImpactOutcome.Ricochet ? result.ExitVelocity : projectile.Direction) * 0.0005f;
        SpawnFragments(worldPoint, projectile.Direction, result);
        return result;
    }

    public bool IsHole(Vector3 localPoint)
    {
        foreach (SteelHole hole in _holes)
        {
            Vector3 d = localPoint - hole.Entry;
            d -= hole.Axis * Vector3.Dot(d, hole.Axis);
            if (d.LengthSquared < hole.Radius * hole.Radius)
                return true;
        }
        return false;
    }

    public float SurfaceZ(Vector2 xy, float faceSign)
    {
        float z = faceSign * ThicknessMeters * 0.5f;
        foreach (SteelDent dent in _dents)
        {
            float r2 = (xy - dent.Center).LengthSquared / (dent.Radius * dent.Radius);
            if (r2 < 1f)
                z -= dent.FrontSign * dent.Depth * MathF.Pow(1f - r2, 2f);
        }
        return z;
    }

    public void UpdatePhysics(float dt)
    {
        foreach (MetalFragment f in _fragments)
        {
            f.Age += dt;
            f.Velocity.Y -= 9.80665f * dt;
            f.Position += f.Velocity * dt;
            if (f.Position.Y < f.Size)
            {
                f.Position.Y = f.Size;
                f.Velocity *= 0.35f;
                f.Velocity.Y = MathF.Abs(f.Velocity.Y) * 0.15f;
            }
        }
        _fragments.RemoveAll(f => f.Age > 4f);
    }

    private void SpawnFragments(Vector3 point, Vector3 direction, SteelImpactResult result)
    {
        // Decorative target/projectile fragments have a capped 2% energy budget.
        float fragmentEnergy = result.DepositedEnergyJoules * 0.02f;
        int count = Math.Clamp((int)(fragmentEnergy / 2f), 1, 14);
        for (int i = 0; i < count; i++)
        {
            if (_fragments.Count >= 240)
                _fragments.RemoveAt(0);
            Vector3 random = new((float)_random.NextDouble() - 0.5f,
                (float)_random.NextDouble() - 0.3f, (float)_random.NextDouble() - 0.5f);
            Vector3 eject = result.Outcome == SteelImpactOutcome.Perforated ? direction : -direction;
            eject = Vector3.Normalize(eject + random);
            float size = 0.0015f + (float)_random.NextDouble() * 0.002f;
            float mass = size * size * size * Material.DensityKgPerM3;
            float speed = MathF.Min(85f, MathF.Sqrt(2f * fragmentEnergy / (count * mass)));
            _fragments.Add(new() { Position = point + eject * 0.012f, Velocity = eject * speed,
                Size = size, Hot = i % 3 == 0 && result.DepositedEnergyJoules > 100f });
        }
    }

    private static bool CylinderInterval(Vector3 from, Vector3 delta, out float enter, out float exit)
    {
        if (!QuadraticInterval(delta.X * delta.X + delta.Y * delta.Y,
            2f * (from.X * delta.X + from.Y * delta.Y),
            from.X * from.X + from.Y * from.Y - RadiusMeters * RadiusMeters, out enter, out exit))
            return false;
        float half = ThicknessMeters * 0.5f;
        if (MathF.Abs(delta.Z) < 1e-10f)
            return MathF.Abs(from.Z) <= half;
        float a = (-half - from.Z) / delta.Z;
        float b = (half - from.Z) / delta.Z;
        enter = MathF.Max(enter, MathF.Min(a, b));
        exit = MathF.Min(exit, MathF.Max(a, b));
        return enter <= exit;
    }

    internal static bool QuadraticInterval(float a, float b, float c, out float enter, out float exit)
    {
        enter = float.NegativeInfinity;
        exit = float.PositiveInfinity;
        if (a < 1e-12f)
            return c <= 0f;
        // Double precision avoids cancellation at long range for small holes.
        double discriminant = (double)b * b - 4.0 * a * c;
        if (discriminant < 0)
            return false;
        double root = Math.Sqrt(discriminant);
        enter = (float)((-b - root) / (2.0 * a));
        exit = (float)((-b + root) / (2.0 * a));
        return true;
    }
}
