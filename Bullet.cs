using OpenTK.Mathematics;

namespace Sniper3D;

/// <summary>
/// Физический снаряд. Интегрируется методом RK4 с фиксированным шагом.
/// </summary>
internal sealed class Bullet
{
    private const int MaxTrailPoints = 80;
    private const float TrailSampleInterval = 0.004f;

    private readonly List<Vector3> _trail = new(MaxTrailPoints);
    private float _trailTimer;
    private float _lingerAfterImpact;

    // Горизонтальная ось вправо относительно направления выстрела.
    // По ней прикладывается накопленный spin drift.
    private readonly Vector3 _spinDriftRight;
    private float _appliedSpinDriftMeters;

    public Vector3 Position { get; private set; }
    public Vector3 PreviousPosition { get; private set; }
    public Vector3 Velocity { get; private set; }
    public Vector3 PreviousVelocity { get; private set; }

    public float AgeSeconds { get; private set; }
    public float PreviousAgeSeconds { get; private set; }
    public float DistanceTraveledMeters { get; private set; }
    public float PreviousDistanceTraveledMeters { get; private set; }
    public bool ResultRegistered { get; set; }
    public bool HasImpacted { get; private set; }
    public bool IsAlive { get; private set; } = true;

    public IReadOnlyList<Vector3> Trail => _trail;
    public float SpeedMps => Velocity.Length;
    public float InitialSpeedMps { get; }
    public BallisticProfile Profile { get; }

    public Bullet(Vector3 position, Vector3 velocity, BallisticProfile profile)
    {
        Profile = profile;
        Position = position;
        PreviousPosition = position;
        Velocity = velocity;
        PreviousVelocity = velocity;
        InitialSpeedMps = velocity.Length;

        Vector3 forward = velocity.LengthSquared > 0.000001f
            ? Vector3.Normalize(velocity)
            : -Vector3.UnitZ;
        Vector3 right = Vector3.Cross(forward, Vector3.UnitY);
        _spinDriftRight = right.LengthSquared > 0.000001f
            ? Vector3.Normalize(right)
            : Vector3.UnitX;

        _trail.Add(position);
    }

    public void Step(
        float dt,
        WindField wind,
        float stepStartTime,
        float windLimitMps,
        bool ignoreLifetimeLimits = false)
    {
        if (!IsAlive)
            return;

        if (HasImpacted)
        {
            _lingerAfterImpact -= dt;
            if (_lingerAfterImpact <= 0f)
                IsAlive = false;
            return;
        }

        PreviousPosition = Position;
        PreviousVelocity = Velocity;
        PreviousAgeSeconds = AgeSeconds;
        PreviousDistanceTraveledMeters = DistanceTraveledMeters;

        // Runge-Kutta 4 для системы:
        // dx/dt = v
        // dv/dt = g + aerodynamicDrag(v - localWind(position, time))
        //
        // Ветер семплируется заново на каждом подшаге RK4, поэтому порыв
        // или смена ветра по дистанции реально меняют траекторию пули.
        Vector3 p0 = Position;
        Vector3 v0 = Velocity;

        Vector3 a1 = Acceleration(p0, v0, stepStartTime, Profile, wind, windLimitMps);
        Vector3 k1P = v0;
        Vector3 k1V = a1;

        Vector3 p2 = p0 + k1P * (dt * 0.5f);
        Vector3 v2 = v0 + k1V * (dt * 0.5f);
        Vector3 k2P = v2;
        Vector3 k2V = Acceleration(p2, v2, stepStartTime + dt * 0.5f, Profile, wind, windLimitMps);

        Vector3 p3 = p0 + k2P * (dt * 0.5f);
        Vector3 v3 = v0 + k2V * (dt * 0.5f);
        Vector3 k3P = v3;
        Vector3 k3V = Acceleration(p3, v3, stepStartTime + dt * 0.5f, Profile, wind, windLimitMps);

        Vector3 p4 = p0 + k3P * dt;
        Vector3 v4 = v0 + k3V * dt;
        Vector3 k4P = v4;
        Vector3 k4V = Acceleration(p4, v4, stepStartTime + dt, Profile, wind, windLimitMps);

        Position = p0 + (k1P + 2f * k2P + 2f * k3P + k4P) * (dt / 6f);
        Velocity = v0 + (k1V + 2f * k2V + 2f * k3V + k4V) * (dt / 6f);

        // Spin drift считается как накопленная поправка по времени полёта.
        // Мы добавляем только приращение между предыдущим и новым моментом,
        // поэтому оно не зависит от FPS и не дублируется на следующем шаге.
        float nextAgeSeconds = AgeSeconds + dt;
        float targetSpinDriftMeters = SpinDriftModel.CumulativeDriftMeters(
            Profile.GyroscopicStabilityFactor,
            nextAgeSeconds,
            Profile.RiflingTwistDirection);
        float deltaSpinDriftMeters = targetSpinDriftMeters - _appliedSpinDriftMeters;
        if (MathF.Abs(deltaSpinDriftMeters) > 0.0000001f)
        {
            Position += _spinDriftRight * deltaSpinDriftMeters;
            _appliedSpinDriftMeters = targetSpinDriftMeters;
        }

        DistanceTraveledMeters += (Position - PreviousPosition).Length;
        AgeSeconds = nextAgeSeconds;

        _trailTimer += dt;
        if (_trailTimer >= TrailSampleInterval)
        {
            _trailTimer -= TrailSampleInterval;
            AddTrailPoint(Position);
        }

        // Защита от вечных пуль, улетевших далеко от полигона. Калибровочный
        // расчёт ZERO может временно отключить эти границы, чтобы найти высокую
        // навесную траекторию для медленного боеприпаса.
        if (!ignoreLifetimeLimits &&
            (AgeSeconds > 20f || DistanceTraveledMeters > 3500f || Position.Y < -100f))
            IsAlive = false;
    }


