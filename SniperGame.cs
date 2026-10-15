using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Sniper3D;

internal sealed class SniperGame : GameWindow
{
    private enum CameraMode
    {
        Shooter,
        Bullet,
        TargetObserver
    }

    private enum SceneType
    {
        RangeCube,
        DestructibleHouse
    }

    private const float NormalFov = 65f;
    private static readonly float[] ScopeMagnifications = { 2f, 4f, 8f, 16f, 32f };
    private static readonly float[] ScopeRangeMarkMeters = { 250f, 500f, 750f, 1000f };
    private const float BaseMouseSensitivity = 0.085f;
    private const float LookSmoothing = 28f;
    private const float ZoomSmoothing = 18f;
    private const float TargetObserverFov = 55f;

    // Лёгкое естественное покачивание оружия при работе через оптику.
    // Значения заданы в угловых градусах: на большой кратности движение
    // заметно визуально, но остаётся небольшим по реальному углу ствола.
    private const float AimSwayYawDegrees = 0.033f;
    private const float AimSwayPitchDegrees = 0.045f;
    private const float AimSwayBlendResponse = 3.2f;
    private const int MinTargetDistance = 100;
    private const int MaxTargetDistance = 2000;
    private const int TargetDistanceStep = 100;
    private const int MinWindLimitMps = 0;
    private const int MaxWindLimitMps = 50;
    private const int WindLimitStepMps = 1;

    // Пристрелка оптики. Колесо мыши меняет ZERO дискретно по 100 м.
    private const int MinZeroDistanceMeters = 0;
    private const int MaxZeroDistanceMeters = 2000;
    private const int ZeroDistanceStepMeters = 100;

    // Баллистика считается независимо от FPS с шагом 1 мс.
    private const float BallisticStepSeconds = 0.001f;
    private const float ImpactPredictionRefreshSeconds = 0.025f;
    private const float ImpactDotRadiusPixels = 2.4f;

    private readonly Vector3 _cameraPosition = new(0f, 1.7f, 5f);
    private readonly Random _random = new();
    private readonly Target _target = new();
    private readonly HouseTarget _house = new();
    private readonly WeaponProfile[] _weapons =
    {
        WeaponProfile.Svd(),
        WeaponProfile.MakarovPm(),
        WeaponProfile.Rpk(),
        WeaponProfile.BarrettM82A1()
    };
    private readonly WindField _wind = new();
    private readonly List<Bullet> _bullets = new();

    private CameraMode _cameraMode = CameraMode.Shooter;
    private SceneType _activeScene = SceneType.RangeCube;
    private SceneType _menuScene = SceneType.RangeCube;
    private Bullet? _trackedBullet;

    private bool _impactPredictionEnabled;
    private bool _hasPredictedImpact;
    private Vector3 _predictedImpactPoint;
    private float _impactPredictionRefreshTimer;
    // Дистанция захватывается только в момент включения T. Пока T включён,
    // она не переопределяется ни по кубу, ни по земле, ни по стене.
    // Ветер при этом продолжает пересчитываться постоянно.
    private float _impactPredictionLockedRangeMeters;

    private Shader? _shader;
    private Mesh? _cube;
    private Mesh? _disc;

    private int _crosshairVao;
    private int _crosshairVbo;
    private int _hudVao;
    private int _hudVbo;

    private float _yaw = -90f;
    private float _pitch;
    private float _targetYaw = -90f;
    private float _targetPitch;
    private float _currentFov = NormalFov;
    private float _ballisticAccumulator;
    private float _titleRefreshTimer;
    private float _automaticFireCooldown;
    private float _aimSwayTime;
    private float _aimSwayBlend;
    private float _aimSwayYaw;
    private float _aimSwayPitch;
    private readonly float[] _scopeRangeMarkAnglesDeg = new float[ScopeRangeMarkMeters.Length];

    // ZERO — дистанция, на которой траектория без ветра пересекает линию визирования.
    // ZERO 0 m означает отсутствие вертикальной поправки: ось ствола параллельна линии визирования.
    // Для 100–2000 м угол ствола рассчитывается реальной RK4-баллистикой и кэшируется по оружию/дистанции.
    private int _zeroDistanceMeters = 0;
    private float _zeroElevationAngleDeg;
    private readonly Dictionary<(int WeaponIndex, int DistanceMeters), float> _zeroAngleCache = new();

    // ПКМ работает как переключатель: один клик включает оптику,
    // следующий клик возвращает обычный обзор.
    private bool _aimingEnabled;

    // O: полная стабилизация оружия. Когда включена, нет ни отдачи,
    // ни физиологического покачивания прицела.
    private bool _stabilizationEnabled;

    private bool _distanceMenuOpen;
    private bool _ignoreNextMouseMove;
    private bool _draggingDistanceSlider;
    private bool _draggingScopeSlider;
    private bool _draggingWindSlider;
    private int _menuDistanceMeters = 1000;
    private int _scopeMagnificationIndex = 4;
    private int _menuScopeMagnificationIndex = 4;
    private int _activeWindLimitMps = 10;
    private int _menuWindLimitMps = 10;
    private int _activeWeaponIndex;
    private int _menuWeaponIndex;

    private int _shots;
    private int _hits;
    private int _score;
    private string _lastShot = "Ready";
    private float _lastImpactSpeed;
    private float _lastFlightTime;
    private float _lastImpactEnergy;
    private float _lastMuzzleVelocity;

    public SniperGame(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings)
        : base(gameWindowSettings, nativeWindowSettings)
    {
    }

