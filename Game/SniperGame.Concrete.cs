using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Sniper3D;

internal sealed partial class SniperGame
{
    private void DrawConcreteHud()
    {
        if (_activeScene != SceneType.RangeCube) return;
        GL.Disable(EnableCap.DepthTest);
        var lines = new List<float>();
        DrawHudText(lines, $"CONCRETE 1M - 40MPA - MASS {_target.AttachedMassKg:0.0} KG", 18f, 20f, 3.6f);
        DrawHudText(lines, $"PEN {_target.LastImpact.PenetrationDepthMeters * 100f:0.0} CM - DEBRIS {_target.Fragments.Count} - R RESET", 18f, 43f, 3.6f);
        DrawHudLines(lines, new(0.88f, 0.90f, 0.93f), 1.5f);
        GL.Enable(EnableCap.DepthTest);
    }
}
