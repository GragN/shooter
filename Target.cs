using OpenTK.Mathematics;

namespace Sniper3D;

/// <summary>
/// Неподвижный объёмный бетонный куб 1x1x1 м.
/// Основная масса куба хранится в 3D-сетке вокселей. При попадании материал
/// не исчезает бесследно: разрушенные ячейки превращаются в физические
/// обломки с массой, импульсом, вращением, гравитацией и столкновениями.
/// </summary>
internal sealed class Target
{
    private const int Resolution = 12;
    private const float CubeSizeMeters = 1.0f;
    private const int MaxFragments = 480;
    private const float DebrisPhysicsStepSeconds = 1f / 120f;
    private const float GravityMps2 = 9.80665f;

    private readonly bool[,,] _intact = new bool[Resolution, Resolution, Resolution];
    private readonly float[,,] _damage = new float[Resolution, Resolution, Resolution];
    private readonly List<ConcreteFragment> _fragments = new();
    private readonly Random _random = new(481516);
    private float _debrisAccumulator;

    public Vector3 Position { get; private set; } = new(0f, 2.2f, -995f);
    public float DistanceMeters { get; private set; } = 1000f;

    public float SizeMeters => CubeSizeMeters;
    public float HalfSizeMeters => CubeSizeMeters * 0.5f;
    public float CellSizeMeters => CubeSizeMeters / Resolution;
    public int GridResolution => Resolution;
    public int TotalCellCount => Resolution * Resolution * Resolution;
    public int IntactCellCount { get; private set; }
    public float Integrity01
    {
        get
        {
            float effective = 0f;
            for (int x = 0; x < Resolution; x++)
            for (int y = 0; y < Resolution; y++)
            for (int z = 0; z < Resolution; z++)
            {
                if (_intact[x, y, z])
                    effective += 1f - 0.45f * MathHelper.Clamp(_damage[x, y, z], 0f, 1f);
            }
            return effective / TotalCellCount;
        }
    }
    public IReadOnlyList<ConcreteFragment> Fragments => _fragments;
    public MaterialProfile Material => MaterialProfile.NormalConcrete;

    public Target()
    {
        ResetDamage();
    }

    public void SetDistance(float distanceMeters, float shooterZ)
    {
        float newDistance = Math.Clamp(distanceMeters, 100f, 2000f);
        Vector3 newPosition = new(Position.X, Position.Y, shooterZ - newDistance);
        bool moved = MathF.Abs(newDistance - DistanceMeters) > 0.001f
            || (newPosition - Position).LengthSquared > 0.000001f;

        DistanceMeters = newDistance;
        Position = newPosition;

        // При ручном переносе дистанции создаём свежий образец бетона.
        // Простое изменение оружия/ветра при той же дистанции куб не чинит.
        if (moved)
            ResetDamage();
    }

    public void ResetDamage()
    {
        for (int x = 0; x < Resolution; x++)
        for (int y = 0; y < Resolution; y++)
        for (int z = 0; z < Resolution; z++)
            _intact[x, y, z] = true;

        Array.Clear(_damage, 0, _damage.Length);
        IntactCellCount = TotalCellCount;
        _fragments.Clear();
        _debrisAccumulator = 0f;
    }

    public bool IsCellIntact(int x, int y, int z)
        => _intact[x, y, z];

    public float GetCellDamage01(int x, int y, int z)
        => MathHelper.Clamp(_damage[x, y, z], 0f, 1f);

    public Vector3 GetCellWorldPosition(int x, int y, int z)
    {
        float cell = CellSizeMeters;
        float min = -HalfSizeMeters + cell * 0.5f;

        return Position + new Vector3(
            min + x * cell,
            min + y * cell,
            min + z * cell);
    }

    /// <summary>
    /// Непрерывно ищет первую целую воксельную ячейку вдоль всего отрезка
    /// полёта. Возвращает также hitT 0..1 — точный момент контакта внутри шага.
    /// </summary>
    public bool TryHitSegment(Vector3 from, Vector3 to, out float hitT, out Vector3 hitPoint)
    {
        Vector3 gridMin = Position - new Vector3(HalfSizeMeters);
        return VoxelSegmentCaster.TryHit(
            _intact,
            gridMin,
            CellSizeMeters,
            from,
            to,
            out hitT,
            out hitPoint);
    }

