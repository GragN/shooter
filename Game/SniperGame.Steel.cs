using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Sniper3D;

internal sealed partial class SniperGame
{
    private void RegisterSteelHit(Bullet bullet, Vector3 point, ProjectileImpactData projectile, bool firstHit)
    {
        if (firstHit)
            _hits++;
        CaptureImpactTelemetry(bullet);
        SteelImpactResult result = _steel.ApplyImpact(point, projectile, out Vector3 exit);
        string action = result.Outcome switch
        {
            SteelImpactOutcome.Perforated => $"PERFORATED - EXIT {result.ExitVelocity.Length:0} M/S",
            SteelImpactOutcome.Ricochet => $"RICOCHET - EXIT {result.ExitVelocity.Length:0} M/S",
            _ => $"STOPPED - DENT {result.DentDepthMeters * 1000f:0.00} MM"
        };
        _lastShot = $"STEEL {action} | ANGLE {result.IncidenceDegrees:0} DEG | LOSS {result.DepositedEnergyJoules:0} J";
        if (result.Outcome != SteelImpactOutcome.Stopped && result.ExitVelocity.LengthSquared > 25f)
            bullet.ContinueAfterMaterialImpact(exit, result.ExitVelocity, result.ResidualMassKg);
        else
            bullet.Impact(point);
        UpdateWindowTitle();
    }

    private void RotateSteelPlate(float deltaDegrees)
    {
        _steel.SetYaw(_steel.YawDegrees + deltaDegrees);
        _impactPredictionEnabled = false;
        _hasPredictedImpact = false;
        _impactPredictionLockedRangeMeters = 0f;
        _lastShot = $"STEEL YAW {_steel.YawDegrees:0} DEG";
        UpdateWindowTitle();
    }

    private void DrawSteelRange()
    {
        float front = _cameraPosition.Z + 20f;
        float end = _steel.Position.Z - 120f;
        DrawCube(new(0f, -0.15f, (front + end) * 0.5f),
            new(50f, 0.3f, front - end), new(0.25f, 0.27f, 0.29f));
        DrawRangeMarkers(_steel.DistanceMeters);
    }

    private void DrawSteelPlate()
    {
        if (_shader is null)
            return;
        if (_steelMeshRevision != _steel.Revision)
        {
            var geometry = SteelPlateGeometry.Build(_steel);
            Mesh replacement = new(geometry.Vertices, geometry.Indices);
            _steelMesh?.Dispose();
            _steelMesh = replacement;
            _steelMeshRevision = _steel.Revision;
        }

        // The 61.7 kg plate is clamped into a rigid support, not a swinging gong.
        Vector3 steelColor = new(0.72f, 0.76f, 0.81f);
        _shader.Use();
        _shader.SetInt("useLighting", 2);
        _shader.SetMatrix4("model", _steel.Transform);
        _shader.SetVector3("objectColor", steelColor);
        _steelMesh?.Draw();

        Frame(new(-0.65f, -0.55f, 0f), new(0.06f, 2.3f, 0.10f), new(0.17f));
        Frame(new(0.65f, -0.55f, 0f), new(0.06f, 2.3f, 0.10f), new(0.17f));
        Frame(new(0f, 0.60f, 0f), new(1.4f, 0.06f, 0.10f), new(0.17f));
        Frame(new(-0.28f, 0.48f, 0f), new(0.045f, 0.20f, 0.025f), new(0.22f));
        Frame(new(0.28f, 0.48f, 0f), new(0.045f, 0.20f, 0.025f), new(0.22f));

        foreach (MetalFragment fragment in _steel.Fragments)
        {
            Vector3 color = fragment.Hot && fragment.Age < 0.08f
                ? new(1f, 0.65f, 0.18f) : new(0.37f, 0.40f, 0.43f);
            DrawCube(fragment.Position, new(fragment.Size), color);
        }

        void Frame(Vector3 position, Vector3 scale, Vector3 color) => DrawCubeRotated(
            _steel.ToWorldPoint(position), scale, color, new(0f, MathHelper.DegreesToRadians(_steel.YawDegrees), 0f));
    }

    private void DrawSteelHud()
    {
        if (_activeScene != SceneType.SteelPlate)
            return;
        GL.Disable(EnableCap.DepthTest);
        var lines = new List<float>();
        DrawHudText(lines, $"S355 - DIAMETER 1M - THICKNESS 10MM - {_steel.MassKg:0.0}KG", 18f, 20f, 3.6f);
        DrawHudText(lines, $"YAW {_steel.YawDegrees:0} DEG - HOLES {_steel.Holes.Count} - [ ] ROTATE - R RESET", 18f, 43f, 3.6f);
        // Keep the complete impact result visible inside the game window.
        DrawHudText(lines, _lastShot.Split('|')[0], 18f, 68f, 3.4f);
        DrawHudLines(lines, new(0.88f, 0.90f, 0.93f), 1.5f);
        GL.Enable(EnableCap.DepthTest);
    }
}
