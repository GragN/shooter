using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Sniper3D;

internal sealed partial class SniperGame
{
    protected override void OnLoad()
    {
        base.OnLoad();

        GL.ClearColor(0.30f, 0.32f, 0.34f, 1f);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.Multisample);
        GlLineWidth.Initialize();

        _shader = new Shader(ShaderSources.Vertex, ShaderSources.Fragment);
        _cube = Mesh.CreateCube();
        _disc = Mesh.CreateDisc();

        CreateCrosshair();
        CreateHudOverlay();
        RecalculateZeroing();
        RecalculateScopeRangeMarks();
        ValidateWindLimiterInvariant();

        CursorState = CursorState.Grabbed;
        VSync = VSyncMode.On;

        UpdateWindowTitle();
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        if (!_distanceMenuOpen && KeyboardState.IsKeyPressed(Keys.M))
        {
            OpenDistanceMenu();
            return;
        }

        if (!_distanceMenuOpen && KeyboardState.IsKeyPressed(Keys.R))
        {
            RestoreActiveDestructible();
            return;
        }

        if (!_distanceMenuOpen && KeyboardState.IsKeyPressed(Keys.O))
        {
            _stabilizationEnabled = !_stabilizationEnabled;

            if (_stabilizationEnabled)
            {
                // Убираем любое уже начавшееся автоматическое движение камеры:
                // sway обнуляется мгновенно, а target-углы приравниваются к
                // текущим, поэтому остаток предыдущей отдачи не дотягивается
                // сглаживанием после включения режима.
                _aimSwayBlend = 0f;
                _aimSwayYaw = 0f;
                _aimSwayPitch = 0f;
                _targetYaw = _yaw;
                _targetPitch = _pitch;
                _lastShot = "STABILIZATION ON";
            }
            else
            {
                _lastShot = "STABILIZATION OFF";
            }

            UpdateWindowTitle();
        }

        if (_distanceMenuOpen)
        {
            if ((_draggingDistanceSlider || _draggingScopeSlider || _draggingWindSlider) && !MouseState.IsButtonDown(MouseButton.Left))
            {
                _draggingDistanceSlider = false;
                _draggingScopeSlider = false;
                _draggingWindSlider = false;
            }

            if (KeyboardState.IsKeyPressed(Keys.M) || KeyboardState.IsKeyPressed(Keys.Escape))
            {
                CloseDistanceMenu();
                return;
            }

            // Пока меню открыто, прицел, ветер и баллистика стоят на паузе.
            // Выбор дистанции выполняется мышью через ползунок.
            return;
        }

        if (KeyboardState.IsKeyPressed(Keys.Escape))
        {
            Close();
            return;
        }

        if (_activeScene == SceneType.SteelPlate)
        {
            if (KeyboardState.IsKeyPressed(Keys.LeftBracket))
                RotateSteelPlate(-5f);
            if (KeyboardState.IsKeyPressed(Keys.RightBracket))
                RotateSteelPlate(5f);
        }

        if (KeyboardState.IsKeyPressed(Keys.T))
        {
            if (_impactPredictionEnabled)
            {
                // Повторное нажатие T полностью снимает старый захват дистанции.
                _impactPredictionEnabled = false;
                _hasPredictedImpact = false;
                _impactPredictionLockedRangeMeters = 0f;
                _impactPredictionRefreshTimer = 0f;
                _lastShot = "BALLISTIC LOCK OFF";
            }
            else
            {
                bool aimingNow = _aimingEnabled && TrackedBulletCamera is null;
                if (aimingNow && TryAcquireImpactPredictionRange(out float lockedRangeMeters))
                {
                    _impactPredictionEnabled = true;
                    _impactPredictionLockedRangeMeters = lockedRangeMeters;
                    _hasPredictedImpact = false;
                    _impactPredictionRefreshTimer = ImpactPredictionRefreshSeconds;
                    _lastShot = $"BALLISTIC LOCK {lockedRangeMeters:0} M";
                }
                else
                {
                    // Чтобы захватить новую дальность, сначала нужно действительно
                    // прицелиться в существующую поверхность, а затем нажать T.
                    _impactPredictionEnabled = false;
                    _hasPredictedImpact = false;
                    _impactPredictionLockedRangeMeters = 0f;
                    _lastShot = "T: AIM AT TARGET FIRST";
                }
            }

            UpdateWindowTitle();
        }

        if (KeyboardState.IsKeyPressed(Keys.D1) || KeyboardState.IsKeyPressed(Keys.KeyPad1))
        {
            _cameraMode = CameraMode.Shooter;
            _trackedBullet = null;
            UpdateWindowTitle();
        }