    public bool TryHitSegment(Vector3 from, Vector3 to, out Vector3 hitPoint)
        => TryHitSegment(from, to, out _, out hitPoint);

    /// <summary>
    /// Терминальная баллистика бетона. В отличие от старой энергетической
    /// эвристики, разрушение зависит от калибра/площади пули, массы, скорости,
    /// импульса и физических параметров бетона. Повреждение накапливается:
    /// слабое попадание не обязано мгновенно удалить целый воксель.
    /// </summary>
    public ConcreteImpactResult ApplyImpact(
        Vector3 hitPoint,
        ProjectileImpactData projectile)
    {
        if (IntactCellCount <= 0)
            return default;

        Vector3 direction = projectile.Direction.LengthSquared > 0.000001f
            ? Vector3.Normalize(projectile.Direction)
            : -Vector3.UnitZ;

        float solidThickness = EstimateContiguousSolidThickness(hitPoint, direction);
        ConcreteImpactSolution solution = ConcreteImpactModel.Solve(
            Material,
            projectile,
            solidThickness,
            CellSizeMeters);

        float damageDepth = MathF.Max(
            projectile.DiameterMeters * 1.5f,
            MathF.Min(
                solidThickness > 0f ? solidThickness : solution.BallisticPenetrationDepthMeters,
                solution.Perforated
                    ? solidThickness
                    : solution.BallisticPenetrationDepthMeters * 1.20f));

        float cellPadding = CellSizeMeters * 0.58f;
        var destroyedCells = new List<CellIndex>();

        for (int x = 0; x < Resolution; x++)
        for (int y = 0; y < Resolution; y++)
        for (int z = 0; z < Resolution; z++)
        {
            if (!_intact[x, y, z])
                continue;

            Vector3 center = GetCellWorldPosition(x, y, z);
            Vector3 delta = center - hitPoint;
            float axial = Vector3.Dot(delta, direction);

            if (axial < -cellPadding || axial > damageDepth + cellPadding)
                continue;

            float depth01 = damageDepth > 0.0001f
                ? MathHelper.Clamp(axial / damageDepth, 0f, 1f)
                : 0f;

            float endRadius = solution.Perforated
                ? solution.ExitSpallRadiusMeters
                : solution.DamageRadiusMeters * 0.72f;
            float localRadius = solution.DamageRadiusMeters
                + (endRadius - solution.DamageRadiusMeters) * depth01;

            Vector3 radial = delta - direction * axial;
            float radialDistance = radial.Length;
            float radiusWithVoxel = localRadius + cellPadding;
            if (radialDistance > radiusWithVoxel)
                continue;

            float radialWeight = 1f - MathHelper.Clamp(
                radialDistance / MathF.Max(radiusWithVoxel, 0.001f),
                0f,
                1f);
            radialWeight = 0.18f + 0.82f * radialWeight * radialWeight;

            float axialWeight = 1f - 0.30f * depth01;
            float increment = solution.CellDamageScale
                * 0.92f
                * radialWeight
                * axialWeight;

            // При физическом сквозном пробитии центральный канал обязан
            // открыться, иначе воксельная дискретизация могла бы оставить
            // невидимую "пробку" на пути уже прошедшей пули.
            bool centralPerforationChannel = solution.Perforated
                && radialDistance <= MathF.Max(projectile.DiameterMeters * 1.35f, CellSizeMeters * 0.75f)
                && axial >= -cellPadding * 0.25f
                && axial <= solidThickness + cellPadding * 0.25f;

            if (centralPerforationChannel)
                increment = MathF.Max(increment, 1.05f);

            _damage[x, y, z] = MathHelper.Clamp(_damage[x, y, z] + increment, 0f, 1.25f);

            if (_damage[x, y, z] >= 1f)
                destroyedCells.Add(new CellIndex(x, y, z));
        }

        foreach (CellIndex cell in destroyedCells)
            RemoveCell(cell);

        List<CellIndex> unsupportedCells = destroyedCells.Count > 0
            ? DetachUnsupportedCells()
            : new List<CellIndex>();

        int fragmentsBefore = _fragments.Count;

        if (destroyedCells.Count > 0)
        {
            SpawnImpactFragments(
                destroyedCells,
                hitPoint,
                direction,
                solution.DepositedEnergyJoules,
                projectile.MassKg,
                highEnergyEjection: true);
        }
        else
        {
            // Даже если целый воксель ещё не разрушен, бетон даёт мелкую крошку.
            SpawnSurfaceChips(hitPoint, direction, solution, projectile);
        }

        SpawnImpactFragments(
            unsupportedCells,
            hitPoint,
            direction,
            solution.DepositedEnergyJoules * 0.08f,
            projectile.MassKg,
            highEnergyEjection: false);

        TrimFragmentBudget();

        return new ConcreteImpactResult(
            destroyedCells.Count + unsupportedCells.Count,
            destroyedCells.Count,
            unsupportedCells.Count,
            Math.Max(0, _fragments.Count - fragmentsBefore),
            solution.Perforated,
            solution.ResidualEnergyJoules,
            solution.ResidualSpeedMps,
            solution.BallisticPenetrationDepthMeters,
            solution.CraterRadiusMeters,
            solution.DepositedEnergyJoules);
    }