    private WeaponProfile ActiveWeapon => _weapons[_activeWeaponIndex];
    private BallisticProfile ActiveBallistics => ActiveWeapon.Ballistics;
    private float ActiveSceneDistanceMeters => _activeScene == SceneType.RangeCube ? _target.DistanceMeters : _house.DistanceMeters;
    private Vector3 ActiveSceneFocus => _activeScene == SceneType.RangeCube ? _target.Position : _house.ObserverFocus;
    private float ActiveSceneIntegrity01 => _activeScene == SceneType.RangeCube ? _target.Integrity01 : _house.Integrity01;
    private int ActiveSceneDebrisCount => _activeScene == SceneType.RangeCube ? _target.Fragments.Count : _house.Fragments.Count;
    private string ActiveSceneHudName => _activeScene == SceneType.RangeCube ? "RANGE+CUBE" : "HOUSE";

    private Bullet? TrackedBulletCamera
    {
        get
        {
            if (_cameraMode != CameraMode.Bullet || _trackedBullet is null)
                return null;

            return _trackedBullet.IsAlive && !_trackedBullet.HasImpacted
                ? _trackedBullet
                : null;
        }
    }

    private static Vector3 DirectionFromAngles(float yawDegrees, float pitchDegrees)
    {
        float yaw = MathHelper.DegreesToRadians(yawDegrees);
        float pitch = MathHelper.DegreesToRadians(pitchDegrees);

        var front = new Vector3(
            MathF.Cos(pitch) * MathF.Cos(yaw),
            MathF.Sin(pitch),
            MathF.Cos(pitch) * MathF.Sin(yaw));

        return Vector3.Normalize(front);
    }

    // Направление, заданное только мышью. HUD ветра использует его, чтобы
    // стрелка не дрожала вместе с имитацией покачивания оружия.
    private Vector3 BaseFront => DirectionFromAngles(_yaw, _pitch);

    // Фактическая ось прицеливания. При включённой оптике к ней добавляется лёгкое
    // физиологическое покачивание; по этой же оси вылетает пуля.
    private Vector3 AimFront => DirectionFromAngles(
        _yaw + _aimSwayYaw,
        _pitch + _aimSwayPitch);

    // Фактическая ось ствола. ZERO поднимает ствол относительно линии визирования,
    // а само перекрестие и камера остаются направлены точно туда, куда целится игрок.
    private Vector3 ShotFront => DirectionFromAngles(
        _yaw + _aimSwayYaw,
        _pitch + _aimSwayPitch + _zeroElevationAngleDeg);

    private Vector3 GetSightUp(Vector3 sightDirection)
    {
        Vector3 right = Vector3.Cross(sightDirection, Vector3.UnitY);
        if (right.LengthSquared < 0.000001f)
            right = Vector3.UnitX;
        else
            right = Vector3.Normalize(right);

        Vector3 up = Vector3.Cross(right, sightDirection);
        return up.LengthSquared < 0.000001f ? Vector3.UnitY : Vector3.Normalize(up);
    }

    private Vector3 GetMuzzlePosition(Vector3 sightDirection)
    {
        Vector3 sightUp = GetSightUp(sightDirection);
        return _cameraPosition
            + sightDirection * 0.85f
            - sightUp * ActiveWeapon.SightHeightMeters;
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        GL.ClearColor(0.30f, 0.32f, 0.34f, 1f);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.Multisample);

        _shader = new Shader(VertexShaderSource, FragmentShaderSource);
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
            _target.UpdatePhysics(dt, 0f, _target.Position.Z - 10f);
        else
            _house.UpdatePhysics(dt, 0f);

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

        cameraPosition = targetPosition
            + targetToShooter * 10f
            + leftFromShooter * 5f;

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

    private void UpdateAimSway(float dt, bool aiming)
    {
        if (_stabilizationEnabled)
        {
            _aimSwayBlend = 0f;
            _aimSwayYaw = 0f;
            _aimSwayPitch = 0f;
            return;
        }

        _aimSwayTime += dt;

        float blendTarget = aiming ? 1f : 0f;
        float blendAlpha = 1f - MathF.Exp(-AimSwayBlendResponse * dt);
        _aimSwayBlend += (blendTarget - _aimSwayBlend) * blendAlpha;

        // Несколько медленных гармоник с разными периодами дают плавное
        // неповторяющееся движение без резкого случайного джиттера.
        float t = _aimSwayTime;
        float yawSignal =
            0.62f * MathF.Sin(t * 0.62f + 0.35f) +
            0.25f * MathF.Sin(t * 1.07f + 2.10f) +
            0.13f * MathF.Sin(t * 0.21f + 4.40f);

        float pitchSignal =
            0.60f * MathF.Sin(t * 0.53f + 1.15f) +
            0.27f * MathF.Sin(t * 0.96f + 3.30f) +
            0.13f * MathF.Sin(t * 0.18f + 5.20f);

        _aimSwayYaw = AimSwayYawDegrees * yawSignal * _aimSwayBlend;
        _aimSwayPitch = AimSwayPitchDegrees * pitchSignal * _aimSwayBlend;
    }

