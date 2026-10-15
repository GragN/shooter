using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Sniper3D;

internal sealed partial class SniperGame
{
    private void DrawRange()
    {
        if (_cube is null)
            return;

        if (_activeScene == SceneType.RangeCube)
            DrawCubeRange();
        else if (_activeScene == SceneType.DestructibleHouse)
            DrawHouseRange();
        else
            DrawSteelRange();
    }

    private void DrawCubeRange()
    {
        float range = _target.DistanceMeters;

        // Открытый полигон: за кубом больше нет бетонной стены/пулеулавливателя.
        // Площадка продолжается ещё на 120 м за кубом, чтобы внешний ракурс и
        // пролетающие насквозь пули не упирались в искусственную стену.
        float floorStartZ = _cameraPosition.Z + 20f;
        float floorEndZ = _target.Position.Z - 120f;
        float floorLength = floorStartZ - floorEndZ;
        float floorCenterZ = (floorStartZ + floorEndZ) * 0.5f;

        DrawCube(
            new Vector3(0f, -0.15f, floorCenterZ),
            new Vector3(50f, 0.3f, floorLength),
            new Vector3(0.27f, 0.28f, 0.29f));

        DrawRangeMarkers(range);
    }

    private void DrawHouseRange()
    {
        float range = _house.DistanceMeters;
        float houseBackZ = _house.Position.Z - _house.DepthMeters * 0.5f;

        float floorStartZ = _cameraPosition.Z + 20f;
        float floorEndZ = houseBackZ - 50f;
        float floorLength = floorStartZ - floorEndZ;
        float floorCenterZ = (floorStartZ + floorEndZ) * 0.5f;

        // Открытая бетонная площадка без пулеулавливателя: после пробития дома
        // пуля может лететь дальше и в итоге уйти в землю.
        DrawCube(
            new Vector3(0f, -0.15f, floorCenterZ),
            new Vector3(60f, 0.3f, floorLength),
            new Vector3(0.255f, 0.265f, 0.275f));

        // Небольшая фундаментная плита под домом. Она декоративная и неразрушаемая.
        DrawCube(
            new Vector3(_house.Position.X, -0.10f, _house.Position.Z),
            new Vector3(_house.WidthMeters + 1.2f, 0.20f, _house.DepthMeters + 1.2f),
            new Vector3(0.31f, 0.32f, 0.33f));

        DrawRangeMarkers(range);
    }

    private void DrawRangeMarkers(float range)
    {
        int markerCount = Math.Max(0, (int)(range / 100f) - 1);
        for (int i = 1; i <= markerCount; i++)
        {
            float z = _cameraPosition.Z - i * 100f;
            DrawCube(new Vector3(-12f, 0.4f, z), new Vector3(0.35f, 0.8f, 5f), new Vector3(0.48f, 0.49f, 0.50f));
            DrawCube(new Vector3(12f, 0.4f, z), new Vector3(0.35f, 0.8f, 5f), new Vector3(0.48f, 0.49f, 0.50f));
        }
    }

    private void DrawTarget()
    {
        if (_cube is null)
            return;

        if (_activeScene == SceneType.RangeCube)
            DrawConcreteCubeTarget();
        else if (_activeScene == SceneType.DestructibleHouse)
            DrawDestructibleHouse();
        else
            DrawSteelPlate();
    }

    private void DrawConcreteCubeTarget()
    {
        if (_shader is null) return;
        if (_concreteMeshRevision != _target.Revision)
        {
            var geometry = ConcreteMeshGeometry.Build(_target);
            Mesh replacement = new(geometry.Vertices, geometry.Indices, vertexColors: true);
            _concreteMesh?.Dispose();
            _concreteMesh = replacement;
            _concreteMeshRevision = _target.Revision;
        }
        _shader.Use();
        _shader.SetInt("useLighting", 3);
        _shader.SetMatrix4("model", Matrix4.CreateTranslation(_target.Position));
        _concreteMesh?.Draw();
        foreach (ConcreteFragment fragment in _target.Fragments)
        {
            if (fragment.LocalVoxelCenters is null)
                DrawCubeRotated(fragment.Position, fragment.SizeMeters, fragment.Color, fragment.RotationRadians);
            else
            {
                if (!_concreteFragmentMeshes.TryGetValue(fragment, out Mesh? mesh))
                {
                    var data = ConcreteMeshGeometry.BuildFragment(fragment);
                    mesh = new(data.Vertices, data.Indices, vertexColors: true);
                    _concreteFragmentMeshes.Add(fragment, mesh);
                }
                Matrix4 rotation = Matrix4.CreateRotationX(fragment.RotationRadians.X)
                    * Matrix4.CreateRotationY(fragment.RotationRadians.Y) * Matrix4.CreateRotationZ(fragment.RotationRadians.Z);
                _shader.Use();
                _shader.SetInt("useLighting", 3);
                _shader.SetMatrix4("model", rotation * Matrix4.CreateTranslation(fragment.Position));
                mesh.Draw();
            }
        }
        foreach (ConcreteFragment old in _concreteFragmentMeshes.Keys.Where(f => !_target.Fragments.Contains(f)).ToArray())
        {
            _concreteFragmentMeshes[old].Dispose();
            _concreteFragmentMeshes.Remove(old);
        }
        float cubeBottom = _target.Position.Y - _target.HalfSizeMeters;
        if (cubeBottom > 0.05f)
        {
            DrawCube(new(_target.Position.X, cubeBottom - 0.06f, _target.Position.Z), new(1.1f, 0.12f, 1.1f), new(0.27f));
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                DrawCube(new(_target.Position.X+x*0.4f, (cubeBottom-0.12f)*0.5f, _target.Position.Z+z*0.4f),
                    new(0.12f, cubeBottom-0.12f, 0.12f), new(0.25f));
        }
    }