    /// <summary>
    /// Возвращает состояние пули к точному моменту столкновения внутри
    /// последнего физического отрезка. Используется continuous collision
    /// detection: hitT=0 соответствует PreviousPosition, hitT=1 — Position.
    /// Скорость и время интерполируются на тот же момент, поэтому энергия
    /// удара не берётся из конца 1-мс шага после поверхности.
    /// </summary>
    public void MoveToSegmentFraction(float hitT)
    {
        if (HasImpacted)
            return;

        float t = MathHelper.Clamp(hitT, 0f, 1f);
        Position = Vector3.Lerp(PreviousPosition, Position, t);
        Velocity = Vector3.Lerp(PreviousVelocity, Velocity, t);
        AgeSeconds = PreviousAgeSeconds + (AgeSeconds - PreviousAgeSeconds) * t;
        DistanceTraveledMeters = PreviousDistanceTraveledMeters
            + (DistanceTraveledMeters - PreviousDistanceTraveledMeters) * t;
    }


    /// <summary>
    /// Продолжает полёт после пробития материала с остаточной энергией.
    /// Направление пока сохраняется; отклонение/рикошет будет отдельной моделью.
    /// </summary>
    public void ContinueAfterPenetration(Vector3 exitDirection, float residualSpeedMps)
    {
        if (HasImpacted || !IsAlive)
            return;

        Vector3 direction = exitDirection.LengthSquared > 0.000001f
            ? Vector3.Normalize(exitDirection)
            : (Velocity.LengthSquared > 0.000001f ? Vector3.Normalize(Velocity) : -Vector3.UnitZ);

        float speed = MathF.Max(0f, residualSpeedMps);
        Velocity = direction * speed;
        PreviousVelocity = Velocity;

        // Малый сдвиг вперёд исключает повторное касание той же граничной
        // плоскости из-за погрешности float. Он значительно меньше любого вокселя.
        float epsilon = MathF.Max(0.0005f, Profile.DiameterMeters * 0.05f);
        Position += direction * epsilon;
        PreviousPosition = Position;
        AddTrailPoint(Position);
    }

    public void Impact(Vector3 impactPosition)
    {
        if (HasImpacted)
            return;

        Position = impactPosition;
        PreviousPosition = impactPosition;
        AddTrailPoint(impactPosition);
        HasImpacted = true;
        _lingerAfterImpact = 0.18f;
    }

    private static Vector3 Acceleration(
        Vector3 position,
        Vector3 velocity,
        float timeSeconds,
        BallisticProfile profile,
        WindField wind,
        float windLimitMps)
    {
        Vector3 gravity = new(0f, -profile.GravityMps2, 0f);

        // Аэродинамика зависит от скорости пули ОТНОСИТЕЛЬНО локального воздуха.
        // Именно поэтому боковой ветер постепенно уводит пулю в сторону.
        // Повторная защита на границе физики: баллистика никогда не получает
        // ветер выше активного лимита, даже если генератор позже изменится.
        Vector3 localWind = WindMath.ClampVelocity(
            wind.Sample(position, timeSeconds, windLimitMps),
            windLimitMps);
        Vector3 relativeVelocity = velocity - localWind;
        float relativeSpeed = relativeVelocity.Length;

        if (relativeSpeed < 0.0001f)
            return gravity;

        // Mach-dependent G1/G7 drag. Reference Cd берётся из стандартной
        // таблицы по числу Маха, затем масштабируется form factor i = SD / BC.
        // Благодаря этому сопротивление резко меняется в трансзвуковой области
        // вместо одного постоянного Cd на всей траектории.
        float effectiveCd = profile.EffectiveDragCoefficient(relativeSpeed);

        // Fd = 1/2 * rho * Cd(Mach) * A * v^2
        // В векторной форме ускорение = -k * |v| * v.
        float dragFactor = 0.5f
            * profile.AirDensityKgPerM3
            * effectiveCd
            * profile.CrossSectionAreaM2
            / profile.MassKg;

        Vector3 dragAcceleration = -dragFactor * relativeSpeed * relativeVelocity;
        return gravity + dragAcceleration;
    }

    private void AddTrailPoint(Vector3 point)
    {
        if (_trail.Count == MaxTrailPoints)
            _trail.RemoveAt(0);
        _trail.Add(point);
    }
}
