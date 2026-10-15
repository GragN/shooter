using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Sniper3D;

internal sealed partial class SniperGame
{
    private void CreateHudOverlay()
    {
        _hudVao = GL.GenVertexArray();
        _hudVbo = GL.GenBuffer();

        GL.BindVertexArray(_hudVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _hudVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, 4096 * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.BindVertexArray(0);
    }

    private void DrawWindHud()
    {
        if (_shader is null || _hudVao == 0 || _hudVbo == 0 || ClientSize.X <= 0 || ClientSize.Y <= 0)
            return;

        // HUD использует тот же активный лимит, что и физика пули.
        Vector3 wind = WindMath.ClampVelocity(
            _wind.SampleNow(_cameraPosition, _activeWindLimitMps),
            _activeWindLimitMps);

        float displayedSpeed = wind.Length;

        Vector3 forwardHorizontal = new(BaseFront.X, 0f, BaseFront.Z);
        if (forwardHorizontal.LengthSquared < 0.0001f)
            forwardHorizontal = -Vector3.UnitZ;
        forwardHorizontal = Vector3.Normalize(forwardHorizontal);

        Vector3 right = Vector3.Normalize(Vector3.Cross(forwardHorizontal, Vector3.UnitY));
        float crossWind = Vector3.Dot(wind, right);
        float alongWind = Vector3.Dot(wind, forwardHorizontal);
        float horizontalSpeed = MathF.Sqrt(crossWind * crossWind + alongWind * alongWind);

        var lines = new List<float>(384);

        float centerX = ClientSize.X - 55f;
        float centerY = 40f;
        float dirX = 0f;
        float dirY = -1f;

        if (horizontalSpeed > 0.0001f)
        {
            dirX = crossWind / horizontalSpeed;
            dirY = -alongWind / horizontalSpeed;
        }

        float baseX = centerX - dirX * 12f;
        float baseY = centerY - dirY * 12f;
        float tipX = centerX + dirX * 14f;
        float tipY = centerY + dirY * 14f;
        AddHudLine(lines, baseX, baseY, tipX, tipY);

        float perpX = -dirY;
        float perpY = dirX;
        float headBackX = tipX - dirX * 8f;
        float headBackY = tipY - dirY * 8f;
        AddHudLine(lines, tipX, tipY, headBackX + perpX * 5f, headBackY + perpY * 5f);
        AddHudLine(lines, tipX, tipY, headBackX - perpX * 5f, headBackY - perpY * 5f);

        const float glyphScale = 5f;
        int windTenths = (int)MathF.Round(displayedSpeed * 10f);
        int windWhole = windTenths / 10;
        int windFraction = Math.Abs(windTenths % 10);
        string speedText = $"WIND {windWhole},{windFraction} M/S";
        float speedWidth = MeasureHudText(speedText, glyphScale);
        DrawHudText(lines, speedText, ClientSize.X - 12f - speedWidth, 66f, glyphScale);

        if (lines.Count == 0)
            return;

        GL.Disable(EnableCap.DepthTest);

        _shader.Use();
        _shader.SetInt("useLighting", 0);
        _shader.SetMatrix4("model", Matrix4.Identity);
        _shader.SetMatrix4("view", Matrix4.Identity);
        _shader.SetMatrix4("projection", Matrix4.Identity);
        _shader.SetVector3("objectColor", new Vector3(0.78f, 0.80f, 0.82f));

        GL.BindBuffer(BufferTarget.ArrayBuffer, _hudVbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, lines.Count * sizeof(float), lines.ToArray());

        GL.BindVertexArray(_hudVao);
        GL.LineWidth(2f);
        GL.DrawArrays(PrimitiveType.Lines, 0, lines.Count / 3);
        GL.BindVertexArray(0);

        GL.Enable(EnableCap.DepthTest);
    }

    private void ValidateWindLimiterInvariant()
    {
        float[] limits = { 0f, 1f, 2f, 5f, 20f, 50f };
        Vector3[] positions =
        {
            _cameraPosition,
            _cameraPosition + new Vector3(15f, 2f, -100f),
            _cameraPosition + new Vector3(-30f, 8f, -750f),
            _cameraPosition + new Vector3(5f, 15f, -1900f)
        };

        foreach (float limit in limits)
        {
            foreach (Vector3 position in positions)
            {
                for (int i = 0; i < 8; i++)
                {
                    Vector3 sample = _wind.Sample(position, i * 7.25f, limit);
                    float speed = sample.Length;
                    if (!float.IsFinite(speed) || speed > limit + 0.001f)
                    {
                        throw new InvalidOperationException(
                            $"Wind limiter invariant failed: speed={speed:0.000}, limit={limit:0.000}");
                    }

                    if (limit > 0f)
                    {
                        float minExpected = limit * 0.70f;
                        if (speed < minExpected - 0.001f)
                        {
                            throw new InvalidOperationException(
                                $"Wind profile invariant failed: speed={speed:0.000}, expected >= {minExpected:0.000}");
                        }
                    }
                }
            }
        }
    }

    private void DrawDistanceMenu()
    {
        if (_shader is null || _hudVao == 0 || _hudVbo == 0 || ClientSize.X <= 0 || ClientSize.Y <= 0)
            return;

        GL.Disable(EnableCap.DepthTest);

        DistanceMenuLayout layout = GetDistanceMenuLayout();
        Vector2 mouse = MouseState.Position;
        bool applyHover = PointInRect(mouse, layout.ApplyLeft, layout.ButtonTop, layout.ButtonWidth, layout.ButtonHeight);
        bool cancelHover = PointInRect(mouse, layout.CancelLeft, layout.ButtonTop, layout.ButtonWidth, layout.ButtonHeight);
        bool scene1Hover = PointInRect(mouse, layout.Scene1Left, layout.SceneTop, layout.SceneButtonWidth, layout.SceneButtonHeight);
        bool scene2Hover = PointInRect(mouse, layout.Scene2Left, layout.SceneTop, layout.SceneButtonWidth, layout.SceneButtonHeight);
        bool weapon1Hover = PointInRect(mouse, layout.Weapon1Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight);
        bool weapon2Hover = PointInRect(mouse, layout.Weapon2Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight);
        bool weapon3Hover = PointInRect(mouse, layout.Weapon3Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight);
        bool weapon4Hover = PointInRect(mouse, layout.Weapon4Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight);

        DrawHudFilledRect(layout.Left, layout.Top, layout.Right - layout.Left, layout.Bottom - layout.Top, new Vector3(0.10f, 0.11f, 0.12f));

        DrawHudFilledRect(
            layout.Scene1Left, layout.SceneTop, layout.SceneButtonWidth, layout.SceneButtonHeight,
            _menuScene == SceneType.RangeCube ? new Vector3(0.34f, 0.36f, 0.38f) :
            scene1Hover ? new Vector3(0.25f, 0.27f, 0.29f) : new Vector3(0.17f, 0.18f, 0.19f));
        DrawHudFilledRect(
            layout.Scene2Left, layout.SceneTop, layout.SceneButtonWidth, layout.SceneButtonHeight,
            _menuScene == SceneType.DestructibleHouse ? new Vector3(0.34f, 0.36f, 0.38f) :
            scene2Hover ? new Vector3(0.25f, 0.27f, 0.29f) : new Vector3(0.17f, 0.18f, 0.19f));

        DrawHudFilledRect(
            layout.Weapon1Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight,
            _menuWeaponIndex == 0 ? new Vector3(0.34f, 0.36f, 0.38f) :
            weapon1Hover ? new Vector3(0.25f, 0.27f, 0.29f) : new Vector3(0.17f, 0.18f, 0.19f));
        DrawHudFilledRect(
            layout.Weapon2Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight,
            _menuWeaponIndex == 1 ? new Vector3(0.34f, 0.36f, 0.38f) :
            weapon2Hover ? new Vector3(0.25f, 0.27f, 0.29f) : new Vector3(0.17f, 0.18f, 0.19f));
        DrawHudFilledRect(
            layout.Weapon3Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight,
            _menuWeaponIndex == 2 ? new Vector3(0.34f, 0.36f, 0.38f) :
            weapon3Hover ? new Vector3(0.25f, 0.27f, 0.29f) : new Vector3(0.17f, 0.18f, 0.19f));
        DrawHudFilledRect(
            layout.Weapon4Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight,
            _menuWeaponIndex == 3 ? new Vector3(0.34f, 0.36f, 0.38f) :
            weapon4Hover ? new Vector3(0.25f, 0.27f, 0.29f) : new Vector3(0.17f, 0.18f, 0.19f));

        DrawHudFilledRect(
            layout.ApplyLeft, layout.ButtonTop, layout.ButtonWidth, layout.ButtonHeight,
            applyHover ? new Vector3(0.28f, 0.30f, 0.32f) : new Vector3(0.20f, 0.21f, 0.22f));
        DrawHudFilledRect(
            layout.CancelLeft, layout.ButtonTop, layout.ButtonWidth, layout.ButtonHeight,
            cancelHover ? new Vector3(0.25f, 0.26f, 0.27f) : new Vector3(0.17f, 0.18f, 0.19f));

        var lines = new List<float>(1800);
        AddHudRect(lines, layout.Left, layout.Top, layout.Right - layout.Left, layout.Bottom - layout.Top);

        const float titleScale = 6.4f;
        string title = "SETTINGS";
        float titleWidth = MeasureHudText(title, titleScale);
        DrawHudText(lines, title, (ClientSize.X - titleWidth) * 0.5f, layout.Top + 13f, titleScale);

        const float sectionScale = 4.7f;
        const float valueScale = 7.2f;
        const float tinyScale = 3.8f;

        // --- Сцена ---
        string sceneLabel = "SCENE";
        float sceneLabelWidth = MeasureHudText(sceneLabel, sectionScale);
        DrawHudText(lines, sceneLabel, (ClientSize.X - sceneLabelWidth) * 0.5f, layout.Top + 39f, sectionScale);
        AddHudRect(lines, layout.Scene1Left, layout.SceneTop, layout.SceneButtonWidth, layout.SceneButtonHeight);
        AddHudRect(lines, layout.Scene2Left, layout.SceneTop, layout.SceneButtonWidth, layout.SceneButtonHeight);

        const float sceneTextScale = 3.8f;
        string scene1 = "RANGE CUBE";
        string scene2 = "HOUSE";
        DrawHudText(lines, scene1,
            layout.Scene1Left + (layout.SceneButtonWidth - MeasureHudText(scene1, sceneTextScale)) * 0.5f,
            layout.SceneTop + 10f, sceneTextScale);
        DrawHudText(lines, scene2,
            layout.Scene2Left + (layout.SceneButtonWidth - MeasureHudText(scene2, sceneTextScale)) * 0.5f,
            layout.SceneTop + 10f, sceneTextScale);

        // --- Оружие ---
        string weaponLabel = "WEAPON";
        float weaponLabelWidth = MeasureHudText(weaponLabel, sectionScale);
        DrawHudText(lines, weaponLabel, (ClientSize.X - weaponLabelWidth) * 0.5f, layout.Top + 112f, sectionScale);

        AddHudRect(lines, layout.Weapon1Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight);
        AddHudRect(lines, layout.Weapon2Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight);
        AddHudRect(lines, layout.Weapon3Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight);
        AddHudRect(lines, layout.Weapon4Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight);

        const float weaponTextScale = 3.0f;
        float[] weaponLefts = { layout.Weapon1Left, layout.Weapon2Left, layout.Weapon3Left, layout.Weapon4Left };
        for (int i = 0; i < _weapons.Length; i++)
        {
            string weaponName = _weapons[i].HudName;
            float weaponNameWidth = MeasureHudText(weaponName, weaponTextScale);
            DrawHudText(lines, weaponName,
                weaponLefts[i] + (layout.WeaponButtonWidth - weaponNameWidth) * 0.5f,
                layout.WeaponTop + 10f, weaponTextScale);
        }

        WeaponProfile menuWeapon = _weapons[_menuWeaponIndex];
        BallisticProfile menuBallistics = menuWeapon.Ballistics;
        string weaponStats = menuWeapon.IsAutomatic
            ? $"{menuBallistics.MassKg * 1000f:0.0}G  {menuBallistics.MuzzleVelocityMps:0}M/S  {menuWeapon.RateOfFireRpm:0}RPM"
            : $"{menuBallistics.MassKg * 1000f:0.0}G  {menuBallistics.MuzzleVelocityMps:0}M/S";
        float weaponStatsWidth = MeasureHudText(weaponStats, 3.4f);
        DrawHudText(lines, weaponStats, (ClientSize.X - weaponStatsWidth) * 0.5f, layout.Top + 188f, 3.4f);

        // --- Дистанция ---
        string distanceLabel = "DISTANCE";
        DrawHudText(lines, distanceLabel,
            (ClientSize.X - MeasureHudText(distanceLabel, sectionScale)) * 0.5f,
            layout.Top + 216f, sectionScale);

        string distanceValue = $"{_menuDistanceMeters} M";
        DrawHudText(lines, distanceValue,
            (ClientSize.X - MeasureHudText(distanceValue, valueScale)) * 0.5f,
            layout.Top + 241f, valueScale);

        AddHudLine(lines, layout.SliderLeft, layout.DistanceSliderY, layout.SliderRight, layout.DistanceSliderY);
        AddHudLine(lines, layout.SliderLeft, layout.DistanceSliderY - 6f, layout.SliderLeft, layout.DistanceSliderY + 6f);
        AddHudLine(lines, layout.SliderRight, layout.DistanceSliderY - 6f, layout.SliderRight, layout.DistanceSliderY + 6f);
        float distanceT = (_menuDistanceMeters - MinTargetDistance) / (float)(MaxTargetDistance - MinTargetDistance);
        float distanceKnobX = layout.SliderLeft + (layout.SliderRight - layout.SliderLeft) * distanceT;
        DrawHudFilledRect(distanceKnobX - 7f, layout.DistanceSliderY - 12f, 14f, 24f, new Vector3(0.72f, 0.74f, 0.76f));
        AddHudRect(lines, distanceKnobX - 7f, layout.DistanceSliderY - 12f, 14f, 24f);
        DrawHudText(lines, "100M", layout.SliderLeft - 6f, layout.DistanceSliderY + 18f, tinyScale);
        string maxDistanceText = "2000M";
        DrawHudText(lines, maxDistanceText,
            layout.SliderRight - MeasureHudText(maxDistanceText, tinyScale) + 6f,
            layout.DistanceSliderY + 18f, tinyScale);

        // --- Кратность ---
        string scopeLabel = "SCOPE";
        DrawHudText(lines, scopeLabel,
            (ClientSize.X - MeasureHudText(scopeLabel, sectionScale)) * 0.5f,
            layout.Top + 326f, sectionScale);
        string scopeValue = $"{ScopeMagnifications[_menuScopeMagnificationIndex]:0}X";
        DrawHudText(lines, scopeValue,
            (ClientSize.X - MeasureHudText(scopeValue, valueScale)) * 0.5f,
            layout.Top + 351f, valueScale);

        AddHudLine(lines, layout.SliderLeft, layout.ScopeSliderY, layout.SliderRight, layout.ScopeSliderY);
        for (int i = 0; i < ScopeMagnifications.Length; i++)
        {
            float markT = i / (float)(ScopeMagnifications.Length - 1);
            float markX = layout.SliderLeft + (layout.SliderRight - layout.SliderLeft) * markT;
            AddHudLine(lines, markX, layout.ScopeSliderY - 7f, markX, layout.ScopeSliderY + 7f);
            string markText = $"{ScopeMagnifications[i]:0}X";
            DrawHudText(lines, markText,
                markX - MeasureHudText(markText, tinyScale) * 0.5f,
                layout.ScopeSliderY + 18f, tinyScale);
        }
        float scopeT = _menuScopeMagnificationIndex / (float)(ScopeMagnifications.Length - 1);
        float scopeKnobX = layout.SliderLeft + (layout.SliderRight - layout.SliderLeft) * scopeT;
        DrawHudFilledRect(scopeKnobX - 7f, layout.ScopeSliderY - 12f, 14f, 24f, new Vector3(0.72f, 0.74f, 0.76f));
        AddHudRect(lines, scopeKnobX - 7f, layout.ScopeSliderY - 12f, 14f, 24f);

        // --- Ограничение ветра ---
        string windLabel = "WIND LIMIT";
        DrawHudText(lines, windLabel,
            (ClientSize.X - MeasureHudText(windLabel, sectionScale)) * 0.5f,
            layout.Top + 436f, sectionScale);
        string windValue = $"{_menuWindLimitMps} M/S";
        DrawHudText(lines, windValue,
            (ClientSize.X - MeasureHudText(windValue, valueScale)) * 0.5f,
            layout.Top + 461f, valueScale);

        AddHudLine(lines, layout.SliderLeft, layout.WindSliderY, layout.SliderRight, layout.WindSliderY);
        for (int mark = 0; mark <= MaxWindLimitMps; mark += 10)
        {
            float markT = mark / (float)MaxWindLimitMps;
            float markX = layout.SliderLeft + (layout.SliderRight - layout.SliderLeft) * markT;
            AddHudLine(lines, markX, layout.WindSliderY - 6f, markX, layout.WindSliderY + 6f);
            string markText = $"{mark}";
            DrawHudText(lines, markText,
                markX - MeasureHudText(markText, tinyScale) * 0.5f,
                layout.WindSliderY + 18f, tinyScale);
        }
        float windT = (_menuWindLimitMps - MinWindLimitMps) / (float)(MaxWindLimitMps - MinWindLimitMps);
        float windKnobX = layout.SliderLeft + (layout.SliderRight - layout.SliderLeft) * windT;
        DrawHudFilledRect(windKnobX - 7f, layout.WindSliderY - 12f, 14f, 24f, new Vector3(0.72f, 0.74f, 0.76f));
        AddHudRect(lines, windKnobX - 7f, layout.WindSliderY - 12f, 14f, 24f);

        AddHudRect(lines, layout.ApplyLeft, layout.ButtonTop, layout.ButtonWidth, layout.ButtonHeight);
        AddHudRect(lines, layout.CancelLeft, layout.ButtonTop, layout.ButtonWidth, layout.ButtonHeight);
        const float buttonTextScale = 4.8f;
        string apply = "APPLY";
        string cancel = "CANCEL";
        DrawHudText(lines, apply,
            layout.ApplyLeft + (layout.ButtonWidth - MeasureHudText(apply, buttonTextScale)) * 0.5f,
            layout.ButtonTop + 8f, buttonTextScale);
        DrawHudText(lines, cancel,
            layout.CancelLeft + (layout.ButtonWidth - MeasureHudText(cancel, buttonTextScale)) * 0.5f,
            layout.ButtonTop + 8f, buttonTextScale);

        DrawHudLines(lines, new Vector3(0.80f, 0.82f, 0.84f), 2f);
        GL.Enable(EnableCap.DepthTest);
    }

    private void DrawHudFilledRect(float x, float y, float width, float height, Vector3 color)
    {
        float w = ClientSize.X;
        float h = ClientSize.Y;

        float x1 = x / w * 2f - 1f;
        float x2 = (x + width) / w * 2f - 1f;
        float y1 = 1f - y / h * 2f;
        float y2 = 1f - (y + height) / h * 2f;

        float[] vertices =
        {
            x1, y1, 0f,
            x2, y1, 0f,
            x2, y2, 0f,
            x1, y1, 0f,
            x2, y2, 0f,
            x1, y2, 0f
        };

        _shader!.Use();
        _shader.SetInt("useLighting", 0);
        _shader.SetMatrix4("model", Matrix4.Identity);
        _shader.SetMatrix4("view", Matrix4.Identity);
        _shader.SetMatrix4("projection", Matrix4.Identity);
        _shader.SetVector3("objectColor", color);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _hudVbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vertices.Length * sizeof(float), vertices);
        GL.BindVertexArray(_hudVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.BindVertexArray(0);
    }

    private void DrawHudLines(List<float> lines, Vector3 color, float width)
    {
        if (lines.Count == 0 || _shader is null)
            return;

        _shader.Use();
        _shader.SetInt("useLighting", 0);
        _shader.SetMatrix4("model", Matrix4.Identity);
        _shader.SetMatrix4("view", Matrix4.Identity);
        _shader.SetMatrix4("projection", Matrix4.Identity);
        _shader.SetVector3("objectColor", color);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _hudVbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, lines.Count * sizeof(float), lines.ToArray());
        GL.BindVertexArray(_hudVao);
        GL.LineWidth(width);
        GL.DrawArrays(PrimitiveType.Lines, 0, lines.Count / 3);
        GL.BindVertexArray(0);
    }