    private void DrawDestructibleHouse()
    {
        float renderedCell = _house.CellSizeMeters * 0.985f;

        for (int x = 0; x < _house.GridX; x++)
        for (int y = 0; y < _house.GridY; y++)
        for (int z = 0; z < _house.GridZ; z++)
        {
            if (!_house.IsCellExposed(x, y, z))
                continue;

            Vector3 position = _house.GetCellWorldPosition(x, y, z);
            int shadeIndex = (x * 11 + y * 5 + z * 17) % 7;
            float damage = _house.GetCellDamage01(x, y, z);
            float shade = (0.355f + shadeIndex * 0.009f) * (1f - 0.30f * damage);
            Vector3 concrete = new(shade, shade + 0.007f, shade + 0.013f);
            DrawCube(position, new Vector3(renderedCell), concrete);
        }

        foreach (ConcreteFragment fragment in _house.Fragments)
        {
            DrawCubeRotated(
                fragment.Position,
                fragment.SizeMeters,
                fragment.Color,
                fragment.RotationRadians);
        }
    }

    private void DrawBullets(Matrix4 view, Matrix4 projection, Bullet? cameraBullet)
    {
        if (_shader is null)
            return;

        // Видимой остаётся только сама трассирующая пуля.
        // Длинный луч/след за ней больше не рисуется.
        Vector3 bulletColor = new(1.00f, 0.92f, 0.62f);

        foreach (Bullet bullet in _bullets)
        {
            if (ReferenceEquals(bullet, cameraBullet))
                continue;

            if (!bullet.HasImpacted)
            {
                // Визуальный размер теперь зависит от реального калибра.
                // Для читаемости трассера он слегка увеличен относительно пули,
                // но .50 BMG всё равно визуально крупнее 7.62 и 9 мм.
                float visualDiameter = MathHelper.Clamp(
                    bullet.Profile.DiameterMeters * 1.8f,
                    0.012f,
                    0.026f);
                DrawCube(
                    bullet.Position,
                    new Vector3(visualDiameter, visualDiameter, visualDiameter),
                    bulletColor);
            }
        }
    }

    private void DrawCube(Vector3 position, Vector3 scale, Vector3 color)
    {
        if (_shader is null || _cube is null)
            return;

        Matrix4 model = Matrix4.CreateScale(scale) * Matrix4.CreateTranslation(position);
        _shader.Use();
        _shader.SetInt("useLighting", 1);
        _shader.SetMatrix4("model", model);
        _shader.SetVector3("objectColor", color);
        _cube.Draw();
    }

    private void DrawCubeRotated(Vector3 position, Vector3 scale, Vector3 color, Vector3 rotationRadians)
    {
        if (_shader is null || _cube is null)
            return;

        Matrix4 rotation = Matrix4.CreateRotationX(rotationRadians.X)
            * Matrix4.CreateRotationY(rotationRadians.Y)
            * Matrix4.CreateRotationZ(rotationRadians.Z);

        Matrix4 model = Matrix4.CreateScale(scale)
            * rotation
            * Matrix4.CreateTranslation(position);

        _shader.Use();
        _shader.SetInt("useLighting", 1);
        _shader.SetMatrix4("model", model);
        _shader.SetVector3("objectColor", color);
        _cube.Draw();
    }

