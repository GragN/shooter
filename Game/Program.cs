using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace Sniper3D;

internal static class Program
{
    private static void Main()
    {
        var gameWindowSettings = GameWindowSettings.Default;

        var nativeWindowSettings = new NativeWindowSettings
        {
            ClientSize = new Vector2i(1280, 720),
            Title = "Sniper 3D - v3.4 STEEL",
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core,
            NumberOfSamples = 4
        };

        using var game = new SniperGame(gameWindowSettings, nativeWindowSettings);
        game.Run();
    }
}
