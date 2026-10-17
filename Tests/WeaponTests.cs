using OpenTK.Mathematics;
using Sniper3D;

internal static class WeaponPhysicsTests
{
    public static void Run(Action<string, Action> run)
    {
        run("Weapons: shared catalogue includes five distinct profiles and NTW-20", () =>
        {
            WeaponProfile[] weapons = WeaponProfile.CreateAll();
            Check(weapons.Length == 5 && weapons.Select(w => w.DisplayName).Distinct().Count() == 5);
            Check(weapons[^1].HudName == "DENEL NTW-20");
            foreach (WeaponProfile weapon in weapons)
            {
                BallisticProfile p = weapon.Ballistics;
                Check(p.MassKg > 0 && p.DiameterMeters > 0 && p.MuzzleVelocityMps > 0);
                Check(float.IsFinite(p.EffectiveDragCoefficient(p.MuzzleVelocityMps)) && p.EffectiveDragCoefficient(p.MuzzleVelocityMps) > 0);
            }
        });
        run("NTW-20: inert 20x82 profile has nominal 28.512 kJ and manual cycle", () =>
        {
            WeaponProfile weapon = WeaponProfile.DenelNtw20();
            BallisticProfile p = weapon.Ballistics;
            Near(p.MassKg, 0.110f, 1e-6f);
            Near(p.DiameterMeters, 0.020f, 1e-6f);
            Near(p.MuzzleVelocityMps, 720f, 1e-5f);
            Near(p.KineticEnergyJoules(p.MuzzleVelocityMps), 28512f, 0.01f);
            Check(!weapon.IsAutomatic && weapon.ShotIntervalSeconds == 3f);
            Check(p.Construction == ProjectileConstruction.InertSteelBody);
        });
        run("NTW-20: free flight loses speed and drops with finite trajectory", () =>
        {
            BallisticProfile p = WeaponProfile.DenelNtw20().Ballistics;
            var bullet = new Bullet(new(0f, 100f, 0f), -Vector3.UnitZ * p.MuzzleVelocityMps, p);
            var wind = new WindField(7);
            for (int i = 0; i < 1000; i++) bullet.Step(0.001f, wind, i * 0.001f, 0f);
            Check(bullet.IsAlive && float.IsFinite(bullet.Position.LengthSquared));
            Check(bullet.Position.Z < -500f && bullet.Position.Y < 100f);
            Check(bullet.SpeedMps < p.MuzzleVelocityMps && bullet.KineticEnergyJoules < 28512f);
            var random = new Random(42);
            for (int i = 0; i < 100; i++)
                Check(MathF.Abs(p.SampleMuzzleVelocity(random) - 720f) <= 12.001f);
        });
        run("NTW-20: steel perforation continues with correct residual mass and energy", () =>
        {
            BallisticProfile p = WeaponProfile.DenelNtw20().Ballistics;
            var plate = new SteelPlateTarget();
            Check(plate.TryHitSegment(plate.Position + Vector3.UnitZ, plate.Position - Vector3.UnitZ,
                out _, out Vector3 contact, p.DiameterMeters * 0.5f));
            SteelImpactResult hit = plate.ApplyImpact(contact,
                ProjectileImpactData.From(p, -Vector3.UnitZ * p.MuzzleVelocityMps), out Vector3 exit);
            Check(hit.Outcome == SteelImpactOutcome.Perforated && plate.Holes.Count == 1);
            Near(hit.ResidualEnergyJoules + hit.DepositedEnergyJoules, 28512f, 0.02f);
            var bullet = new Bullet(contact, -Vector3.UnitZ * p.MuzzleVelocityMps, p);
            bullet.ContinueAfterMaterialImpact(exit, hit.ExitVelocity, hit.ResidualMassKg);
            Check(bullet.MassKg < p.MassKg && bullet.SpeedMps < p.MuzzleVelocityMps);
            Near(bullet.KineticEnergyJoules, hit.ResidualEnergyJoules, 0.02f);
            bullet.Step(0.001f, new WindField(7), 0f, 0f);
            Check(!plate.TryHitSegment(bullet.PreviousPosition, bullet.Position, out _, out _, p.DiameterMeters * 0.5f));
        });
        run("NTW-20: 20 mm projectile cannot fit through a .50 bore", () =>
        {
            var plate = new SteelPlateTarget();
            Check(plate.TryHitSegment(plate.Position + Vector3.UnitZ, plate.Position - Vector3.UnitZ,
                out _, out Vector3 contact));
            plate.ApplyImpact(contact, ProjectileImpactData.From(WeaponProfile.BarrettM82A1().Ballistics,
                -Vector3.UnitZ * 838f), out _);
            Check(plate.Holes.Count == 1);
            Check(plate.TryHitSegment(plate.Position + Vector3.UnitZ, plate.Position - Vector3.UnitZ,
                out _, out _, WeaponProfile.DenelNtw20().Ballistics.DiameterMeters * 0.5f));
        });
        run("NTW-20: concrete damage is wider than .50 without an energy explosion", () =>
        {
            var ntwTarget = new Target(); var barrettTarget = new Target();
            var ntw = Hit(ntwTarget, WeaponProfile.DenelNtw20());
            var barrett = Hit(barrettTarget, WeaponProfile.BarrettM82A1());
            Check(!ntw.Perforated && ntw.CraterRadiusMeters > barrett.CraterRadiusMeters,
                $"NTW radius {ntw.CraterRadiusMeters}; Barrett {barrett.CraterRadiusMeters}");
            // More energy does not imply greater depth or more whole 25 mm cells:
            // calibre, sectional mass and voxel boundaries also matter.
            Check(ntwTarget.DetachedMassKg > 0f);
            double kinetic = ntwTarget.Fragments.Sum(f => (double)f.KineticEnergyJoules);
            Check(kinetic <= ntw.DepositedEnergyJoules * 0.025 + 0.01);
            Check(ntw.CrushingWorkJoules + ntw.FractureWorkJoules + kinetic <= ntw.DepositedEnergyJoules + 0.01);
            Check(Math.Abs(ntwTarget.AttachedMassKg + ntwTarget.DetachedMassKg - 2400) < 0.002);
        });
        run("NTW-20: house scene accepts 20 mm impacts with finite damage", () =>
        {
            var house = new HouseTarget();
            Vector3 p = house.Position + new Vector3(3.75f, 0f, 0f);
            Check(house.TryHitSegment(p + Vector3.UnitZ * 10f, p - Vector3.UnitZ * 10f,
                out _, out Vector3 contact));
            BallisticProfile profile = WeaponProfile.DenelNtw20().Ballistics;
            var hit = house.ApplyImpact(contact, ProjectileImpactData.From(profile, -Vector3.UnitZ * profile.MuzzleVelocityMps));
            Check(hit.DepositedEnergyJoules > 0f && float.IsFinite(hit.ResidualSpeedMps));
            Check(house.Integrity01 is >= 0f and <= 1f);
        });
    }

    private static ConcreteImpactResult Hit(Target target, WeaponProfile weapon)
    {
        Check(target.TryHitSegment(target.Position + Vector3.UnitZ, target.Position - Vector3.UnitZ,
            out _, out Vector3 point));
        return target.ApplyImpact(point, ProjectileImpactData.From(weapon.Ballistics,
            -Vector3.UnitZ * weapon.Ballistics.MuzzleVelocityMps));
    }
    private static void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static void Near(float a, float b, float tolerance)
    { if (!float.IsFinite(a) || MathF.Abs(a - b) > tolerance) throw new Exception($"Expected {b} +/- {tolerance}, got {a}"); }
}