    private void DrawDisc(Vector3 position, float radius, Vector3 color)
    {
        if (_shader is null || _disc is null)
            return;

        Matrix4 model = Matrix4.CreateScale(radius, radius, 1f) * Matrix4.CreateTranslation(position);
        _shader.Use();
        _shader.SetInt("useLighting", 1);
        _shader.SetMatrix4("model", model);
        _shader.SetVector3("objectColor", color);
        _disc.Draw();
    }

    private void DrawImpactPredictionMarker(
        Vector3 worldPoint,
        Vector3 cameraPosition,
        Vector3 cameraFront,
        Vector3 cameraUp,
        float verticalFovDegrees)
    {
        if (_shader is null || _hudVao == 0 || _hudVbo == 0 || ClientSize.X <= 0 || ClientSize.Y <= 0)
            return;

        Vector3 front = cameraFront;
        if (front.LengthSquared < 0.000001f)
            return;
        front = Vector3.Normalize(front);

        Vector3 right = Vector3.Cross(front, cameraUp);
        if (right.LengthSquared < 0.000001f)
            return;
        right = Vector3.Normalize(right);

        Vector3 up = Vector3.Normalize(Vector3.Cross(right, front));
        Vector3 toPoint = worldPoint - cameraPosition;
        float depth = Vector3.Dot(toPoint, front);
        if (depth <= 0.01f)
            return;

        float aspect = ClientSize.X / (float)ClientSize.Y;
        float tanHalfFov = MathF.Tan(MathHelper.DegreesToRadians(verticalFovDegrees) * 0.5f);
        if (tanHalfFov <= 0.000001f)
            return;

        float ndcX = Vector3.Dot(toPoint, right) / (depth * tanHalfFov * aspect);
        float ndcY = Vector3.Dot(toPoint, up) / (depth * tanHalfFov);

        // Если точка находится вне изображения, ничего не рисуем.
        if (ndcX < -1.05f || ndcX > 1.05f || ndcY < -1.05f || ndcY > 1.05f)
            return;

        float screenX = (ndcX * 0.5f + 0.5f) * ClientSize.X;
        float screenY = (0.5f - ndcY * 0.5f) * ClientSize.Y;

        DrawHudDot(screenX, screenY, ImpactDotRadiusPixels, new Vector3(0.92f, 0.08f, 0.06f));
    }

    private void DrawImpactPredictionMarkerForViewport(
        Vector3 worldPoint,
        Vector3 cameraPosition,
        Vector3 cameraFront,
        Vector3 cameraUp,
        float verticalFovDegrees,
        int viewportWidth,
        int viewportHeight)
    {
        if (_shader is null || _hudVao == 0 || _hudVbo == 0 || viewportWidth <= 0 || viewportHeight <= 0)
            return;

        Vector3 front = cameraFront;
        if (front.LengthSquared < 0.000001f)
            return;
        front = Vector3.Normalize(front);

        Vector3 right = Vector3.Cross(front, cameraUp);
        if (right.LengthSquared < 0.000001f)
            return;
        right = Vector3.Normalize(right);

        Vector3 up = Vector3.Normalize(Vector3.Cross(right, front));
        Vector3 toPoint = worldPoint - cameraPosition;
        float depth = Vector3.Dot(toPoint, front);
        if (depth <= 0.01f)
            return;

        float aspect = viewportWidth / (float)viewportHeight;
        float tanHalfFov = MathF.Tan(MathHelper.DegreesToRadians(verticalFovDegrees) * 0.5f);
        if (tanHalfFov <= 0.000001f)
            return;

        float ndcX = Vector3.Dot(toPoint, right) / (depth * tanHalfFov * aspect);
        float ndcY = Vector3.Dot(toPoint, up) / (depth * tanHalfFov);

        if (ndcX < -1.05f || ndcX > 1.05f || ndcY < -1.05f || ndcY > 1.05f)
            return;

        DrawNdcDot(ndcX, ndcY, ImpactDotRadiusPixels, viewportWidth, viewportHeight, new Vector3(0.92f, 0.08f, 0.06f));
    }