    /// <summary>
    /// Обновляет физику отделившихся кусков фиксированным шагом 120 Гц.
    /// Основной куб остаётся закреплённым, а обломки получают полноценное
    /// свободное движение, вращение, падение и простые столкновения.
    /// </summary>
    public void UpdatePhysics(float frameDt, float groundY, float backstopZ)
    {
        _debrisAccumulator += MathF.Min(frameDt, 0.05f);

        int safety = 0;
        while (_debrisAccumulator >= DebrisPhysicsStepSeconds && safety < 10)
        {
            StepDebrisPhysics(DebrisPhysicsStepSeconds, groundY, backstopZ);
            _debrisAccumulator -= DebrisPhysicsStepSeconds;
            safety++;
        }

        if (safety == 10)
            _debrisAccumulator = 0f;
    }

    private void StepDebrisPhysics(float dt, float groundY, float backstopZ)
    {
        float linearDamping = MathF.Exp(-0.11f * dt);
        float angularDamping = MathF.Exp(-0.30f * dt);

        foreach (ConcreteFragment fragment in _fragments)
        {
            fragment.AgeSeconds += dt;
            if (fragment.Sleeping)
                continue;

            fragment.Velocity.Y -= GravityMps2 * dt;
            fragment.Velocity *= linearDamping;
            fragment.AngularVelocityRadPerSec *= angularDamping;

            fragment.Position += fragment.Velocity * dt;
            fragment.RotationRadians += fragment.AngularVelocityRadPerSec * dt;

            bool contacted = false;
            contacted |= ResolveFragmentAgainstConcrete(fragment);
            contacted |= ResolveFragmentAgainstGround(fragment, groundY);
            contacted |= ResolveFragmentAgainstBackstop(fragment, backstopZ);

            if (contacted
                && fragment.Velocity.LengthSquared < 0.025f
                && fragment.AngularVelocityRadPerSec.LengthSquared < 0.20f)
            {
                fragment.SleepTimerSeconds += dt;
                if (fragment.SleepTimerSeconds > 0.65f)
                {
                    fragment.Sleeping = true;
                    fragment.Velocity = Vector3.Zero;
                    fragment.AngularVelocityRadPerSec = Vector3.Zero;
                }
            }
            else
            {
                fragment.SleepTimerSeconds = 0f;
            }
        }

        // Для небольшого числа активных обломков добавляем столкновения между ними.
        // При большом количестве отключаем этот O(n^2) этап ради стабильного FPS.
        if (_fragments.Count <= 140)
            ResolveFragmentPairs();
    }


