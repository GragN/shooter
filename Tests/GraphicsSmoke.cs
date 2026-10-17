using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace Sniper3D;

internal static class GraphicsSmoke
{
    private static int Main() => SniperGame.RunGraphicsSmoke();
}

internal sealed partial class SniperGame
{
    internal static int RunGraphicsSmoke()
    {
        using var game = new SniperGame(GameWindowSettings.Default, new NativeWindowSettings
        {
            ClientSize = new(1280, 720), APIVersion = new(3, 3), Profile = ContextProfile.Core,
            Title = "Sniper3D graphics smoke test", StartVisible = true
        });
        int frames = 0;
        bool complete = false;
        game.UpdateFrame += _ =>
        {
            ErrorCode error = GL.GetError();
            if (error != ErrorCode.NoError)
                throw new Exception($"OpenGL error after frame {frames}: {error}");
            frames++;
            game._yaw = game._targetYaw = -90f;
            game._pitch = game._targetPitch = 0f;
            switch (frames)
            {
                case 1:
                    Console.WriteLine($"GL {GL.GetString(StringName.Version)} / {GL.GetString(StringName.Renderer)}");
                    game.OpenDistanceMenu();
                    var layout = game.GetDistanceMenuLayout();
                    game.HandleDistanceMenuMouseDown(new(layout.Scene3Left + layout.SceneButtonWidth * 0.5f,
                        layout.SceneTop + layout.SceneButtonHeight * 0.5f));
                    game._menuDistanceMeters = 100;
                    game._menuWindLimitMps = 0;
                    game.ApplyDistanceMenu();
                    if (game._activeScene != SceneType.SteelPlate)
                        throw new Exception("Third scene menu button did not select steel");
                    game._stabilizationEnabled = true;
                    game._aimingEnabled = true;
                    game._currentFov = 4f;
                    Shoot(3, 0.4f);
                    if (game._steel.Holes.Count != 1 || game._hits != 1)
                        throw new Exception("Barrett shot did not perforate the steel scene");
                    break;
                case 3:
                    Capture("steel");
                    game.OpenDistanceMenu();
                    break;
                case 5:
                    Capture("menu");
                    game.CloseDistanceMenu();
                    game._cameraMode = CameraMode.TargetObserver;
                    break;
                case 7:
                    Capture("observer");
                    game._steel.ResetDamage();
                    game.RotateSteelPlate(75f);
                    Shoot(1, 0.8f);
                    if (game._steel.Holes.Count != 0 || game._steel.HitCount != 1)
                        throw new Exception("Grazing PM impact did not reach steel / incorrectly perforated");
                    break;
                case 9:
                    game.RestoreActiveDestructible();
                    game._steel.SetYaw(0f);
                    Shoot(0, 0.4f);
                    if (game._steel.Holes.Count == 0)
                        throw new Exception("SVD at 100 m did not perforate the model plate");
                    break;
                case 11:
                    game._activeScene = SceneType.RangeCube;
                    game.SetActiveSceneDistance(100f);
                    // Aim at the raised concrete sample, rather than below its bottom face.
                    ShootConcrete(3);
                    if (game._target.DetachedMassKg <= 0 || game._target.LastImpact.Perforated)
                        throw new Exception("A .50 shot did not damage / incorrectly perforated intact 1 m concrete");
                    break;
                case 13:
                    Capture("concrete");
                    // Repeated hits update the cached damage mesh, and reset must
                    // clear both physical bodies and their rendering resources.
                    ShootConcrete(0);
                    for (int i=0; i<120; i++) game._target.UpdatePhysics(1f/120f, 0f);
                    break;
                case 15:
                    Capture("concrete-debris");
                    game.RestoreActiveDestructible();
                    if (game._target.Fragments.Count != 0 || game._target.Integrity01 != 1f)
                        throw new Exception("Concrete reset did not restore intact sample");
                    game._activeScene = SceneType.DestructibleHouse;
                    game.SetActiveSceneDistance(100f);
                    break;
                case 17:
                    game.OpenDistanceMenu();
                    var weaponsLayout = game.GetDistanceMenuLayout();
                    for (int i = 0; i < game._weapons.Length; i++)
                    {
                        float left = weaponsLayout.WeaponButtonLeft(i);
                        if (left < weaponsLayout.SliderLeft || left + weaponsLayout.WeaponButtonWidth > weaponsLayout.SliderRight + 0.01f)
                            throw new Exception("Weapon button lies outside menu");
                        game.HandleDistanceMenuMouseDown(new(left + weaponsLayout.WeaponButtonWidth * 0.5f,
                            weaponsLayout.WeaponTop + weaponsLayout.WeaponButtonHeight * 0.5f));
                        if (game._menuWeaponIndex != i)
                            throw new Exception($"Menu click did not select weapon {i}");
                    }
                    game._menuScene = SceneType.RangeCube;
                    game.ApplyDistanceMenu();
                    if (game.ActiveWeapon.HudName != "DENEL NTW-20" || game._shotCooldownSeconds != 0f)
                        throw new Exception("NTW-20 selection did not apply / retained old cooldown");
                    ShootConcrete(4);
                    if (game._target.DetachedMassKg <= 0 || game._lastImpactEnergy < 20000f)
                        throw new Exception("NTW-20 did not hit concrete with its own projectile profile");
                    int count = game._shots;
                    game.Fire();
                    if (game._shots != count) throw new Exception("NTW-20 fired before cycling");
                    game.UpdateWeaponCycle(game.ActiveWeapon.ShotIntervalSeconds - 0.1f);
                    game.Fire();
                    if (game._shots != count) throw new Exception("NTW-20 cycled too early");
                    game.UpdateWeaponCycle(0.2f);
                    game.Fire();
                    if (game._shots != count + 1) throw new Exception("NTW-20 did not fire after cycling");
                    game._bullets.Clear();
                    break;
                case 19:
                    Capture("ntw-concrete");
                    game.OpenDistanceMenu();
                    break;
                case 21:
                    Capture("ntw-menu");
                    game._menuScene = SceneType.SteelPlate;
                    game.ApplyDistanceMenu();
                    Shoot(4, 0.4f);
                    if (game._steel.Holes.Count != 1 || !game._lastShot.StartsWith("STEEL PERFORATED") || game._lastImpactEnergy < 20000f)
                        throw new Exception("NTW-20 did not perforate the steel scene");
                    break;
                case 23:
                    Capture("ntw-steel");
                    game.RestoreActiveDestructible();
                    if (game._shotCooldownSeconds != 0f || game._steel.Holes.Count != 0)
                        throw new Exception("Reset did not clear NTW-20 cycling and steel damage");
                    game._activeScene = SceneType.DestructibleHouse;
                    break;
                case 25:
                    complete = true;
                    game.Close();
                    break;
            }
        };
        game.Run();
        if (!complete)
            throw new Exception("Graphics smoke test ended before checks completed");
        Console.WriteLine($"PASS graphics: {frames} frames, three scenes, five menu weapons, NTW-20 cycling, steel + concrete impacts, observer/PIP and reset, no GL errors");
        return 0;

        void Shoot(int weapon, float seconds)
        {
            game._bullets.Clear();
            game._yaw = game._targetYaw = -90f;
            game._pitch = game._targetPitch = 0f;
            game._aimSwayYaw = game._aimSwayPitch = 0f;
            game._activeWeaponIndex = weapon;
            game._shotCooldownSeconds = 0f;
            game._zeroDistanceMeters = 100;
            game.RecalculateZeroing();
            game.Fire();
            for (int i = 0; i < seconds / BallisticStepSeconds; i++)
                game.StepBallistics(BallisticStepSeconds);
        }
        void ShootConcrete(int weapon)
        {
            game._bullets.Clear();
            game._yaw = game._targetYaw = -90f;
            game._pitch = game._targetPitch = MathHelper.RadiansToDegrees(MathF.Atan2(
                game._target.Position.Y-game._cameraPosition.Y,game._target.DistanceMeters));
            game._aimSwayYaw = game._aimSwayPitch = 0f;
            game._activeWeaponIndex = weapon;
            game._shotCooldownSeconds = 0f;
            game._zeroDistanceMeters = 100;
            game.RecalculateZeroing();
            game.Fire();
            for (int i=0; i<400; i++) game.StepBallistics(BallisticStepSeconds);
        }
        void Capture(string name)
        {
            string? directory = Environment.GetEnvironmentVariable("SNIPER3D_SCREENSHOT_DIR");
            int width = game.ClientSize.X, height = game.ClientSize.Y;
            byte[] pixels = new byte[width * height * 3];
            GL.ReadBuffer(ReadBufferMode.Back);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            GL.ReadPixels(0, 0, width, height, PixelFormat.Rgb, PixelType.UnsignedByte, pixels);
            if (pixels.All(value => value == 0))
                throw new Exception($"Empty framebuffer for {name}");
            if (string.IsNullOrEmpty(directory))
                return;
            Directory.CreateDirectory(directory);
            using var file = File.Create(Path.Combine(directory, name + ".ppm"));
            byte[] header = System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
            file.Write(header);
            for (int y = height - 1; y >= 0; y--)
                file.Write(pixels, y * width * 3, width * 3);
        }
    }
}
