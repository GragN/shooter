using OpenTK.Mathematics;
using Sniper3D;

int passed = 0, failed = 0;
Run("Plate dimensions and steel mass", () =>
{
    Near(SteelPlateTarget.DiameterMeters, 1f, 1e-6f);
    Near(SteelPlateTarget.ThicknessMeters, 0.01f, 1e-6f);
    Near(new SteelPlateTarget().MassKg, 61.65376f, 0.001f);
});
Run("PM bullet stops; high-energy .50 perforates", () =>
{
    Check(Solve(WeaponProfile.MakarovPm(), 315f).Outcome == SteelImpactOutcome.Stopped);
    Check(Solve(WeaponProfile.BarrettM82A1(), 838f).Outcome == SteelImpactOutcome.Perforated);
});
Run("Perforation threshold separates stop and passage", () =>
{
    WeaponProfile weapon = WeaponProfile.Svd();
    float limit = Solve(weapon, 800f).BallisticLimitMps;
    Check(Solve(weapon, limit * 0.99f).Outcome == SteelImpactOutcome.Stopped);
    Check(Solve(weapon, limit * 1.01f).Outcome == SteelImpactOutcome.Perforated);
});
Run("Thickness and obliquity increase required energy", () =>
{
    float normal = Solve(WeaponProfile.Svd(), 800f).BallisticLimitMps;
    Check(Solve(WeaponProfile.Svd(), 800f, 45f).BallisticLimitMps > normal);
    Check(Solve(WeaponProfile.Svd(), 800f, 0f, 0.02f).BallisticLimitMps > normal);
});
Run("Grazing .50 impact ricochets away with energy loss", () =>
{
    SteelImpactResult hit = Solve(WeaponProfile.BarrettM82A1(), 600f, 75f);
    Check(hit.Outcome == SteelImpactOutcome.Ricochet);
    Check(hit.ExitVelocity.Z > 0f);
    Check(hit.ExitVelocity.Length < 600f);
    Check(hit.DentDepthMeters < SteelPlateTarget.ThicknessMeters);
});
Run("Energy and mass budgets over 1900 impact scenarios", () =>
{
    foreach (WeaponProfile weapon in Weapons())
    for (int speed = 50; speed <= 1000; speed += 50)
    for (int angle = 0; angle <= 90; angle += 5)
    {
        SteelImpactResult hit = Solve(weapon, speed, angle);
        float energy = weapon.Ballistics.KineticEnergyJoules(speed);
        Check(float.IsFinite(hit.BallisticLimitMps) && float.IsFinite(hit.DepositedEnergyJoules));
        Check(hit.ResidualEnergyJoules >= 0f && hit.DepositedEnergyJoules >= 0f);
        Near(hit.ResidualEnergyJoules + hit.DepositedEnergyJoules, energy, 0.02f);
        Check(hit.ResidualMassKg <= weapon.Ballistics.MassKg && hit.ResidualMassKg >= 0f);
        Near(0.5f * hit.ResidualMassKg * hit.ExitVelocity.LengthSquared, hit.ResidualEnergyJoules, 0.02f);
    }
});
Run("Continuous collision catches 10 mm plate across 2 m segment", () =>
{
    var plate = new SteelPlateTarget();
    Check(plate.TryHitSegment(plate.Position + Vector3.UnitZ, plate.Position - Vector3.UnitZ, out float t, out _));
    Near(t, 0.4975f, 0.0001f);
});
Run("Disc is circular, including rim and back face", () =>
{
    var plate = new SteelPlateTarget();
    Vector3 p = plate.Position;
    Check(!plate.TryHitSegment(p + new Vector3(0.49f, 0.49f, 1f), p + new Vector3(0.49f, 0.49f, -1f), out _, out _));
    Check(plate.TryHitSegment(p + new Vector3(0.499f, 0f, 1f), p + new Vector3(0.499f, 0f, -1f), out _, out _));
    Check(plate.TryHitSegment(p - Vector3.UnitZ, p + Vector3.UnitZ, out float t, out _));
    Near(t, 0.4975f, 0.0001f);
});
Run("Yaw, world transform and collision agree", () =>
{
    var plate = new SteelPlateTarget();
    foreach (float yaw in new[] { 0f, 25f, 60f, 75f })
    {
        plate.SetYaw(yaw);
        Vector3 world = plate.ToWorldPoint(new(0.1f, 0.2f, 0.003f));
        Check((plate.ToLocalPoint(world) - new Vector3(0.1f, 0.2f, 0.003f)).Length < 0.0002f);
        Vector3 transformed = Vector3.TransformPosition(new(0.1f, 0.2f, 0.003f), plate.Transform);
        Check((world - transformed).Length < 0.0002f);
        Check(plate.TryHitSegment(plate.Position + plate.Normal, plate.Position - plate.Normal, out _, out _));
    }
});
Run("Permanent hole passes a repeated shot, surrounding steel still collides", () =>
{
    var plate = new SteelPlateTarget();
    Perforate(plate);
    Check(plate.Holes.Count == 1);
    Check(!plate.TryHitSegment(plate.Position + Vector3.UnitZ, plate.Position - Vector3.UnitZ,
        out _, out _, WeaponProfile.BarrettM82A1().Ballistics.DiameterMeters * 0.5f));
    Vector3 offset = new(0.04f, 0f, 0f);
    Check(plate.TryHitSegment(plate.Position + offset + Vector3.UnitZ,
        plate.Position + offset - Vector3.UnitZ, out _, out _));
});
Run("Oblique ray cannot pass through a hole wall", () =>
{
    var plate = new SteelPlateTarget();
    Perforate(plate);
    Vector3 entry = plate.Position + new Vector3(0f, 0f, SteelPlateTarget.ThicknessMeters * 0.5f);
    Check(plate.TryHitSegment(entry, entry + new Vector3(0.1f, 0f, -0.02f), out float t, out _));
    Check(t > 0f && t < 1f);
});
Run("Oblique perforation passes along its bore", () =>
{
    var plate = new SteelPlateTarget();
    plate.SetYaw(35f);
    Check(plate.TryHitSegment(plate.Position + Vector3.UnitZ, plate.Position - Vector3.UnitZ, out _, out Vector3 contact));
    SteelImpactResult result = plate.ApplyImpact(contact,
        ProjectileImpactData.From(WeaponProfile.BarrettM82A1().Ballistics, -Vector3.UnitZ * 838f), out _);
    Check(result.Outcome == SteelImpactOutcome.Perforated);
    Check(!plate.TryHitSegment(plate.Position + Vector3.UnitZ, plate.Position - Vector3.UnitZ, out _, out _));
});
Run("A larger projectile cannot fit through a smaller bore", () =>
{
    var plate = new SteelPlateTarget();
    Perforate(plate);
    Check(plate.TryHitSegment(plate.Position + Vector3.UnitZ, plate.Position - Vector3.UnitZ,
        out _, out _, plate.Holes[0].Radius * 1.1f));
});
Run("Separated and overlapping holes remain open together", () =>
{
    var plate = new SteelPlateTarget();
    foreach (float x in new[] { -0.02f, -0.01f, 0.03f })
    {
        Vector3 p = plate.Position + new Vector3(x, 0f, 0f);
        Check(plate.TryHitSegment(p + Vector3.UnitZ, p - Vector3.UnitZ, out _, out Vector3 contact));
        plate.ApplyImpact(contact, ProjectileImpactData.From(WeaponProfile.BarrettM82A1().Ballistics,
            -Vector3.UnitZ * 838f), out _);
    }
    foreach (float x in new[] { -0.02f, -0.015f, -0.01f, 0.03f })
    {
        Vector3 p = plate.Position + new Vector3(x, 0f, 0f);
        Check(!plate.TryHitSegment(p + Vector3.UnitZ, p - Vector3.UnitZ, out _, out _));
    }
});
Run("Stopped hit produces dent, not a hole or concrete rubble", () =>
{
    var plate = new SteelPlateTarget();
    plate.TryHitSegment(plate.Position + Vector3.UnitZ, plate.Position - Vector3.UnitZ, out _, out Vector3 contact);
    var hit = plate.ApplyImpact(contact,
        ProjectileImpactData.From(WeaponProfile.MakarovPm().Ballistics, -Vector3.UnitZ * 315f), out _);
    Check(hit.Outcome == SteelImpactOutcome.Stopped && plate.Holes.Count == 0 && plate.Dents.Count == 1);
    Check(plate.SurfaceZ(Vector2.Zero, 1f) < SteelPlateTarget.ThicknessMeters * 0.5f);
    Check(plate.TryHitSegment(plate.Position + Vector3.UnitZ, plate.Position - Vector3.UnitZ, out _, out _));
});
Run("Exit continuation uses residual mass and energy; no repeated self-hit", () =>
{
    var plate = new SteelPlateTarget();
    var profile = WeaponProfile.BarrettM82A1().Ballistics;
    var bullet = new Bullet(plate.Position + Vector3.UnitZ, -Vector3.UnitZ * 838f, profile);
    bullet.Step(0.002f, new WindField(1), 0f, 0f);
    Check(plate.TryHitSegment(bullet.PreviousPosition, bullet.Position, out float t, out Vector3 point));
    bullet.MoveToSegmentFraction(t);
    var result = plate.ApplyImpact(point, ProjectileImpactData.From(profile, bullet.Velocity), out Vector3 exit);
    bullet.ContinueAfterMaterialImpact(exit, result.ExitVelocity, result.ResidualMassKg);
    Near(bullet.KineticEnergyJoules, result.ResidualEnergyJoules, 0.01f);
    bullet.Step(0.001f, new WindField(1), 0.002f, 0f);
    Check(!plate.TryHitSegment(bullet.PreviousPosition, bullet.Position, out _, out _));
    Check(bullet.KineticEnergyJoules < result.ResidualEnergyJoules);
});
Run("Reset removes damage; range changes reset damage", () =>
{
    var plate = new SteelPlateTarget();
    Perforate(plate);
    int revision = plate.Revision;
    plate.ResetDamage();
    Check(plate.Holes.Count == 0 && plate.Fragments.Count == 0 && plate.Revision > revision);
    Perforate(plate);
    plate.SetDistance(500f, 5f);
    Check(plate.Holes.Count == 0);
    Near(plate.Position.Z + SteelPlateTarget.ThicknessMeters * 0.5f, -495f, 0.0001f);
});
Run("Steel mesh has finite vertices, unit normals and physical dimensions", () =>
{
    var mesh = SteelPlateGeometry.Build(new SteelPlateTarget());
    Check(mesh.Vertices.Length > 0 && mesh.Indices.Length % 3 == 0);
    float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity, maxRadius = 0f;
    for (int i = 0; i < mesh.Vertices.Length; i += 6)
    {
        Vector3 p = new(mesh.Vertices[i], mesh.Vertices[i + 1], mesh.Vertices[i + 2]);
        Vector3 n = new(mesh.Vertices[i + 3], mesh.Vertices[i + 4], mesh.Vertices[i + 5]);
        Check(float.IsFinite(p.LengthSquared));
        Near(n.Length, 1f, 0.001f);
        Check(p.Xy.Length <= 0.50001f);
        minZ = MathF.Min(minZ, p.Z); maxZ = MathF.Max(maxZ, p.Z);
        maxRadius = MathF.Max(maxRadius, p.Xy.Length);
    }
    Near(maxZ - minZ, 0.01f, 1e-6f);
    Near(maxRadius, 0.5f, 1e-6f);
});
Run("Concrete cube and house still catch high-speed segments", () =>
{
    var cube = new Target();
    Check(cube.TryHitSegment(cube.Position + Vector3.UnitZ * 2f, cube.Position - Vector3.UnitZ * 2f, out _, out _));
    var house = new HouseTarget();
    Vector3 p = house.Position + new Vector3(3.75f, 0f, 0f);
    Check(house.TryHitSegment(p + Vector3.UnitZ * 10f, p - Vector3.UnitZ * 10f, out _, out _));
});