    private float EstimateContiguousSolidThickness(Vector3 hitPoint, Vector3 direction)
    {
        float step = MathF.Max(0.0025f, CellSizeMeters * 0.08f);
        float maxDistance = CubeSizeMeters * 1.5f;
        float thickness = 0f;
        bool enteredSolid = false;
        float emptyRun = 0f;

        for (float d = step * 0.25f; d <= maxDistance; d += step)
        {
            bool solid = IsPointInsideIntactConcrete(hitPoint + direction * d);
            if (solid)
            {
                enteredSolid = true;
                emptyRun = 0f;
                thickness += step;
            }
            else if (enteredSolid)
            {
                emptyRun += step;
                if (emptyRun >= step * 1.5f)
                    break;
            }
        }

        return thickness;
    }

    private bool IsPointInsideIntactConcrete(Vector3 point)
    {
        Vector3 min = Position - new Vector3(HalfSizeMeters);
        Vector3 local = point - min;
        if (local.X < 0f || local.Y < 0f || local.Z < 0f
            || local.X >= CubeSizeMeters || local.Y >= CubeSizeMeters || local.Z >= CubeSizeMeters)
            return false;

        int x = Math.Clamp((int)(local.X / CellSizeMeters), 0, Resolution - 1);
        int y = Math.Clamp((int)(local.Y / CellSizeMeters), 0, Resolution - 1);
        int z = Math.Clamp((int)(local.Z / CellSizeMeters), 0, Resolution - 1);
        return _intact[x, y, z];
    }

    private void SpawnSurfaceChips(
        Vector3 hitPoint,
        Vector3 incomingDirection,
        ConcreteImpactSolution solution,
        ProjectileImpactData projectile)
    {
        int count = Math.Clamp(
            1 + (int)(solution.DepositedEnergyJoules / 700f),
            1,
            projectile.DiameterMeters >= 0.012f ? 8 : 5);

        float chipBudget = solution.DepositedEnergyJoules * 0.035f;
        float energyPerChip = chipBudget / Math.Max(1, count);

        for (int i = 0; i < count && _fragments.Count < MaxFragments; i++)
        {
            float baseSize = MathHelper.Clamp(
                projectile.DiameterMeters * RandomRange(1.1f, 2.6f),
                0.008f,
                MathF.Min(0.045f, CellSizeMeters * 0.42f));

            Vector3 size = new(
                baseSize * RandomRange(0.65f, 1.25f),
                baseSize * RandomRange(0.55f, 1.15f),
                baseSize * RandomRange(0.65f, 1.30f));

            float mass = MathF.Max(0.002f, size.X * size.Y * size.Z * Material.DensityKgPerM3);
            float speed = MathHelper.Clamp(
                MathF.Sqrt(MathF.Max(0f, 2f * energyPerChip / mass)),
                0.25f,
                18f);

            Vector3 backwards = -incomingDirection;
            Vector3 direction = backwards * RandomRange(0.45f, 0.85f)
                + RandomUnitVector() * RandomRange(0.35f, 0.85f)
                + Vector3.UnitY * RandomRange(0.02f, 0.25f);
            direction = direction.LengthSquared > 0.000001f
                ? Vector3.Normalize(direction)
                : Vector3.UnitY;

            float shade = RandomRange(0.33f, 0.45f);
            _fragments.Add(new ConcreteFragment(
                hitPoint + direction * (baseSize * 0.4f),
                direction * speed,
                RandomUnitVector() * RandomRange(-0.7f, 0.7f),
                RandomUnitVector() * RandomRange(3f, 16f),
                size,
                new Vector3(shade, shade + 0.006f, shade + 0.012f),
                mass));
        }
    }