    private void UpdateImpactPrediction(float dt, bool aiming)
    {
        if (!_impactPredictionEnabled || _impactPredictionLockedRangeMeters <= 0f ||
            !aiming || _distanceMenuOpen || TrackedBulletCamera is not null)
        {
            _hasPredictedImpact = false;
            _impactPredictionRefreshTimer = 0f;
            return;
        }

        _impactPredictionRefreshTimer += dt;
        if (_hasPredictedImpact && _impactPredictionRefreshTimer < ImpactPredictionRefreshSeconds)
            return;

        _impactPredictionRefreshTimer = 0f;
        _hasPredictedImpact = TryPredictImpactAtLockedRange(out _predictedImpactPoint);
    }

    /// <summary>
    /// При включении T один раз измеряет дистанцию по центральному лучу прицела.
    /// В дальнейшем эта дальность остаётся неизменной, пока пользователь не
    /// выключит T и не захватит новую цель. Это намеренно НЕ динамический
    /// дальномер.
    /// </summary>
    private bool TryAcquireImpactPredictionRange(out float lockedRangeMeters)
    {
        lockedRangeMeters = 0f;

        Vector3 aimDirection = AimFront;
        if (aimDirection.LengthSquared < 0.000001f)
            return false;
        aimDirection = Vector3.Normalize(aimDirection);

        Vector3 rayOrigin = _cameraPosition;
        const float maxLockDistanceMeters = 2600f;
        Vector3 rayEnd = rayOrigin + aimDirection * maxLockDistanceMeters;

        bool found = false;
        float nearestRayDistance = float.PositiveInfinity;
        Vector3 nearestPoint = default;

        void ConsiderPoint(Vector3 point)
        {
            float alongRay = Vector3.Dot(point - rayOrigin, aimDirection);
            if (alongRay <= 0.01f || alongRay >= nearestRayDistance)
                return;

            nearestRayDistance = alongRay;
            nearestPoint = point;
            found = true;
        }

        // Существующий бетон. Разрушенное отверстие луч пропускает, поэтому
        // через него можно захватить более дальнюю стену.
        if (TryHitActiveDestructibleSegment(rayOrigin, rayEnd, out Vector3 concretePoint))
            ConsiderPoint(concretePoint);

        // Земля.
        if (aimDirection.Y < -0.000001f)
        {
            float groundT = -rayOrigin.Y / aimDirection.Y;
            if (groundT > 0f && groundT <= maxLockDistanceMeters)
                ConsiderPoint(rayOrigin + aimDirection * groundT);
        }

        if (!found)
            return false;

        // Реальная пуля стартует на 0,85 м впереди камеры. Храним именно
        // downrange-дистанцию от дульной точки до захваченной поверхности.
        Vector3 muzzlePosition = GetMuzzlePosition(aimDirection);
        lockedRangeMeters = Vector3.Dot(nearestPoint - muzzlePosition, aimDirection);
        lockedRangeMeters = MathF.Max(1f, lockedRangeMeters);
        return true;
    }

    /// <summary>
    /// Прогнозирует положение пули на ЗАРАНЕЕ ЗАХВАЧЕННОЙ дистанции.
    /// Геометрия мира больше не определяет дальность при каждом обновлении.
    /// При этом ветер семплируется заново при каждом обновлении прогноза и на
    /// каждом RK4-подшаге, поэтому изменение ветра постоянно двигает красную
    /// точку. Используется то же оружие, сопротивление и гравитация, что и у
    /// настоящего выстрела.
    /// </summary>
    private bool TryPredictImpactAtLockedRange(out Vector3 impactPoint)
    {
        impactPoint = default;
        if (_impactPredictionLockedRangeMeters <= 0f)
            return false;

        Vector3 aimDirection = AimFront;
        if (aimDirection.LengthSquared < 0.000001f)
            return false;
        aimDirection = Vector3.Normalize(aimDirection);

        Vector3 muzzlePosition = GetMuzzlePosition(aimDirection);
        Vector3 shotDirection = ShotFront;
        BallisticProfile ballistics = ActiveBallistics;

        var simulatedBullet = new Bullet(
            muzzlePosition,
            shotDirection * ballistics.MuzzleVelocityMps,
            ballistics);

        float simulationTime = _wind.TimeSeconds;
        int maxSteps = (int)MathF.Ceiling(20f / BallisticStepSeconds);

        for (int i = 0; i < maxSteps; i++)
        {
            simulatedBullet.Step(
                BallisticStepSeconds,
                _wind,
                simulationTime,
                _activeWindLimitMps);

            simulationTime += BallisticStepSeconds;

            Vector3 from = simulatedBullet.PreviousPosition;
            Vector3 to = simulatedBullet.Position;

            float fromDownrange = Vector3.Dot(from - muzzlePosition, aimDirection);
            float toDownrange = Vector3.Dot(to - muzzlePosition, aimDirection);

            if (toDownrange >= _impactPredictionLockedRangeMeters &&
                toDownrange > fromDownrange + 0.000001f)
            {
                float t = (_impactPredictionLockedRangeMeters - fromDownrange)
                    / (toDownrange - fromDownrange);
                t = MathHelper.Clamp(t, 0f, 1f);
                impactPoint = from + (to - from) * t;
                return true;
            }

            if (!simulatedBullet.IsAlive)
                break;
        }

        return false;
    }