ConcretePhysicsTests.Run(Run);
WeaponPhysicsTests.Run(Run);
Console.WriteLine($"{passed} passed, {failed} failed");
foreach (WeaponProfile weapon in Weapons())
    Console.WriteLine($"MODEL ESTIMATE {weapon.DisplayName}: normal-incidence limit {Solve(weapon, 800f).BallisticLimitMps:0} m/s");
return failed == 0 ? 0 : 1;

void Run(string name, Action test)
{
    try { test(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
static void Near(float actual, float expected, float tolerance)
{
    if (MathF.Abs(actual - expected) > tolerance || !float.IsFinite(actual))
        throw new Exception($"Expected {expected} +/- {tolerance}, got {actual}");
}
static IEnumerable<WeaponProfile> Weapons() => WeaponProfile.CreateAll();
static SteelImpactResult Solve(WeaponProfile weapon, float speed, float degrees = 0f, float thickness = 0.01f)
{
    float a = MathHelper.DegreesToRadians(degrees);
    Vector3 velocity = new Vector3(MathF.Sin(a), 0f, -MathF.Cos(a)) * speed;
    return SteelImpactModel.Solve(new(), ProjectileImpactData.From(weapon.Ballistics, velocity), Vector3.UnitZ, thickness);
}
static void Perforate(SteelPlateTarget plate)
{
    Check(plate.TryHitSegment(plate.Position + plate.Normal, plate.Position - plate.Normal, out _, out Vector3 contact));
    var hit = plate.ApplyImpact(contact,
        ProjectileImpactData.From(WeaponProfile.BarrettM82A1().Ballistics, -plate.Normal * 838f), out _);
    Check(hit.Outcome == SteelImpactOutcome.Perforated);
}
