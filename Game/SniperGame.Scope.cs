using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Sniper3D;

internal sealed partial class SniperGame
{
    private void RecalculateZeroing()
    {
        if (TryGetZeroElevationAngle(_zeroDistanceMeters, out float angleDeg))
            _zeroElevationAngleDeg = angleDeg;
        else
            _zeroElevationAngleDeg = 0f;
    }

    /// <summary>
    /// Возвращает реальный угол возвышения ствола для выбранной ZERO-дистанции.
    /// Расчёт выполняется той же G1/G7 + RK4 физикой, что и настоящий выстрел,
    /// но в неподвижном воздухе. Линия визирования проходит через центр камеры,
    /// а ось ствола расположена ниже неё на SightHeightMeters.
    /// </summary>
    private bool TryGetZeroElevationAngle(int distanceMeters, out float angleDeg)
    {
        distanceMeters = Math.Clamp(distanceMeters, MinZeroDistanceMeters, MaxZeroDistanceMeters);

        // ZERO 0 m = механическая нейтраль: никакой автоматической вертикальной поправки.
        // Ствол остаётся параллелен линии визирования, а пуля стартует ниже неё
        // на реальную высоту оптики SightHeightMeters.
        if (distanceMeters == 0)
        {
            angleDeg = 0f;
            return true;
        }

        var key = (_activeWeaponIndex, distanceMeters);

        if (_zeroAngleCache.TryGetValue(key, out angleDeg))
            return float.IsFinite(angleDeg);

        BallisticProfile profile = ActiveBallistics;
        float sightHeight = MathF.Max(0f, ActiveWeapon.SightHeightMeters);

        bool TryError(float testAngleDeg, out float errorY)
        {
            errorY = 0f;
            float angleRad = MathHelper.DegreesToRadians(testAngleDeg);
            Vector3 direction = new(0f, MathF.Sin(angleRad), -MathF.Cos(angleRad));
            Vector3 muzzle = new(0f, -sightHeight, 0f);
            var testBullet = new Bullet(muzzle, direction * profile.MuzzleVelocityMps, profile);

            float time = 0f;
            int maxSteps = (int)MathF.Ceiling(20f / BallisticStepSeconds);
            for (int i = 0; i < maxSteps; i++)
            {
                testBullet.Step(BallisticStepSeconds, _wind, time, 0f, ignoreLifetimeLimits: true);
                time += BallisticStepSeconds;

                float fromDistance = -testBullet.PreviousPosition.Z;
                float toDistance = -testBullet.Position.Z;
                if (toDistance >= distanceMeters && toDistance > fromDistance + 0.000001f)
                {
                    float t = (distanceMeters - fromDistance) / (toDistance - fromDistance);
                    t = MathHelper.Clamp(t, 0f, 1f);
                    errorY = testBullet.PreviousPosition.Y
                        + (testBullet.Position.Y - testBullet.PreviousPosition.Y) * t;
                    return true;
                }

                if (!testBullet.IsAlive)
                    break;
            }

            return false;
        }

        // Нижняя граница заведомо направляет пулю ниже линии визирования.
        float low = -1f;
        if (!TryError(low, out float lowError))
        {
            angleDeg = float.NaN;
            _zeroAngleCache[key] = angleDeg;
            return false;
        }

        // Ищем верхнюю границу, на которой пуля уже проходит выше линии визирования.
        float high = 0.25f;
        float highError = float.NaN;
        bool bracketed = false;
        while (high <= 30f)
        {
            if (TryError(high, out highError) && lowError <= 0f && highError >= 0f)
            {
                bracketed = true;
                break;
            }
            high += 0.5f;
        }

        if (!bracketed)
        {
            angleDeg = float.NaN;
            _zeroAngleCache[key] = angleDeg;
            return false;
        }

        // Бисекция даёт намного более точную пристрелку, чем простой atan(drop/range),
        // потому что изменение угла также немного меняет время полёта и сопротивление.
        for (int iteration = 0; iteration < 18; iteration++)
        {
            float mid = (low + high) * 0.5f;
            if (!TryError(mid, out float midError))
                break;

            if (midError < 0f)
            {
                low = mid;
                lowError = midError;
            }
            else
            {
                high = mid;
                highError = midError;
            }
        }

        angleDeg = (low + high) * 0.5f;
        _zeroAngleCache[key] = angleDeg;
        return true;
    }