    private void UpdateAutomaticFire(float dt)
    {
        // В режиме камеры пули после первого выстрела автоматический огонь
        // приостанавливается, чтобы камера не перескакивала между очередью.
        if (TrackedBulletCamera is not null)
        {
            _automaticFireCooldown = 0f;
            return;
        }

        if (!ActiveWeapon.IsAutomatic || ActiveWeapon.RateOfFireRpm <= 0f)
        {
            _automaticFireCooldown = 0f;
            return;
        }

        if (!MouseState.IsButtonDown(MouseButton.Left))
        {
            // После отпускания ЛКМ следующий зажим начинается с мгновенного выстрела.
            _automaticFireCooldown = 0f;
            return;
        }

        _automaticFireCooldown -= dt;
        float secondsPerShot = 60f / ActiveWeapon.RateOfFireRpm;

        // Safety ограничивает число выстрелов за один кадр после сильного лага.
        int safety = 0;
        while (_automaticFireCooldown <= 0f && safety < 3)
        {
            Fire();
            _automaticFireCooldown += secondsPerShot;
            safety++;
        }
    }

    private void Fire()
    {
        _shots++;

        // Пристрелка не двигает перекрестие: она меняет угол ствола относительно
        // линии визирования. На выбранной ZERO-дистанции без ветра траектория
        // пересекает центральную линию прицела.
        Vector3 sightDirection = AimFront;
        Vector3 shotDirection = ShotFront;
        Vector3 muzzlePosition = GetMuzzlePosition(sightDirection);
        BallisticProfile ballistics = ActiveBallistics;

        // Каждый реальный патрон немного отличается по начальной скорости.
        // ZERO/T/риски прицела остаются откалиброваны по номинальной средней,
        // а конкретный выстрел получает собственную гауссову вариацию.
        float actualMuzzleVelocityMps = ballistics.SampleMuzzleVelocity(_random);
        Vector3 muzzleVelocity = shotDirection * actualMuzzleVelocityMps;

        var bullet = new Bullet(muzzlePosition, muzzleVelocity, ballistics);
        _bullets.Add(bullet);

        if (_cameraMode == CameraMode.Bullet)
            _trackedBullet = bullet;

        _lastShot = "BULLET IN FLIGHT";

        // В обычном режиме отдача подбрасывает прицел вверх. В режиме O
        // отдача полностью отключена — ни одного углового смещения не добавляется.
        if (!_stabilizationEnabled)
            _targetPitch = MathHelper.Clamp(_targetPitch + ActiveWeapon.RecoilPitchDegrees, -75f, 75f);

        UpdateWindowTitle();
    }

    private void UpdateBullets(float frameDt)
    {
        _ballisticAccumulator += frameDt;

        // Ограничение защищает от спирали накопления после остановки в отладчике.
        int safetySteps = 0;
        while (_ballisticAccumulator >= BallisticStepSeconds && safetySteps < 80)
        {
            StepBallistics(BallisticStepSeconds);
            _ballisticAccumulator -= BallisticStepSeconds;
            safetySteps++;
        }

        if (safetySteps == 80)
            _ballisticAccumulator = 0f;

        _bullets.RemoveAll(b => !b.IsAlive);

        if (_trackedBullet is not null && (!_trackedBullet.IsAlive || _trackedBullet.HasImpacted))
            _trackedBullet = null;
    }

    private void StepBallistics(float dt)
    {
        // Ветер обновляется тем же фиксированным физическим шагом, что и пули.
        // Это делает порывы независимыми от FPS.
        float stepStartTime = _wind.TimeSeconds;
        _wind.Update(dt);

        foreach (Bullet bullet in _bullets)
        {
            if (!bullet.IsAlive)
                continue;

            bullet.Step(dt, _wind, stepStartTime, _activeWindLimitMps);

            if (bullet.HasImpacted)
                continue;

            ResolveBulletCollisions(bullet);
        }
    }

    private void ResolveBulletCollisions(Bullet bullet)
    {
        Vector3 from = bullet.PreviousPosition;
        Vector3 to = bullet.Position;

        // CCD: сначала находим ВСЕ потенциальные столкновения на последнем
        // отрезке физического шага, затем выбираем самое раннее по hitT.
        float concreteT = float.PositiveInfinity;
        Vector3 concreteImpact = default;
        bool hasConcrete = TryHitActiveDestructibleSegment(from, to, out concreteT, out concreteImpact);

        bool hasGround = false;
        float groundT = float.PositiveInfinity;
        Vector3 groundImpact = default;
        if (from.Y > 0f && to.Y <= 0f)
        {
            groundT = from.Y / (from.Y - to.Y);
            if (groundT >= 0f && groundT <= 1f)
            {
                hasGround = true;
                groundImpact = from + (to - from) * groundT;
            }
        }

        if (!hasConcrete && !hasGround)
            return;

        if (hasConcrete && (!hasGround || concreteT <= groundT))
        {
            // Возвращаем состояние пули к реальному моменту контакта, а не
            // используем скорость/время в конце 1-мс физического шага.
            bullet.MoveToSegmentFraction(concreteT);
            concreteImpact = bullet.Position;
            bool firstRegisteredHit = !bullet.ResultRegistered;
            bullet.ResultRegistered = true;

            ProjectileImpactData projectile = ProjectileImpactData.From(
                bullet.Profile,
                bullet.Velocity);
            ConcreteImpactResult impact = ApplyImpactToActiveDestructible(
                concreteImpact,
                projectile);

            RegisterConcreteHit(
                bullet,
                impact,
                concreteImpact,
                projectile.Direction,
                firstRegisteredHit);
            return;
        }

        // Земля оказалась ближе по тому же continuous segment.
        bullet.MoveToSegmentFraction(groundT);
        groundImpact = bullet.Position;
        if (!bullet.ResultRegistered)
        {
            bullet.ResultRegistered = true;
            RegisterMiss(bullet);
        }
        else
        {
            CaptureImpactTelemetry(bullet);
        }

        bullet.Impact(groundImpact);
        UpdateWindowTitle();
    }