    private void AddHudRect(List<float> lines, float x, float y, float width, float height)
    {
        AddHudLine(lines, x, y, x + width, y);
        AddHudLine(lines, x + width, y, x + width, y + height);
        AddHudLine(lines, x + width, y + height, x, y + height);
        AddHudLine(lines, x, y + height, x, y);
    }

    private void AddHudLine(List<float> lines, float x1, float y1, float x2, float y2)
    {
        float w = ClientSize.X;
        float h = ClientSize.Y;

        lines.Add(x1 / w * 2f - 1f);
        lines.Add(1f - y1 / h * 2f);
        lines.Add(0f);

        lines.Add(x2 / w * 2f - 1f);
        lines.Add(1f - y2 / h * 2f);
        lines.Add(0f);
    }

    private static float MeasureHudText(string text, float scale)
    {
        if (string.IsNullOrEmpty(text))
            return 0f;

        return (text.Length - 1) * 1.35f * scale + scale;
    }

    private void DrawHudText(List<float> lines, string text, float x, float y, float scale)
    {
        float cursor = x;
        foreach (char ch in text)
        {
            DrawHudGlyph(lines, ch, cursor, y, scale);
            cursor += 1.35f * scale;
        }
    }

    private void DrawHudGlyph(List<float> lines, char ch, float x, float y, float s)
    {
        void L(float x1, float y1, float x2, float y2) =>
            AddHudLine(lines, x + x1 * s, y + y1 * s, x + x2 * s, y + y2 * s);

        void Seg(char segment)
        {
            switch (segment)
            {
                case 'A': L(0.15f, 0f, 0.85f, 0f); break;
                case 'B': L(1f, 0.15f, 1f, 0.85f); break;
                case 'C': L(1f, 1.15f, 1f, 1.85f); break;
                case 'D': L(0.15f, 2f, 0.85f, 2f); break;
                case 'E': L(0f, 1.15f, 0f, 1.85f); break;
                case 'F': L(0f, 0.15f, 0f, 0.85f); break;
                case 'G': L(0.15f, 1f, 0.85f, 1f); break;
            }
        }

        string segments = ch switch
        {
            '0' => "ABCDEF",
            '1' => "BC",
            '2' => "ABDEG",
            '3' => "ABCDG",
            '4' => "BCFG",
            '5' => "ACDFG",
            '6' => "ACDEFG",
            '7' => "ABC",
            '8' => "ABCDEFG",
            '9' => "ABCDFG",
            'S' => "ACDFG",
            _ => ""
        };

        foreach (char segment in segments)
            Seg(segment);

        switch (ch)
        {
            case 'A':
                L(0f, 2f, 0.5f, 0f);
                L(0.5f, 0f, 1f, 2f);
                L(0.22f, 1.05f, 0.78f, 1.05f);
                break;
            case 'C':
                L(1f, 0f, 0f, 0f);
                L(0f, 0f, 0f, 2f);
                L(0f, 2f, 1f, 2f);
                break;
            case 'D':
                L(0f, 0f, 0f, 2f);
                L(0f, 0f, 0.75f, 0f);
                L(0.75f, 0f, 1f, 0.3f);
                L(1f, 0.3f, 1f, 1.7f);
                L(1f, 1.7f, 0.75f, 2f);
                L(0.75f, 2f, 0f, 2f);
                break;
            case 'E':
                L(1f, 0f, 0f, 0f);
                L(0f, 0f, 0f, 2f);
                L(0f, 1f, 0.8f, 1f);
                L(0f, 2f, 1f, 2f);
                break;
            case 'I':
                L(0f, 0f, 1f, 0f);
                L(0.5f, 0f, 0.5f, 2f);
                L(0f, 2f, 1f, 2f);
                break;
            case 'L':
                L(0f, 0f, 0f, 2f);
                L(0f, 2f, 1f, 2f);
                break;
            case 'N':
                L(0f, 2f, 0f, 0f);
                L(0f, 0f, 1f, 2f);
                L(1f, 2f, 1f, 0f);
                break;
            case 'O':
                L(0f, 0f, 1f, 0f);
                L(1f, 0f, 1f, 2f);
                L(1f, 2f, 0f, 2f);
                L(0f, 2f, 0f, 0f);
                break;
            case 'P':
                L(0f, 2f, 0f, 0f);
                L(0f, 0f, 1f, 0f);
                L(1f, 0f, 1f, 1f);
                L(1f, 1f, 0f, 1f);
                break;
            case 'R':
                L(0f, 2f, 0f, 0f);
                L(0f, 0f, 1f, 0f);
                L(1f, 0f, 1f, 1f);
                L(1f, 1f, 0f, 1f);
                L(0.45f, 1f, 1f, 2f);
                break;
            case 'T':
                L(0f, 0f, 1f, 0f);
                L(0.5f, 0f, 0.5f, 2f);
                break;
            case 'Y':
                L(0f, 0f, 0.5f, 0.95f);
                L(1f, 0f, 0.5f, 0.95f);
                L(0.5f, 0.95f, 0.5f, 2f);
                break;
            case 'W':
                L(0f, 0f, 0.20f, 2f);
                L(0.20f, 2f, 0.50f, 1.15f);
                L(0.50f, 1.15f, 0.80f, 2f);
                L(0.80f, 2f, 1f, 0f);
                break;
            case 'K':
                L(0f, 0f, 0f, 2f);
                L(0f, 1.05f, 1f, 0f);
                L(0f, 1.05f, 1f, 2f);
                break;
            case 'V':
                L(0f, 0f, 0.5f, 2f);
                L(1f, 0f, 0.5f, 2f);
                break;
            case ',':
                // Хорошо заметная десятичная запятая. Она специально крупнее
                // обычного знака препинания, чтобы 3,5 нельзя было прочитать как 35.
                L(0.25f, 1.55f, 0.75f, 1.55f);
                L(0.75f, 1.55f, 0.75f, 1.95f);
                L(0.75f, 1.95f, 0.35f, 1.95f);
                L(0.35f, 1.95f, 0.35f, 1.55f);
                L(0.55f, 1.95f, 0.28f, 2.35f);
                break;
            case '.':
                // Оставлено для других чисел HUD, если они появятся позже.
                L(0.25f, 1.55f, 0.75f, 1.55f);
                L(0.75f, 1.55f, 0.75f, 2.00f);
                L(0.75f, 2.00f, 0.25f, 2.00f);
                L(0.25f, 2.00f, 0.25f, 1.55f);
                break;
            case 'X':
                L(0f, 0f, 1f, 2f);
                L(1f, 0f, 0f, 2f);
                break;
            case 'G':
                L(1f, 0f, 0f, 0f);
                L(0f, 0f, 0f, 2f);
                L(0f, 2f, 1f, 2f);
                L(1f, 2f, 1f, 1f);
                L(1f, 1f, 0.55f, 1f);
                break;
            case 'M':
                L(0f, 2f, 0f, 0f);
                L(0f, 0f, 0.5f, 0.85f);
                L(0.5f, 0.85f, 1f, 0f);
                L(1f, 0f, 1f, 2f);
                break;
            case '/':
                L(0f, 2f, 1f, 0f);
                break;
        }
    }
}