        if (KeyboardState.IsKeyPressed(Keys.D2) || KeyboardState.IsKeyPressed(Keys.KeyPad2))
        {
            _cameraMode = CameraMode.Bullet;
            _trackedBullet = _bullets.LastOrDefault(b => b.IsAlive && !b.HasImpacted);
            UpdateWindowTitle();
        }

        if (KeyboardState.IsKeyPressed(Keys.D3) || KeyboardState.IsKeyPressed(Keys.KeyPad3))
        {
            _cameraMode = CameraMode.TargetObserver;
            _trackedBullet = null;
            UpdateWindowTitle();
        }

        float dt = MathF.Min((float)args.Time, 0.05f);
        float lookAlpha = 1f - MathF.Exp(-LookSmoothing * dt);
        float zoomAlpha = 1f - MathF.Exp(-ZoomSmoothing * dt);

        _yaw += (_targetYaw - _yaw) * lookAlpha;
        _pitch += (_targetPitch - _pitch) * lookAlpha;

        bool bulletViewActive = TrackedBulletCamera is not null;
        bool aiming = !bulletViewActive && _aimingEnabled;
        UpdateAimSway(dt, aiming);

        float scopeFov = NormalFov / ScopeMagnifications[_scopeMagnificationIndex];
        float desiredFov = aiming ? scopeFov : NormalFov;
        _currentFov += (desiredFov - _currentFov) * zoomAlpha;

        UpdateImpactPrediction(dt, aiming);
        UpdateAutomaticFire(dt);
        UpdateBullets(dt);
        if (_activeScene == SceneType.RangeCube)
            _target.UpdatePhysics(dt, 0f);
        else if (_activeScene == SceneType.DestructibleHouse)
            _house.UpdatePhysics(dt, 0f);
        else
            _steel.UpdatePhysics(dt);

        _titleRefreshTimer += dt;
        if (_titleRefreshTimer >= 0.1f)
        {
            _titleRefreshTimer = 0f;
            UpdateWindowTitle();
        }
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        base.OnMouseMove(e);

        if (!IsFocused)
            return;

        if (_distanceMenuOpen)
        {
            if (_draggingDistanceSlider)
                UpdateMenuDistanceFromMouse(e.X);
            else if (_draggingScopeSlider)
                UpdateMenuScopeFromMouse(e.X);
            else if (_draggingWindSlider)
                UpdateMenuWindFromMouse(e.X);
            return;
        }

        if (_ignoreNextMouseMove)
        {
            _ignoreNextMouseMove = false;
            return;
        }

        if (TrackedBulletCamera is not null)
            return;

        float fovScale = _currentFov / NormalFov;
        float sensitivity = BaseMouseSensitivity * fovScale;

        _targetYaw += e.DeltaX * sensitivity;
        _targetPitch -= e.DeltaY * sensitivity;
        _targetPitch = MathHelper.Clamp(_targetPitch, -75f, 75f);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        // В меню и камере пули колесо не вмешивается в пристрелку.
        // Один щелчок колеса = 100 м. ZERO можно менять как в оптике, так и
        // в обычном режиме, а результат сразу виден после входа в прицел.
        if (_distanceMenuOpen || TrackedBulletCamera is not null)
            return;

        float wheel = e.OffsetY;
        if (MathF.Abs(wheel) < 0.001f)
            return;

        int notchCount = Math.Max(1, (int)MathF.Round(MathF.Abs(wheel)));
        int direction = wheel > 0f ? 1 : -1;
        int nextZero = Math.Clamp(
            _zeroDistanceMeters + direction * notchCount * ZeroDistanceStepMeters,
            MinZeroDistanceMeters,
            MaxZeroDistanceMeters);

        if (nextZero == _zeroDistanceMeters)
            return;

        _zeroDistanceMeters = nextZero;
        RecalculateZeroing();
        RecalculateScopeRangeMarks();

        // T сохраняет захваченную дальность, но прогноз немедленно пересчитается
        // уже с новым углом пристрелки.
        _impactPredictionRefreshTimer = ImpactPredictionRefreshSeconds;
        _lastShot = $"ZERO {_zeroDistanceMeters} M";
        UpdateWindowTitle();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        if (_distanceMenuOpen)
        {
            if (e.Button == MouseButton.Left)
                HandleDistanceMenuMouseDown(MouseState.Position);
            return;
        }

        if (e.Button == MouseButton.Right && TrackedBulletCamera is null)
        {
            _aimingEnabled = !_aimingEnabled;
            _lastShot = _aimingEnabled ? "SCOPE ON" : "SCOPE OFF";
            UpdateWindowTitle();
            return;
        }

