using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Sniper3D;

internal sealed partial class SniperGame
{
    private void UpdateWindowTitle()
    {
        float accuracy = _shots == 0 ? 0f : _hits * 100f / _shots;

        string telemetry;
        Bullet? flying = _bullets.LastOrDefault(b => b.IsAlive && !b.HasImpacted);
        if (flying is not null)
        {
            telemetry = $"MV {flying.InitialSpeedMps:0.0} m/s | Flight {flying.AgeSeconds:0.00}s | {flying.SpeedMps:0} m/s";
        }
        else if (_lastFlightTime > 0f)
        {
            telemetry = $"MV {_lastMuzzleVelocity:0.0} m/s | TOF {_lastFlightTime:0.00}s | Impact {_lastImpactSpeed:0} m/s | {_lastImpactEnergy / 1000f:0.00} kJ";
        }
        else
        {
            telemetry = $"Muzzle {ActiveBallistics.MuzzleVelocityMps:0} +/- {ActiveBallistics.MuzzleVelocityStdDevMps:0.0} m/s | Bullet {ActiveBallistics.MassKg * 1000f:0.0} g";
        }

        if (_distanceMenuOpen)
        {
            string menuSceneName = SceneHudName(_menuScene);
            Title = $"Sniper 3D v3.6 MATERIAL PHYSICS | SETTINGS | {menuSceneName} | {_weapons[_menuWeaponIndex].DisplayName} | {_menuDistanceMeters} m | {ScopeMagnifications[_menuScopeMagnificationIndex]:0}x | Wind max {_menuWindLimitMps} m/s | Mouse + APPLY | M/Esc cancel";
        }
        else
        {
            string fireHint = ActiveWeapon.IsAutomatic ? "Hold LMB auto" : "LMB fire";
            if (_shotCooldownSeconds > 0f)
                fireHint = $"BOLT CYCLE {_shotCooldownSeconds:0.0}s";
            string cameraStatus = _cameraMode switch
            {
                CameraMode.Shooter => "CAM 1 SHOOTER",
                CameraMode.Bullet => TrackedBulletCamera is not null ? "CAM 2 BULLET" : "CAM 2 BULLET ARMED",
                CameraMode.TargetObserver => "CAM 3 TARGET + SHOOTER PIP",
                _ => "CAM"
            };

            string stabilizationStatus = _stabilizationEnabled ? "O STABLE ON" : "O STABLE OFF";
            string condition = _activeScene == SceneType.SteelPlate
                ? $"Holes {_steel.Holes.Count} | Plate yaw {_steel.YawDegrees:0} deg | [ ] rotate"
                : $"Structure {ActiveSceneIntegrity01 * 100f:0}%";
            Title = $"Sniper 3D v3.6 MATERIAL PHYSICS | {ActiveSceneHudName} | {ActiveWeapon.DisplayName} | {ActiveSceneDistanceMeters:0} m | Scope {ScopeMagnifications[_scopeMagnificationIndex]:0}x | ZERO {_zeroDistanceMeters} m | Wind max {_activeWindLimitMps} m/s | {cameraStatus} | {stabilizationStatus} | {condition} | Debris {ActiveSceneDebrisCount} | Hits {_hits}/{_shots} ({accuracy:0}%) | {_lastShot} | {telemetry} | {fireHint} | RMB toggle zoom | Wheel ZERO 0..2000m / 100m | {(_impactPredictionEnabled ? $"T LOCK {_impactPredictionLockedRangeMeters:0}m" : "T LOCK OFF")} | 1 shooter | 2 bullet cam | 3 target cam + PIP | O no recoil/sway | M settings | R restore";
        }
    }
}