    private void SpawnImpactFragments(
        List<CellIndex> cells,
        Vector3 hitPoint,
        Vector3 incomingDirection,
        float energyBudgetJoules,
        float projectileMassKg,
        bool highEnergyEjection)
    {
        if (cells.Count == 0)
            return;

        float energyTransferFraction = highEnergyEjection ? Material.FragmentEnergyFraction : Material.FragmentEnergyFraction * 0.23f;
        float transferredEnergy = MathF.Max(0f, energyBudgetJoules) * energyTransferFraction;
        float energyPerFragment = transferredEnergy / Math.Max(1, cells.Count);

        // Импульс пули добавляет направленную составляющую независимо от энергии.
        float projectileSpeed = energyBudgetJoules > 0f && projectileMassKg > 0.000001f
            ? MathF.Sqrt(2f * energyBudgetJoules / projectileMassKg)
            : 0f;
        float projectileMomentum = projectileMassKg * projectileSpeed;

        foreach (CellIndex cell in cells)
        {
            Vector3 center = GetCellWorldPosition(cell.X, cell.Y, cell.Z);

            float sx = CellSizeMeters * RandomRange(0.72f, 0.96f);
            float sy = CellSizeMeters * RandomRange(0.70f, 0.98f);
            float sz = CellSizeMeters * RandomRange(0.72f, 0.96f);
            Vector3 size = new(sx, sy, sz);

            float volume = sx * sy * sz;
            float mass = MathF.Max(0.08f, volume * Material.DensityKgPerM3);

            float speedFromEnergy = MathF.Sqrt(MathF.Max(0f, 2f * energyPerFragment / mass));
            float speedCap = highEnergyEjection ? 14f : 3.2f;
            float baseSpeed = MathHelper.Clamp(speedFromEnergy, highEnergyEjection ? 0.45f : 0.12f, speedCap);

            Vector3 offset = center - hitPoint;
            float axial = Vector3.Dot(offset, incomingDirection);
            Vector3 radial = offset - incomingDirection * axial;
            Vector3 radialDir = radial.LengthSquared > 0.000001f
                ? Vector3.Normalize(radial)
                : RandomUnitVector();

            Vector3 jitter = RandomUnitVector();
            Vector3 ejectionDirection;
            if (highEnergyEjection)
            {
                ejectionDirection = incomingDirection * 0.72f
                    + radialDir * RandomRange(0.38f, 0.88f)
                    + Vector3.UnitY * RandomRange(0.03f, 0.24f)
                    + jitter * 0.20f;
            }
            else
            {
                // Отделившийся из-за потери опоры кусок в основном просто осыпается.
                ejectionDirection = incomingDirection * 0.12f
                    + radialDir * 0.16f
                    + Vector3.UnitY * RandomRange(0.02f, 0.12f)
                    + jitter * 0.18f;
            }

            if (ejectionDirection.LengthSquared < 0.000001f)
                ejectionDirection = Vector3.UnitY;
            else
                ejectionDirection = Vector3.Normalize(ejectionDirection);

            float momentumKick = highEnergyEjection
                ? MathHelper.Clamp(projectileMomentum / MathF.Max(0.1f, mass * cells.Count), 0f, 2.5f)
                : 0f;

            Vector3 velocity = ejectionDirection * (baseSpeed + momentumKick);

            Vector3 rotation = new(
                RandomRange(-MathF.PI, MathF.PI),
                RandomRange(-MathF.PI, MathF.PI),
                RandomRange(-MathF.PI, MathF.PI));

            float spin = highEnergyEjection ? RandomRange(3.0f, 13.0f) : RandomRange(0.8f, 5.0f);
            Vector3 angularVelocity = RandomUnitVector() * spin;

            float shade = RandomRange(0.34f, 0.46f);
            Vector3 color = new(shade, shade + 0.007f, shade + 0.013f);

            _fragments.Add(new ConcreteFragment(
                center,
                velocity,
                rotation,
                angularVelocity,
                size,
                color,
                mass));
        }
    }