    private void RegisterConcreteHit(
        Bullet bullet,
        ConcreteImpactResult impact,
        Vector3 impactPoint,
        Vector3 incomingDirection,
        bool countAsNewHit)
    {
        if (countAsNewHit)
            _hits++;
        _score += impact.RemovedCellCount;
        CaptureImpactTelemetry(bullet);

        float integrityPercent = ActiveSceneIntegrity01 * 100f;
        string objectName = _activeScene == SceneType.RangeCube ? "CUBE" : "HOUSE";

        if (impact.Perforated && impact.ResidualSpeedMps > 5f)
        {
            _lastShot =
                $"{objectName} PERFORATED | PEN {impact.PenetrationDepthMeters * 100f:0.0} CM | " +
                $"EXIT {impact.ResidualSpeedMps:0} M/S | {integrityPercent:0}% LEFT";

            // Пуля не умирает: она выходит из бетона с остаточной энергией
            // и может попасть во вторую стену/перегородку/землю.
            bullet.ContinueAfterPenetration(incomingDirection, impact.ResidualSpeedMps);
        }
        else
        {
            _lastShot = impact.RemovedCellCount > 0
                ? $"{objectName} -{impact.RemovedCellCount} | PEN {impact.PenetrationDepthMeters * 100f:0.0} CM | DEBRIS {impact.SpawnedFragmentCount} | {integrityPercent:0}% LEFT"
                : $"{objectName} DAMAGED | PEN {impact.PenetrationDepthMeters * 100f:0.0} CM | {integrityPercent:0}% LEFT";

            bullet.Impact(impactPoint);
        }

        // Основной массив остаётся на месте; отделившиеся куски живут своей физикой.
        UpdateWindowTitle();
    }

    private void RegisterMiss(Bullet bullet)
    {
        CaptureImpactTelemetry(bullet);
        _lastShot = "MISS";
    }

    private void CaptureImpactTelemetry(Bullet bullet)
    {
        _lastMuzzleVelocity = bullet.InitialSpeedMps;
        _lastFlightTime = bullet.AgeSeconds;
        _lastImpactSpeed = bullet.SpeedMps;
        _lastImpactEnergy = bullet.Profile.KineticEnergyJoules(_lastImpactSpeed);
    }

    private static bool TryCrossZPlane(Vector3 from, Vector3 to, float z, out Vector3 point)
    {
        point = default;

        float dz = to.Z - from.Z;
        if (MathF.Abs(dz) < 0.000001f)
            return false;

        float t = (z - from.Z) / dz;
        if (t < 0f || t > 1f)
            return false;

        point = from + (to - from) * t;
        return true;
    }

    private bool TryHitActiveDestructibleSegment(Vector3 from, Vector3 to, out Vector3 hitPoint)
        => TryHitActiveDestructibleSegment(from, to, out _, out hitPoint);

    private bool TryHitActiveDestructibleSegment(
        Vector3 from,
        Vector3 to,
        out float hitT,
        out Vector3 hitPoint)
    {
        return _activeScene == SceneType.RangeCube
            ? _target.TryHitSegment(from, to, out hitT, out hitPoint)
            : _house.TryHitSegment(from, to, out hitT, out hitPoint);
    }

    private ConcreteImpactResult ApplyImpactToActiveDestructible(
        Vector3 hitPoint,
        ProjectileImpactData projectile)
    {
        return _activeScene == SceneType.RangeCube
            ? _target.ApplyImpact(hitPoint, projectile)
            : _house.ApplyImpact(hitPoint, projectile);
    }

    private void SetActiveSceneDistance(float distanceMeters)
    {
        if (_activeScene == SceneType.RangeCube)
            _target.SetDistance(distanceMeters, _cameraPosition.Z);
        else
            _house.SetDistance(distanceMeters, _cameraPosition.Z);
    }

    private void ResetActiveDestructibleDamage()
    {
        if (_activeScene == SceneType.RangeCube)
            _target.ResetDamage();
        else
            _house.ResetDamage();
    }

    private void OpenDistanceMenu()
    {
        _distanceMenuOpen = true;
        _draggingDistanceSlider = false;
        _draggingScopeSlider = false;
        _draggingWindSlider = false;
        _menuDistanceMeters = (int)ActiveSceneDistanceMeters;
        _menuScene = _activeScene;
        _menuScopeMagnificationIndex = _scopeMagnificationIndex;
        _menuWindLimitMps = _activeWindLimitMps;
        _menuWeaponIndex = _activeWeaponIndex;
        _ignoreNextMouseMove = true;
        CursorState = CursorState.Normal;

        // Ставим видимый курсор на текущее положение бегунка.
        DistanceMenuLayout layout = GetDistanceMenuLayout();
        float t = (_menuDistanceMeters - MinTargetDistance) / (float)(MaxTargetDistance - MinTargetDistance);
        float knobX = layout.SliderLeft + (layout.SliderRight - layout.SliderLeft) * t;
        MousePosition = new Vector2(knobX, layout.DistanceSliderY);

        UpdateWindowTitle();
    }

    private void CloseDistanceMenu()
    {
        _distanceMenuOpen = false;
        _draggingDistanceSlider = false;
        _draggingScopeSlider = false;
        _draggingWindSlider = false;
        _ignoreNextMouseMove = true;
        CursorState = CursorState.Grabbed;
        UpdateWindowTitle();
    }