    /// <summary>
    /// Пересчитывает BDC-точки как ДОПОЛНИТЕЛЬНУЮ поправку относительно текущего ZERO.
    /// Поэтому при ZERO 1000 m отметка 1000 м сходится с центром перекрестия,
    /// а более близкие отметки могут оказаться выше центра.
    /// </summary>
    private void RecalculateScopeRangeMarks()
    {
        Array.Fill(_scopeRangeMarkAnglesDeg, float.NaN);

        for (int i = 0; i < ScopeRangeMarkMeters.Length; i++)
        {
            int range = (int)ScopeRangeMarkMeters[i];
            if (TryGetZeroElevationAngle(range, out float absoluteAngleDeg))
                _scopeRangeMarkAnglesDeg[i] = absoluteAngleDeg - _zeroElevationAngleDeg;
        }
    }

    /// <summary>
    /// Рисует четыре небольшие BDC-точки относительно текущего ZERO.
    /// После дальней пристрелки ближние отметки могут оказаться выше центра;
    /// положение по-прежнему задаётся реальным углом, а не пикселями.
    /// </summary>
    private void DrawScopeRangeMarks(int viewportWidth, int viewportHeight, float verticalFovDegrees)
    {
        if (_shader is null || _hudVao == 0 || _hudVbo == 0 ||
            viewportWidth <= 0 || viewportHeight <= 0 || verticalFovDegrees <= 0.01f)
            return;

        float halfFovRadians = MathHelper.DegreesToRadians(verticalFovDegrees * 0.5f);
        float tanHalfFov = MathF.Tan(halfFovRadians);
        if (tanHalfFov <= 0.000001f)
            return;

        var points = new List<float>(ScopeRangeMarkMeters.Length * 3);

        for (int i = 0; i < ScopeRangeMarkMeters.Length; i++)
        {
            float angleDeg = _scopeRangeMarkAnglesDeg[i];
            if (!float.IsFinite(angleDeg))
                continue;

            float angleRad = MathHelper.DegreesToRadians(angleDeg);
            float yNdc = -MathF.Tan(angleRad) / tanHalfFov;

            // Не рисуем отметку за пределами видимой оптики. Если конкретная
            // отметка совпала с текущим ZERO, она естественно сливается с центром.
            if (MathF.Abs(yNdc) < 0.012f || yNdc <= -0.94f || yNdc >= 0.94f)
                continue;

            // Каждая дистанционная поправка отображается одной компактной точкой
            // точно на вертикальной оси сетки. Размер одинаковый для всех четырёх
            // отметок, чтобы не перегружать изображение.
            points.Add(0f); points.Add(yNdc); points.Add(0f);
        }

        if (points.Count == 0)
            return;

        GL.Disable(EnableCap.DepthTest);

        _shader.Use();
        _shader.SetInt("useLighting", 0);
        _shader.SetMatrix4("model", Matrix4.Identity);
        _shader.SetMatrix4("view", Matrix4.Identity);
        _shader.SetMatrix4("projection", Matrix4.Identity);
        _shader.SetVector3("objectColor", new Vector3(0.68f, 0.69f, 0.70f));

        float[] vertices = points.ToArray();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _hudVbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vertices.Length * sizeof(float), vertices);
        GL.BindVertexArray(_hudVao);
        GL.PointSize(3f);
        GL.DrawArrays(PrimitiveType.Points, 0, vertices.Length / 3);
        GL.BindVertexArray(0);

        GL.Enable(EnableCap.DepthTest);
    }
}