    /// <summary>
    /// Проверяет структурную связность по 6 соседям. Всё, что больше не связано
    /// с нижним слоем куба, считается потерявшим опору и отделяется.
    /// </summary>
    private List<CellIndex> DetachUnsupportedCells()
    {
        var supported = new bool[Resolution, Resolution, Resolution];
        var queue = new Queue<CellIndex>();

        // Нижний слой играет роль области опоры/связи с основанием.
        for (int x = 0; x < Resolution; x++)
        for (int z = 0; z < Resolution; z++)
        {
            if (!_intact[x, 0, z])
                continue;

            supported[x, 0, z] = true;
            queue.Enqueue(new CellIndex(x, 0, z));
        }

        var neighbours = new (int X, int Y, int Z)[]
        {
            (1, 0, 0), (-1, 0, 0),
            (0, 1, 0), (0, -1, 0),
            (0, 0, 1), (0, 0, -1)
        };

        while (queue.Count > 0)
        {
            CellIndex c = queue.Dequeue();
            foreach ((int dx, int dy, int dz) in neighbours)
            {
                int nx = c.X + dx;
                int ny = c.Y + dy;
                int nz = c.Z + dz;

                if (nx < 0 || nx >= Resolution
                    || ny < 0 || ny >= Resolution
                    || nz < 0 || nz >= Resolution)
                {
                    continue;
                }

                if (!_intact[nx, ny, nz] || supported[nx, ny, nz])
                    continue;

                supported[nx, ny, nz] = true;
                queue.Enqueue(new CellIndex(nx, ny, nz));
            }
        }

        var detached = new List<CellIndex>();
        for (int x = 0; x < Resolution; x++)
        for (int y = 0; y < Resolution; y++)
        for (int z = 0; z < Resolution; z++)
        {
            if (!_intact[x, y, z] || supported[x, y, z])
                continue;

            var cell = new CellIndex(x, y, z);
            RemoveCell(cell);
            detached.Add(cell);
        }

        return detached;
    }

    private bool ResolveFragmentAgainstGround(ConcreteFragment fragment, float groundY)
    {
        float radius = fragment.CollisionRadiusMeters;
        float penetration = groundY - (fragment.Position.Y - radius);
        if (penetration <= 0f)
            return false;

        fragment.Position.Y += penetration;
        if (fragment.Velocity.Y < 0f)
            fragment.Velocity.Y = -fragment.Velocity.Y * Material.Restitution;

        float groundTangential = MathHelper.Clamp(1f - Material.Friction * 0.45f, 0.20f, 0.95f);
        fragment.Velocity.X *= groundTangential;
        fragment.Velocity.Z *= groundTangential;
        fragment.AngularVelocityRadPerSec *= 0.70f;
        return true;
    }

    private bool ResolveFragmentAgainstBackstop(ConcreteFragment fragment, float backstopZ)
    {
        float radius = fragment.CollisionRadiusMeters;
        float penetration = backstopZ - (fragment.Position.Z - radius);
        if (penetration <= 0f)
            return false;

        fragment.Position.Z += penetration;
        if (fragment.Velocity.Z < 0f)
            fragment.Velocity.Z = -fragment.Velocity.Z * 0.10f;

        fragment.Velocity.X *= 0.78f;
        fragment.Velocity.Y *= 0.78f;
        fragment.AngularVelocityRadPerSec *= 0.76f;
        return true;
    }

    private bool ResolveFragmentAgainstConcrete(ConcreteFragment fragment)
    {
        float radius = fragment.CollisionRadiusMeters;
        Vector3 cubeHalf = new(HalfSizeMeters + radius);
        Vector3 d = fragment.Position - Position;
        if (MathF.Abs(d.X) > cubeHalf.X
            || MathF.Abs(d.Y) > cubeHalf.Y
            || MathF.Abs(d.Z) > cubeHalf.Z)
        {
            return false;
        }

        float minWorldX = Position.X - HalfSizeMeters;
        float minWorldY = Position.Y - HalfSizeMeters;
        float minWorldZ = Position.Z - HalfSizeMeters;

        int cx = (int)MathF.Floor((fragment.Position.X - minWorldX) / CellSizeMeters);
        int cy = (int)MathF.Floor((fragment.Position.Y - minWorldY) / CellSizeMeters);
        int cz = (int)MathF.Floor((fragment.Position.Z - minWorldZ) / CellSizeMeters);

        bool collided = false;
        float halfCell = CellSizeMeters * 0.5f;
        Vector3 half = new(halfCell);

        for (int x = Math.Max(0, cx - 2); x <= Math.Min(Resolution - 1, cx + 2); x++)
        for (int y = Math.Max(0, cy - 2); y <= Math.Min(Resolution - 1, cy + 2); y++)
        for (int z = Math.Max(0, cz - 2); z <= Math.Min(Resolution - 1, cz + 2); z++)
        {
            if (!_intact[x, y, z])
                continue;

            Vector3 center = GetCellWorldPosition(x, y, z);
            Vector3 min = center - half;
            Vector3 max = center + half;

            if (!SphereAabbPenetration(fragment.Position, radius, min, max, out Vector3 normal, out float penetration))
                continue;

            fragment.Position += normal * penetration;

            float vn = Vector3.Dot(fragment.Velocity, normal);
            if (vn < 0f)
                fragment.Velocity -= normal * (1.10f * vn);

            // Потеря энергии при ударе бетона о бетон.
            fragment.Velocity *= 0.87f;
            fragment.AngularVelocityRadPerSec *= 0.86f;
            fragment.Wake();
            collided = true;
        }

        return collided;
    }

