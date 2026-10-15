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
                    break;
                case 13:
                    game._activeScene = SceneType.DestructibleHouse;
                    game.SetActiveSceneDistance(100f);
                    break;
                case 15:
                    complete = true;
                    game.Close();
                    break;
            }
        };
        game.Run();
        if (!complete)
            throw new Exception("Graphics smoke test ended before checks completed");
        Console.WriteLine($"PASS graphics: {frames} frames, three scenes, menu, observer/PIP, steel impacts, no GL errors");
        return 0;

        void Shoot(int weapon, float seconds)
        {
            game._bullets.Clear();
            game._yaw = game._targetYaw = -90f;
            game._pitch = game._targetPitch = 0f;
            game._aimSwayYaw = game._aimSwayPitch = 0f;
            game._activeWeaponIndex = weapon;
            game._zeroDistanceMeters = 100;
            game.RecalculateZeroing();
            game.Fire();
            for (int i = 0; i < seconds / BallisticStepSeconds; i++)
                game.StepBallistics(BallisticStepSeconds);
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