        if (e.Button == MouseButton.Left && !ActiveWeapon.IsAutomatic && TrackedBulletCamera is null)
            Fire();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (_distanceMenuOpen && e.Button == MouseButton.Left)
        {
            _draggingDistanceSlider = false;
            _draggingScopeSlider = false;
            _draggingWindSlider = false;
        }
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        if (_shader is null || _cube is null || _disc is null || ClientSize.X <= 0 || ClientSize.Y <= 0)
            return;

        // Каждый кадр начинаем с полного viewport. Режим 3 позже создаёт
        // второй независимый viewport для картинки-в-картинке.
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
        GL.ClearColor(0.30f, 0.32f, 0.34f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        Bullet? cameraBullet = !_distanceMenuOpen ? TrackedBulletCamera : null;
        bool bulletViewActive = cameraBullet is not null;
        bool targetObserverActive = !_distanceMenuOpen && _cameraMode == CameraMode.TargetObserver;

        Vector3 renderCameraPosition = _cameraPosition;
        Vector3 renderCameraFront = AimFront;
        Vector3 renderCameraUp = Vector3.UnitY;
        float renderFov = _currentFov;

        if (cameraBullet is not null)
        {
            Vector3 velocity = cameraBullet.Velocity;
            if (velocity.LengthSquared > 0.000001f)
                renderCameraFront = Vector3.Normalize(velocity);

            renderCameraPosition = cameraBullet.Position;
            renderFov = NormalFov;

            if (MathF.Abs(Vector3.Dot(renderCameraFront, Vector3.UnitY)) > 0.98f)
                renderCameraUp = Vector3.UnitZ;
        }
        else if (_cameraMode == CameraMode.TargetObserver)
        {
            GetTargetObserverCamera(out renderCameraPosition, out renderCameraFront, out renderCameraUp);
            renderFov = TargetObserverFov;
        }

        Matrix4 view = Matrix4.LookAt(
            renderCameraPosition,
            renderCameraPosition + renderCameraFront,
            renderCameraUp);

        Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(renderFov),
            ClientSize.X / (float)ClientSize.Y,
            0.05f,
            2300f);

        RenderWorld(view, projection, cameraBullet);

        if (_distanceMenuOpen)
        {
            DrawDistanceMenu();
        }
        else if (targetObserverActive)
        {
            // В режиме 3 основной экран занят внешней камерой куба.
            // HUD ветра остаётся глобальным, а обычная камера стрелка со
            // своим перекрестием выводится отдельным окном снизу слева.
            DrawWindHud();
            DrawShooterPictureInPicture();
        }
        else if (!bulletViewActive)
        {
            DrawCrosshair();

            bool aiming = _aimingEnabled;
            if (aiming)
                DrawScopeRangeMarks(ClientSize.X, ClientSize.Y, renderFov);

            DrawWindHud();

            if (_impactPredictionEnabled && aiming && _hasPredictedImpact)
            {
                DrawImpactPredictionMarker(
                    _predictedImpactPoint,
                    renderCameraPosition,
                    renderCameraFront,
                    renderCameraUp,
                    renderFov);
            }
        }

        if (!_distanceMenuOpen)
        {
            DrawSteelHud();
            DrawConcreteHud();
        }