    private void ResolveFragmentPairs()
    {
        for (int i = 0; i < _fragments.Count; i++)
        {
            ConcreteFragment a = _fragments[i];
            for (int j = i + 1; j < _fragments.Count; j++)
            {
                ConcreteFragment b = _fragments[j];
                if (a.Sleeping && b.Sleeping)
                    continue;

                float ra = a.CollisionRadiusMeters;
                float rb = b.CollisionRadiusMeters;
                Vector3 delta = b.Position - a.Position;
                float minDistance = ra + rb;
                float distanceSquared = delta.LengthSquared;
                if (distanceSquared >= minDistance * minDistance)
                    continue;

                float distance = MathF.Sqrt(MathF.Max(distanceSquared, 0.0000001f));
                Vector3 normal = distance > 0.0001f ? delta / distance : Vector3.UnitY;
                float penetration = minDistance - distance;

                float invMassA = 1f / a.MassKg;
                float invMassB = 1f / b.MassKg;
                float invMassSum = invMassA + invMassB;

                a.Position -= normal * penetration * (invMassA / invMassSum);
                b.Position += normal * penetration * (invMassB / invMassSum);

                Vector3 relativeVelocity = b.Velocity - a.Velocity;
                float relativeNormalSpeed = Vector3.Dot(relativeVelocity, normal);
                if (relativeNormalSpeed >= 0f)
                    continue;

                const float restitution = 0.10f;
                float impulseMagnitude = -(1f + restitution) * relativeNormalSpeed / invMassSum;
                Vector3 impulse = normal * impulseMagnitude;

                a.Velocity -= impulse * invMassA;
                b.Velocity += impulse * invMassB;
                a.AngularVelocityRadPerSec *= 0.92f;
                b.AngularVelocityRadPerSec *= 0.92f;
                a.Wake();
                b.Wake();
            }
        }
    }

    private static bool SphereAabbPenetration(
        Vector3 sphereCenter,
        float radius,
        Vector3 min,
        Vector3 max,
        out Vector3 normal,
        out float penetration)
    {
        Vector3 closest = new(
            MathHelper.Clamp(sphereCenter.X, min.X, max.X),
            MathHelper.Clamp(sphereCenter.Y, min.Y, max.Y),
            MathHelper.Clamp(sphereCenter.Z, min.Z, max.Z));

        Vector3 delta = sphereCenter - closest;
        float distanceSquared = delta.LengthSquared;
        if (distanceSquared > radius * radius)
        {
            normal = Vector3.Zero;
            penetration = 0f;
            return false;
        }

        if (distanceSquared > 0.0000001f)
        {
            float distance = MathF.Sqrt(distanceSquared);
            normal = delta / distance;
            penetration = radius - distance;
            return penetration > 0f;
        }

        // Центр сферы оказался внутри ячейки. Выбираем ближайшую грань.
        float left = sphereCenter.X - min.X;
        float right = max.X - sphereCenter.X;
        float bottom = sphereCenter.Y - min.Y;
        float top = max.Y - sphereCenter.Y;
        float back = sphereCenter.Z - min.Z;
        float front = max.Z - sphereCenter.Z;

        float nearest = left;
        normal = -Vector3.UnitX;

        if (right < nearest) { nearest = right; normal = Vector3.UnitX; }
        if (bottom < nearest) { nearest = bottom; normal = -Vector3.UnitY; }
        if (top < nearest) { nearest = top; normal = Vector3.UnitY; }
        if (back < nearest) { nearest = back; normal = -Vector3.UnitZ; }
        if (front < nearest) { nearest = front; normal = Vector3.UnitZ; }

        penetration = radius + nearest;
        return true;
    }