    private void HandleDistanceMenuMouseDown(Vector2 mouse)
    {
        DistanceMenuLayout layout = GetDistanceMenuLayout();

        if (PointInRect(mouse, layout.Scene1Left, layout.SceneTop, layout.SceneButtonWidth, layout.SceneButtonHeight))
        {
            _menuScene = SceneType.RangeCube;
            UpdateWindowTitle();
            return;
        }

        if (PointInRect(mouse, layout.Scene2Left, layout.SceneTop, layout.SceneButtonWidth, layout.SceneButtonHeight))
        {
            _menuScene = SceneType.DestructibleHouse;
            UpdateWindowTitle();
            return;
        }

        if (PointInRect(mouse, layout.Weapon1Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight))
        {
            _menuWeaponIndex = 0;
            UpdateWindowTitle();
            return;
        }

        if (PointInRect(mouse, layout.Weapon2Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight))
        {
            _menuWeaponIndex = 1;
            UpdateWindowTitle();
            return;
        }

        if (PointInRect(mouse, layout.Weapon3Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight))
        {
            _menuWeaponIndex = 2;
            UpdateWindowTitle();
            return;
        }

        if (PointInRect(mouse, layout.Weapon4Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight))
        {
            _menuWeaponIndex = 3;
            UpdateWindowTitle();
            return;
        }

        // Увеличенная зона клика делает ползунки удобными даже на высоком DPI.
        if (mouse.X >= layout.SliderLeft - 14f && mouse.X <= layout.SliderRight + 14f &&
            mouse.Y >= layout.DistanceSliderY - 22f && mouse.Y <= layout.DistanceSliderY + 22f)
        {
            _draggingDistanceSlider = true;
            _draggingScopeSlider = false;
            _draggingWindSlider = false;
            UpdateMenuDistanceFromMouse(mouse.X);
            return;
        }

        if (mouse.X >= layout.SliderLeft - 14f && mouse.X <= layout.SliderRight + 14f &&
            mouse.Y >= layout.ScopeSliderY - 22f && mouse.Y <= layout.ScopeSliderY + 22f)
        {
            _draggingScopeSlider = true;
            _draggingDistanceSlider = false;
            _draggingWindSlider = false;
            UpdateMenuScopeFromMouse(mouse.X);
            return;
        }

        if (mouse.X >= layout.SliderLeft - 14f && mouse.X <= layout.SliderRight + 14f &&
            mouse.Y >= layout.WindSliderY - 22f && mouse.Y <= layout.WindSliderY + 22f)
        {
            _draggingWindSlider = true;
            _draggingDistanceSlider = false;
            _draggingScopeSlider = false;
            UpdateMenuWindFromMouse(mouse.X);
            return;
        }

        if (PointInRect(mouse, layout.ApplyLeft, layout.ButtonTop, layout.ButtonWidth, layout.ButtonHeight))
        {
            ApplyDistanceMenu();
            return;
        }

        if (PointInRect(mouse, layout.CancelLeft, layout.ButtonTop, layout.ButtonWidth, layout.ButtonHeight))
        {
            CloseDistanceMenu();
        }
    }

    private void UpdateMenuDistanceFromMouse(float mouseX)
    {
        DistanceMenuLayout layout = GetDistanceMenuLayout();
        float t = MathHelper.Clamp(
            (mouseX - layout.SliderLeft) / (layout.SliderRight - layout.SliderLeft),
            0f,
            1f);

        float rawDistance = MinTargetDistance + t * (MaxTargetDistance - MinTargetDistance);
        int snapped = (int)MathF.Round(rawDistance / TargetDistanceStep) * TargetDistanceStep;
        _menuDistanceMeters = Math.Clamp(snapped, MinTargetDistance, MaxTargetDistance);
        UpdateWindowTitle();
    }

    private void UpdateMenuScopeFromMouse(float mouseX)
    {
        DistanceMenuLayout layout = GetDistanceMenuLayout();
        float t = MathHelper.Clamp(
            (mouseX - layout.SliderLeft) / (layout.SliderRight - layout.SliderLeft),
            0f,
            1f);

        int index = (int)MathF.Round(t * (ScopeMagnifications.Length - 1));
        _menuScopeMagnificationIndex = Math.Clamp(index, 0, ScopeMagnifications.Length - 1);
        UpdateWindowTitle();
    }

    private void UpdateMenuWindFromMouse(float mouseX)
    {
        DistanceMenuLayout layout = GetDistanceMenuLayout();
        float t = MathHelper.Clamp(
            (mouseX - layout.SliderLeft) / (layout.SliderRight - layout.SliderLeft),
            0f,
            1f);

        float rawWind = MinWindLimitMps + t * (MaxWindLimitMps - MinWindLimitMps);
        int snapped = (int)MathF.Round(rawWind / WindLimitStepMps) * WindLimitStepMps;
        _menuWindLimitMps = Math.Clamp(snapped, MinWindLimitMps, MaxWindLimitMps);

        // Ползунок меняет только значение в меню. Физический лимит становится
        // активным только после APPLY, поэтому M/Esc/CANCEL не создают
        // скрытого возврата к старому ограничению.
        UpdateWindowTitle();
    }

    private static bool PointInRect(Vector2 p, float x, float y, float width, float height)
    {
        return p.X >= x && p.X <= x + width && p.Y >= y && p.Y <= y + height;
    }