        // Любой PIP-pass обязан вернуть полный viewport перед SwapBuffers.
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
        SwapBuffers();
    }

    private void RenderWorld(Matrix4 view, Matrix4 projection, Bullet? cameraBullet)
    {
        if (_shader is null)
            return;

        _shader.Use();
        _shader.SetMatrix4("view", view);
        _shader.SetMatrix4("projection", projection);
        _shader.SetVector3("cameraPosition", view.Inverted().ExtractTranslation());

        DrawRange();
        DrawTarget();
        DrawBullets(view, projection, cameraBullet);
    }

    private void GetTargetObserverCamera(
        out Vector3 cameraPosition,
        out Vector3 cameraFront,
        out Vector3 cameraUp)
    {
        Vector3 targetPosition = ActiveSceneFocus;

        // 10 м от активного разрушаемого объекта в сторону стрелка.
        Vector3 targetToShooter = _cameraPosition - targetPosition;
        targetToShooter.Y = 0f;
        if (targetToShooter.LengthSquared < 0.000001f)
            targetToShooter = Vector3.UnitZ;
        else
            targetToShooter = Vector3.Normalize(targetToShooter);

        // "Влево относительно стрелка": берём направление от стрелка к объекту
        // и его левый вектор в горизонтальной плоскости.
        Vector3 shooterToTarget = -targetToShooter;
        Vector3 leftFromShooter = Vector3.Cross(Vector3.UnitY, shooterToTarget);
        if (leftFromShooter.LengthSquared < 0.000001f)
            leftFromShooter = -Vector3.UnitX;
        else
            leftFromShooter = Vector3.Normalize(leftFromShooter);

        // A close inspection view makes millimetre-scale steel damage visible.
        float observerDistance = _activeScene switch { SceneType.SteelPlate => 1.8f, SceneType.RangeCube => 2.3f, _ => 10f };
        float observerOffset = _activeScene switch { SceneType.SteelPlate => 0.65f, SceneType.RangeCube => 1f, _ => 5f };
        cameraPosition = targetPosition
            + targetToShooter * observerDistance
            + leftFromShooter * observerOffset;

        cameraFront = targetPosition - cameraPosition;
        if (cameraFront.LengthSquared < 0.000001f)
            cameraFront = -Vector3.UnitZ;
        else
            cameraFront = Vector3.Normalize(cameraFront);

        cameraUp = Vector3.UnitY;
    }

    private void DrawShooterPictureInPicture()
    {
        if (_shader is null || ClientSize.X <= 0 || ClientSize.Y <= 0)
            return;

        const int margin = 18;
        const int border = 4;

        int pipWidth = Math.Clamp((int)(ClientSize.X * 0.32f), 280, 480);
        int pipHeight = Math.Max(158, (int)MathF.Round(pipWidth * 9f / 16f));

        // На очень маленьком окне не позволяем PIP занять почти весь экран.
        int maxHeight = Math.Max(120, (int)(ClientSize.Y * 0.42f));
        if (pipHeight > maxHeight)
        {
            pipHeight = maxHeight;
            pipWidth = Math.Max(210, (int)MathF.Round(pipHeight * 16f / 9f));
        }

        int pipX = margin;
        int pipY = margin; // OpenGL viewport отсчитывается от нижнего края.

        GL.Enable(EnableCap.ScissorTest);

        // Тонкая тёмная рамка отделяет вторую камеру от основной картинки.
        GL.Scissor(
            Math.Max(0, pipX - border),
            Math.Max(0, pipY - border),
            Math.Min(ClientSize.X, pipWidth + border * 2),
            Math.Min(ClientSize.Y, pipHeight + border * 2));
        GL.ClearColor(0.045f, 0.050f, 0.055f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        GL.Scissor(pipX, pipY, pipWidth, pipHeight);
        GL.Viewport(pipX, pipY, pipWidth, pipHeight);
        GL.ClearColor(0.30f, 0.32f, 0.34f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        Vector3 shooterFront = AimFront;
        Vector3 shooterUp = Vector3.UnitY;
        Matrix4 shooterView = Matrix4.LookAt(
            _cameraPosition,
            _cameraPosition + shooterFront,
            shooterUp);
        Matrix4 shooterProjection = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(_currentFov),
            pipWidth / (float)pipHeight,
            0.05f,
            2300f);

        RenderWorld(shooterView, shooterProjection, null);

        // Перекрестие должно иметь тот же размер в пикселях, что и в обычном
        // режиме, поэтому временно пересчитываем его под размеры PIP.
        UpdateCrosshairBuffer(pipWidth, pipHeight);
        DrawCrosshair();

        bool aiming = _aimingEnabled;
        if (aiming)
            DrawScopeRangeMarks(pipWidth, pipHeight, _currentFov);

        if (_impactPredictionEnabled && aiming && _hasPredictedImpact)
        {
            DrawImpactPredictionMarkerForViewport(
                _predictedImpactPoint,
                _cameraPosition,
                shooterFront,
                shooterUp,
                _currentFov,
                pipWidth,
                pipHeight);
        }

        GL.Disable(EnableCap.ScissorTest);
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
        GL.ClearColor(0.30f, 0.32f, 0.34f, 1f);

        // Возвращаем стандартный буфер перекрестия для камер 1/2.
        UpdateCrosshairBuffer();
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
        UpdateCrosshairBuffer();
    }

    protected override void OnUnload()
    {
        base.OnUnload();

        _disc?.Dispose();
        _steelMesh?.Dispose();
        _concreteMesh?.Dispose();
        foreach (Mesh mesh in _concreteFragmentMeshes.Values) mesh.Dispose();
        _cube?.Dispose();
        _shader?.Dispose();

        if (_crosshairVao != 0)
            GL.DeleteVertexArray(_crosshairVao);
        if (_crosshairVbo != 0)
            GL.DeleteBuffer(_crosshairVbo);
        if (_hudVao != 0)
            GL.DeleteVertexArray(_hudVao);
        if (_hudVbo != 0)
            GL.DeleteBuffer(_hudVbo);
    }
}