    private bool TryFindClosestIntactCell(Vector3 point, out CellIndex closest)
    {
        closest = default;
        float bestDistanceSquared = float.PositiveInfinity;
        bool found = false;

        for (int x = 0; x < Resolution; x++)
        for (int y = 0; y < Resolution; y++)
        for (int z = 0; z < Resolution; z++)
        {
            if (!_intact[x, y, z])
                continue;

            Vector3 delta = GetCellWorldPosition(x, y, z) - point;
            float distanceSquared = delta.LengthSquared;
            if (distanceSquared >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            closest = new CellIndex(x, y, z);
            found = true;
        }

        return found;
    }

    private void RemoveCell(CellIndex cell)
    {
        if (!_intact[cell.X, cell.Y, cell.Z])
            return;

        _intact[cell.X, cell.Y, cell.Z] = false;
        _damage[cell.X, cell.Y, cell.Z] = 1f;
        IntactCellCount = Math.Max(0, IntactCellCount - 1);
    }

    private void TrimFragmentBudget()
    {
        while (_fragments.Count > MaxFragments)
        {
            int index = _fragments.FindIndex(f => f.Sleeping);
            if (index < 0)
                index = 0;
            _fragments.RemoveAt(index);
        }
    }

    private float RandomRange(float min, float max)
        => min + (max - min) * (float)_random.NextDouble();

    private Vector3 RandomUnitVector()
    {
        for (int i = 0; i < 8; i++)
        {
            Vector3 v = new(
                RandomRange(-1f, 1f),
                RandomRange(-1f, 1f),
                RandomRange(-1f, 1f));

            float lengthSquared = v.LengthSquared;
            if (lengthSquared > 0.0001f && lengthSquared <= 1f)
                return v / MathF.Sqrt(lengthSquared);
        }

        return Vector3.UnitY;
    }

    private static bool TrySegmentAabb(
        Vector3 from,
        Vector3 to,
        Vector3 min,
        Vector3 max,
        out float enterT,
        out float exitT)
    {
        Vector3 delta = to - from;
        enterT = 0f;
        exitT = 1f;

        for (int axis = 0; axis < 3; axis++)
        {
            float origin = axis == 0 ? from.X : axis == 1 ? from.Y : from.Z;
            float direction = axis == 0 ? delta.X : axis == 1 ? delta.Y : delta.Z;
            float axisMin = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            float axisMax = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;

            if (MathF.Abs(direction) < 0.0000001f)
            {
                if (origin < axisMin || origin > axisMax)
                    return false;
                continue;
            }

            float inv = 1f / direction;
            float t1 = (axisMin - origin) * inv;
            float t2 = (axisMax - origin) * inv;

            if (t1 > t2)
                (t1, t2) = (t2, t1);

            enterT = MathF.Max(enterT, t1);
            exitT = MathF.Min(exitT, t2);

            if (enterT > exitT)
                return false;
        }

        return exitT >= 0f && enterT <= 1f;
    }

    private readonly record struct CellIndex(int X, int Y, int Z);
}

internal readonly record struct ConcreteImpactResult(
    int RemovedCellCount,
    int DirectlyDestroyedCellCount,
    int StructurallyDetachedCellCount,
    int SpawnedFragmentCount,
    bool Perforated,
    float ResidualEnergyJoules,
    float ResidualSpeedMps,
    float PenetrationDepthMeters,
    float CraterRadiusMeters,
    float DepositedEnergyJoules);