    private DistanceMenuLayout GetDistanceMenuLayout()
    {
        float panelWidth = MathF.Min(650f, ClientSize.X - 40f);
        float panelHeight = MathF.Min(690f, ClientSize.Y - 20f);
        float left = (ClientSize.X - panelWidth) * 0.5f;
        float top = (ClientSize.Y - panelHeight) * 0.5f;
        float right = left + panelWidth;
        float bottom = top + panelHeight;

        float sliderLeft = left + 72f;
        float sliderRight = right - 72f;

        const float sceneGap = 14f;
        float sceneButtonWidth = (sliderRight - sliderLeft - sceneGap) * 0.5f;
        const float sceneButtonHeight = 36f;
        float scene1Left = sliderLeft;
        float scene2Left = scene1Left + sceneButtonWidth + sceneGap;
        float sceneTop = top + 62f;

        const float weaponGap = 10f;
        float weaponButtonWidth = (sliderRight - sliderLeft - weaponGap * 3f) / 4f;
        const float weaponButtonHeight = 38f;
        float weapon1Left = sliderLeft;
        float weapon2Left = weapon1Left + weaponButtonWidth + weaponGap;
        float weapon3Left = weapon2Left + weaponButtonWidth + weaponGap;
        float weapon4Left = weapon3Left + weaponButtonWidth + weaponGap;
        float weaponTop = top + 145f;

        float distanceSliderY = top + 300f;
        float scopeSliderY = top + 410f;
        float windSliderY = top + 520f;

        const float buttonWidth = 150f;
        const float buttonHeight = 36f;
        const float buttonGap = 22f;
        float buttonsTotal = buttonWidth * 2f + buttonGap;
        float applyLeft = (ClientSize.X - buttonsTotal) * 0.5f;
        float cancelLeft = applyLeft + buttonWidth + buttonGap;
        float buttonTop = bottom - 48f;

        return new DistanceMenuLayout(
            left, top, right, bottom,
            sliderLeft, sliderRight,
            scene1Left, scene2Left, sceneTop, sceneButtonWidth, sceneButtonHeight,
            weapon1Left, weapon2Left, weapon3Left, weapon4Left, weaponTop, weaponButtonWidth, weaponButtonHeight,
            distanceSliderY, scopeSliderY, windSliderY,
            applyLeft, cancelLeft, buttonTop, buttonWidth, buttonHeight);
    }

    private readonly record struct DistanceMenuLayout(
        float Left,
        float Top,
        float Right,
        float Bottom,
        float SliderLeft,
        float SliderRight,
        float Scene1Left,
        float Scene2Left,
        float SceneTop,
        float SceneButtonWidth,
        float SceneButtonHeight,
        float Weapon1Left,
        float Weapon2Left,
        float Weapon3Left,
        float Weapon4Left,
        float WeaponTop,
        float WeaponButtonWidth,
        float WeaponButtonHeight,
        float DistanceSliderY,
        float ScopeSliderY,
        float WindSliderY,
        float ApplyLeft,
        float CancelLeft,
        float ButtonTop,
        float ButtonWidth,
        float ButtonHeight);

    private void ApplyDistanceMenu()
    {
        _menuDistanceMeters = Math.Clamp(
            (_menuDistanceMeters / TargetDistanceStep) * TargetDistanceStep,
            MinTargetDistance,
            MaxTargetDistance);

        bool sceneChanged = _activeScene != _menuScene;
        _activeScene = _menuScene;
        SetActiveSceneDistance(_menuDistanceMeters);
        if (sceneChanged)
            ResetActiveDestructibleDamage();

        _scopeMagnificationIndex = Math.Clamp(_menuScopeMagnificationIndex, 0, ScopeMagnifications.Length - 1);
        _menuWindLimitMps = Math.Clamp(_menuWindLimitMps, MinWindLimitMps, MaxWindLimitMps);
        _activeWindLimitMps = _menuWindLimitMps;
        _menuWeaponIndex = Math.Clamp(_menuWeaponIndex, 0, _weapons.Length - 1);
        _activeWeaponIndex = _menuWeaponIndex;
        RecalculateZeroing();
        RecalculateScopeRangeMarks();

        // Старые пули относятся к прежней геометрии полигона или оружию.
        _bullets.Clear();
        _trackedBullet = null;
        _ballisticAccumulator = 0f;
        _automaticFireCooldown = 0f;
        _lastMuzzleVelocity = 0f;
        _lastFlightTime = 0f;
        _lastImpactSpeed = 0f;
        _lastImpactEnergy = 0f;
        _impactPredictionEnabled = false;
        _impactPredictionLockedRangeMeters = 0f;
        _hasPredictedImpact = false;
        _lastShot = $"{ActiveWeapon.DisplayName} | {ActiveSceneHudName} | RANGE {_menuDistanceMeters} M";

        CloseDistanceMenu();
    }

    private void RestoreActiveDestructible()
    {
        // Полностью восстанавливаем текущий разрушаемый объект и удаляем его обломки.
        ResetActiveDestructibleDamage();

        // Пули, уже находящиеся в воздухе, удаляем, чтобы сразу после восстановления
        // они не повредили свежий куб старым выстрелом.
        _bullets.Clear();
        _trackedBullet = null;
        _ballisticAccumulator = 0f;
        _automaticFireCooldown = 0f;

        _lastMuzzleVelocity = 0f;
        _lastFlightTime = 0f;
        _lastImpactSpeed = 0f;
        _lastImpactEnergy = 0f;
        _lastShot = _activeScene == SceneType.RangeCube ? "CUBE RESTORED" : "HOUSE RESTORED";
        _hasPredictedImpact = false;
        _impactPredictionRefreshTimer = ImpactPredictionRefreshSeconds;

        UpdateWindowTitle();
    }

