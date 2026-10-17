using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Sniper3D;

internal sealed partial class SniperGame : GameWindow
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
        DestructibleHouse,
        SteelPlate
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
    private readonly SteelPlateTarget _steel = new();
    private readonly WeaponProfile[] _weapons = WeaponProfile.CreateAll();
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
    private Mesh? _steelMesh;
    private int _steelMeshRevision = -1;
    private Mesh? _concreteMesh;
    private int _concreteMeshRevision = -1;
    private readonly Dictionary<ConcreteFragment, Mesh> _concreteFragmentMeshes = new();

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
    private float _shotCooldownSeconds;
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
    private float ActiveSceneDistanceMeters => _activeScene switch
    {
        SceneType.RangeCube => _target.DistanceMeters,
        SceneType.DestructibleHouse => _house.DistanceMeters,
        _ => _steel.DistanceMeters
    };
    private Vector3 ActiveSceneFocus => _activeScene switch
    {
        SceneType.RangeCube => _target.Position,
        SceneType.DestructibleHouse => _house.ObserverFocus,
        _ => _steel.Position
    };
    private float ActiveSceneIntegrity01 => _activeScene == SceneType.RangeCube ? _target.Integrity01 : _house.Integrity01;
    private int ActiveSceneDebrisCount => _activeScene switch
    {
        SceneType.RangeCube => _target.Fragments.Count,
        SceneType.DestructibleHouse => _house.Fragments.Count,
        _ => _steel.Fragments.Count
    };
    private string ActiveSceneHudName => SceneHudName(_activeScene);
    private static string SceneHudName(SceneType scene) => scene switch
    {
        SceneType.RangeCube => "RANGE+CUBE",
        SceneType.DestructibleHouse => "HOUSE",
        _ => "STEEL S355 1M X 10MM"
    };

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
}