    private void DrawNdcDot(
        float centerNdcX,
        float centerNdcY,
        float radiusPixels,
        int viewportWidth,
        int viewportHeight,
        Vector3 color)
    {
        const int segments = 16;
        if (viewportWidth <= 0 || viewportHeight <= 0)
            return;

        float radiusX = radiusPixels / viewportWidth * 2f;
        float radiusY = radiusPixels / viewportHeight * 2f;
        var vertices = new float[segments * 3 * 3];
        int cursor = 0;

        void AddVertex(float x, float y)
        {
            vertices[cursor++] = x;
            vertices[cursor++] = y;
            vertices[cursor++] = 0f;
        }

        for (int i = 0; i < segments; i++)
        {
            float a0 = MathF.Tau * i / segments;
            float a1 = MathF.Tau * (i + 1) / segments;

            AddVertex(centerNdcX, centerNdcY);
            AddVertex(centerNdcX + MathF.Cos(a0) * radiusX, centerNdcY + MathF.Sin(a0) * radiusY);
            AddVertex(centerNdcX + MathF.Cos(a1) * radiusX, centerNdcY + MathF.Sin(a1) * radiusY);
        }

        GL.Disable(EnableCap.DepthTest);

        _shader!.Use();
        _shader.SetInt("useLighting", 0);
        _shader.SetMatrix4("model", Matrix4.Identity);
        _shader.SetMatrix4("view", Matrix4.Identity);
        _shader.SetMatrix4("projection", Matrix4.Identity);
        _shader.SetVector3("objectColor", color);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _hudVbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vertices.Length * sizeof(float), vertices);
        GL.BindVertexArray(_hudVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, segments * 3);
        GL.BindVertexArray(0);

        GL.Enable(EnableCap.DepthTest);
    }

    private void DrawHudDot(float centerX, float centerY, float radius, Vector3 color)
    {
        const int segments = 16;
        float w = ClientSize.X;
        float h = ClientSize.Y;
        if (w <= 0f || h <= 0f)
            return;

        var vertices = new float[segments * 3 * 3];
        int cursor = 0;

        void AddVertex(float px, float py)
        {
            vertices[cursor++] = px / w * 2f - 1f;
            vertices[cursor++] = 1f - py / h * 2f;
            vertices[cursor++] = 0f;
        }

        for (int i = 0; i < segments; i++)
        {
            float a0 = MathF.Tau * i / segments;
            float a1 = MathF.Tau * (i + 1) / segments;

            AddVertex(centerX, centerY);
            AddVertex(centerX + MathF.Cos(a0) * radius, centerY + MathF.Sin(a0) * radius);
            AddVertex(centerX + MathF.Cos(a1) * radius, centerY + MathF.Sin(a1) * radius);
        }

        GL.Disable(EnableCap.DepthTest);

        _shader!.Use();
        _shader.SetInt("useLighting", 0);
        _shader.SetMatrix4("model", Matrix4.Identity);
        _shader.SetMatrix4("view", Matrix4.Identity);
        _shader.SetMatrix4("projection", Matrix4.Identity);
        _shader.SetVector3("objectColor", color);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _hudVbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vertices.Length * sizeof(float), vertices);
        GL.BindVertexArray(_hudVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, segments * 3);
        GL.BindVertexArray(0);

        GL.Enable(EnableCap.DepthTest);
    }

    private void CreateCrosshair()
    {
        _crosshairVao = GL.GenVertexArray();
        _crosshairVbo = GL.GenBuffer();

        GL.BindVertexArray(_crosshairVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _crosshairVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, 12 * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.BindVertexArray(0);

        UpdateCrosshairBuffer();
    }

    private void UpdateCrosshairBuffer()
    {
        UpdateCrosshairBuffer(ClientSize.X, ClientSize.Y);
    }

    private void UpdateCrosshairBuffer(int viewportWidth, int viewportHeight)
    {
        if (_crosshairVbo == 0 || viewportWidth <= 0 || viewportHeight <= 0)
            return;

        float halfX = 9f / viewportWidth * 2f;
        float halfY = 9f / viewportHeight * 2f;

        float[] vertices =
        {
            -halfX, 0f, 0f,
             halfX, 0f, 0f,
             0f, -halfY, 0f,
             0f,  halfY, 0f
        };

        GL.BindBuffer(BufferTarget.ArrayBuffer, _crosshairVbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vertices.Length * sizeof(float), vertices);
    }

    private void DrawCrosshair()
    {
        if (_shader is null || _crosshairVao == 0)
            return;

        GL.Disable(EnableCap.DepthTest);

        _shader.Use();
        _shader.SetInt("useLighting", 0);
        _shader.SetMatrix4("model", Matrix4.Identity);
        _shader.SetMatrix4("view", Matrix4.Identity);
        _shader.SetMatrix4("projection", Matrix4.Identity);
        _shader.SetVector3("objectColor", new Vector3(0.82f, 0.83f, 0.84f));

        GL.BindVertexArray(_crosshairVao);
        GlLineWidth.Set(2f);
        GL.DrawArrays(PrimitiveType.Lines, 0, 4);
        GL.BindVertexArray(0);

        GL.Enable(EnableCap.DepthTest);
    }
}