    private void DrawRange()
    {
        if (_cube is null)
            return;

        if (_activeScene == SceneType.RangeCube)
            DrawCubeRange();
        else
            DrawHouseRange();
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
        else
            DrawDestructibleHouse();
    }

    private void DrawConcreteCubeTarget()
    {
        float cell = _target.CellSizeMeters;
        float renderedCell = cell * 0.992f;
        int resolution = _target.GridResolution;

        for (int x = 0; x < resolution; x++)
        for (int y = 0; y < resolution; y++)
        for (int z = 0; z < resolution; z++)
        {
            if (!_target.IsCellIntact(x, y, z))
                continue;

            Vector3 position = _target.GetCellWorldPosition(x, y, z);
            int shadeIndex = (x * 17 + y * 7 + z * 13) % 6;
            float damage = _target.GetCellDamage01(x, y, z);
            float shade = (0.385f + shadeIndex * 0.010f) * (1f - 0.28f * damage);
            Vector3 concrete = new(shade, shade + 0.006f, shade + 0.011f);
            DrawCube(position, new Vector3(renderedCell), concrete);
        }

        foreach (ConcreteFragment fragment in _target.Fragments)
        {
            DrawCubeRotated(
                fragment.Position,
                fragment.SizeMeters,
                fragment.Color,
                fragment.RotationRadians);
        }

        float cubeBottom = _target.Position.Y - _target.HalfSizeMeters;
        if (cubeBottom > 0.05f)
        {
            DrawCube(
                new Vector3(_target.Position.X, cubeBottom * 0.5f, _target.Position.Z),
                new Vector3(0.24f, cubeBottom, 0.24f),
                new Vector3(0.28f, 0.29f, 0.30f));
        }
    }

    private void DrawDestructibleHouse()
    {
        float renderedCell = _house.CellSizeMeters * 0.985f;

        for (int x = 0; x < _house.GridX; x++)
        for (int y = 0; y < _house.GridY; y++)
        for (int z = 0; z < _house.GridZ; z++)
        {
            if (!_house.IsCellIntact(x, y, z))
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
        GL.LineWidth(2f);
        GL.DrawArrays(PrimitiveType.Lines, 0, 4);
        GL.BindVertexArray(0);

        GL.Enable(EnableCap.DepthTest);
    }

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
            string menuSceneName = _menuScene == SceneType.RangeCube ? "RANGE+CUBE" : "HOUSE";
            Title = $"Sniper 3D v3.3 CONCRETE MATERIAL | SETTINGS | {menuSceneName} | {_weapons[_menuWeaponIndex].DisplayName} | {_menuDistanceMeters} m | {ScopeMagnifications[_menuScopeMagnificationIndex]:0}x | Wind max {_menuWindLimitMps} m/s | Mouse + APPLY | M/Esc cancel";
        }
        else
        {
            string fireHint = ActiveWeapon.IsAutomatic ? "Hold LMB auto" : "LMB fire";
            string cameraStatus = _cameraMode switch
            {
                CameraMode.Shooter => "CAM 1 SHOOTER",
                CameraMode.Bullet => TrackedBulletCamera is not null ? "CAM 2 BULLET" : "CAM 2 BULLET ARMED",
                CameraMode.TargetObserver => "CAM 3 TARGET + SHOOTER PIP",
                _ => "CAM"
            };

            string stabilizationStatus = _stabilizationEnabled ? "O STABLE ON" : "O STABLE OFF";
            Title = $"Sniper 3D v3.3 CONCRETE MATERIAL | {ActiveSceneHudName} | {ActiveWeapon.DisplayName} | {ActiveSceneDistanceMeters:0} m | Scope {ScopeMagnifications[_scopeMagnificationIndex]:0}x | ZERO {_zeroDistanceMeters} m | Wind max {_activeWindLimitMps} m/s | {cameraStatus} | {stabilizationStatus} | Structure {ActiveSceneIntegrity01 * 100f:0}% | Debris {ActiveSceneDebrisCount} | Hits {_hits}/{_shots} ({accuracy:0}%) | {_lastShot} | {telemetry} | {fireHint} | RMB toggle zoom | Wheel ZERO 0..2000m / 100m | {(_impactPredictionEnabled ? $"T LOCK {_impactPredictionLockedRangeMeters:0}m" : "T LOCK OFF")} | 1 shooter | 2 bullet cam | 3 target cam + PIP | O no recoil/sway | M settings | R restore";
        }
    }

    private const string VertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;

        uniform mat4 model;
        uniform mat4 view;
        uniform mat4 projection;

        out vec3 Normal;

        void main()
        {
            mat3 normalMatrix = transpose(inverse(mat3(model)));
            Normal = normalMatrix * aNormal;
            gl_Position = projection * view * model * vec4(aPosition, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        out vec4 FragColor;

        in vec3 Normal;

        uniform vec3 objectColor;
        uniform int useLighting;

        void main()
        {
            if (useLighting == 0)
            {
                FragColor = vec4(objectColor, 1.0);
                return;
            }

            vec3 n = normalize(Normal);
            vec3 lightDir = normalize(vec3(-0.45, 0.85, 0.35));
            float diffuse = max(dot(n, lightDir), 0.0);
            float brightness = 0.40 + diffuse * 0.60;
            FragColor = vec4(objectColor * brightness, 1.0);
        }
        """;
}
