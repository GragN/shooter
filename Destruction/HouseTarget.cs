using OpenTK.Mathematics;

namespace Sniper3D;

/// <summary>
/// Разрушаемый бетонный дом для второй сцены.
/// Дом построен из объёмной воксельной оболочки: стены, пол, плоская крыша
/// и внутренняя перегородка. Окна и дверь являются настоящими отверстиями.
/// При попадании ячейки отделяются и превращаются в физические обломки.
/// </summary>
internal sealed class HouseTarget
{
    private const int CellsX = 16;
    private const int CellsY = 8;
    private const int CellsZ = 12;
    private const float CellSize = 0.50f;
    private const int MaxFragments = 700;
    private const float DebrisPhysicsStepSeconds = 1f / 120f;
    private const float GravityMps2 = 9.80665f;

    private static readonly (int X, int Y, int Z)[] NeighbourOffsets =
    {
        (1, 0, 0), (-1, 0, 0),
        (0, 1, 0), (0, -1, 0),
        (0, 0, 1), (0, 0, -1)
    };

    private readonly bool[,,] _intact = new bool[CellsX, CellsY, CellsZ];
    private readonly float[,,] _damage = new float[CellsX, CellsY, CellsZ];
    private readonly List<ConcreteFragment> _fragments = new();
    private readonly Random _random = new(271828);
    private float _debrisAccumulator;
    private int _initialCellCount;

    public Vector3 Position { get; private set; } = new(0f, CellsY * CellSize * 0.5f, -998f);
    public float DistanceMeters { get; private set; } = 1000f;

    public float WidthMeters => CellsX * CellSize;
    public float HeightMeters => CellsY * CellSize;
    public float DepthMeters => CellsZ * CellSize;
    public float CellSizeMeters => CellSize;
    public int GridX => CellsX;
    public int GridY => CellsY;
    public int GridZ => CellsZ;
    public int IntactCellCount { get; private set; }
    public int InitialCellCount => _initialCellCount;
    public float Integrity01
    {
        get
        {
            if (_initialCellCount <= 0)
                return 0f;

            float effective = 0f;
            for (int x = 0; x < CellsX; x++)
            for (int y = 0; y < CellsY; y++)
            for (int z = 0; z < CellsZ; z++)
            {
                if (_intact[x, y, z])
                    effective += 1f - 0.45f * MathHelper.Clamp(_damage[x, y, z], 0f, 1f);
            }
            return effective / _initialCellCount;
        }
    }
    public IReadOnlyList<ConcreteFragment> Fragments => _fragments;
    public MaterialProfile Material => MaterialProfile.NormalConcrete;

    public Vector3 ObserverFocus => Position + new Vector3(0f, 0.15f, 0f);

    /// <summary>
    /// Ближняя к стрелку плоскость фасада. DistanceMeters относится именно к фасаду.
    /// </summary>
    public float FrontZ => Position.Z + DepthMeters * 0.5f;

    public HouseTarget()
    {
        ResetDamage();
    }

    public void SetDistance(float distanceMeters, float shooterZ)
    {
        float newDistance = Math.Clamp(distanceMeters, 100f, 2000f);
        float centerZ = shooterZ - newDistance - DepthMeters * 0.5f;
        Vector3 newPosition = new(0f, HeightMeters * 0.5f, centerZ);

        bool moved = MathF.Abs(newDistance - DistanceMeters) > 0.001f
            || (newPosition - Position).LengthSquared > 0.000001f;

        DistanceMeters = newDistance;
        Position = newPosition;

        if (moved)
            ResetDamage();
    }

    public void ResetDamage()
    {
        Array.Clear(_intact, 0, _intact.Length);
        Array.Clear(_damage, 0, _damage.Length);

        for (int x = 0; x < CellsX; x++)
        for (int y = 0; y < CellsY; y++)
        for (int z = 0; z < CellsZ; z++)
        {
            bool floor = y == 0;
            bool roof = y == CellsY - 1;
            bool leftRightWall = x == 0 || x == CellsX - 1;
            bool frontWall = z == CellsZ - 1;
            bool backWall = z == 0;
            bool innerWall = z == CellsZ / 2 && y > 0 && y < CellsY - 1;

            bool solid = floor || roof || leftRightWall || frontWall || backWall || innerWall;
            if (!solid)
                continue;

            // Дверь в центре переднего фасада: 1 м шириной, 2 м высотой.
            if (frontWall && x >= 7 && x <= 8 && y >= 1 && y <= 4)
                solid = false;

            // Два окна на переднем фасаде.
            bool frontWindowLeft = frontWall && x >= 3 && x <= 4 && y >= 3 && y <= 5;
            bool frontWindowRight = frontWall && x >= 11 && x <= 12 && y >= 3 && y <= 5;
            if (frontWindowLeft || frontWindowRight)
                solid = false;

            // Окна в боковых стенах.
            bool sideWindow = leftRightWall && z >= 4 && z <= 6 && y >= 3 && y <= 5;
            if (sideWindow)
                solid = false;

            // Проход во внутренней перегородке.
            if (innerWall && x >= 7 && x <= 8 && y >= 1 && y <= 4)
                solid = false;

            if (solid)
                _intact[x, y, z] = true;
        }

        _initialCellCount = CountIntactCells();
        IntactCellCount = _initialCellCount;
        _fragments.Clear();
        _debrisAccumulator = 0f;
    }

    public bool IsCellIntact(int x, int y, int z) => _intact[x, y, z];

    /// <summary>
    /// Returns true only for intact cells with at least one face exposed to an
    /// empty cell or the outside of the voxel grid.
    /// </summary>
    public bool IsCellExposed(int x, int y, int z)
    {
        if (!_intact[x, y, z])
            return false;

        return x == 0 || !_intact[x - 1, y, z]
            || x == CellsX - 1 || !_intact[x + 1, y, z]
            || y == 0 || !_intact[x, y - 1, z]
            || y == CellsY - 1 || !_intact[x, y + 1, z]
            || z == 0 || !_intact[x, y, z - 1]
            || z == CellsZ - 1 || !_intact[x, y, z + 1];
    }

    public float GetCellDamage01(int x, int y, int z)
        => MathHelper.Clamp(_damage[x, y, z], 0f, 1f);

    public Vector3 GetCellWorldPosition(int x, int y, int z)
    {
        float minX = -WidthMeters * 0.5f + CellSize * 0.5f;
        float minY = -HeightMeters * 0.5f + CellSize * 0.5f;
        float minZ = -DepthMeters * 0.5f + CellSize * 0.5f;

        return Position + new Vector3(
            minX + x * CellSize,
            minY + y * CellSize,
            minZ + z * CellSize);
    }

    /// <summary>
    /// Точный continuous collision traversal по вокселям дома. В отличие от
    /// прежней выборки точек вдоль сегмента, 3D DDA гарантированно посещает
    /// каждую пересечённую ячейку и не пропускает тонкую стену на высокой скорости.
    /// </summary>
    public bool TryHitSegment(Vector3 from, Vector3 to, out float hitT, out Vector3 hitPoint)
    {
        Vector3 half = new(WidthMeters * 0.5f, HeightMeters * 0.5f, DepthMeters * 0.5f);
        Vector3 gridMin = Position - half;
        return VoxelSegmentCaster.TryHit(
            _intact,
            gridMin,
            CellSize,
            from,
            to,
            out hitT,
            out hitPoint);
    }

    public bool TryHitSegment(Vector3 from, Vector3 to, out Vector3 hitPoint)
        => TryHitSegment(from, to, out _, out hitPoint);

    /// <summary>
    /// Материальная модель обычного бетона. Повреждение зависит от калибра,
    /// площади, массы, скорости и импульса пули и накапливается между попаданиями.
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
            CellSize);

        float damageDepth = MathF.Max(
            projectile.DiameterMeters * 1.5f,
            MathF.Min(
                solidThickness > 0f ? solidThickness : solution.BallisticPenetrationDepthMeters,
                solution.Perforated
                    ? solidThickness
                    : solution.BallisticPenetrationDepthMeters * 1.20f));

        float padding = CellSize * 0.58f;
        var destroyedCells = new List<CellIndex>();

        for (int x = 0; x < CellsX; x++)
        for (int y = 0; y < CellsY; y++)
        for (int z = 0; z < CellsZ; z++)
        {
            if (!_intact[x, y, z])
                continue;

            Vector3 center = GetCellWorldPosition(x, y, z);
            Vector3 delta = center - hitPoint;
            float axial = Vector3.Dot(delta, direction);
            if (axial < -padding || axial > damageDepth + padding)
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
            float radiusWithVoxel = localRadius + padding;
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

            bool centralPerforationChannel = solution.Perforated
                && radialDistance <= MathF.Max(projectile.DiameterMeters * 1.35f, CellSize * 0.75f)
                && axial >= -padding * 0.25f
                && axial <= solidThickness + padding * 0.25f;

            if (centralPerforationChannel)
                increment = MathF.Max(increment, 1.05f);

            _damage[x, y, z] = MathHelper.Clamp(_damage[x, y, z] + increment, 0f, 1.25f);
            if (_damage[x, y, z] >= 1f)
                destroyedCells.Add(new CellIndex(x, y, z));
        }

        foreach (CellIndex cell in destroyedCells)
            RemoveCell(cell);

        List<CellIndex> unsupported = destroyedCells.Count > 0
            ? DetachUnsupportedCells()
            : new List<CellIndex>();

        int fragmentsBefore = _fragments.Count;

        if (destroyedCells.Count > 0)
            SpawnFragments(destroyedCells, hitPoint, direction, solution.DepositedEnergyJoules, projectile.MassKg, true);
        else
            SpawnSurfaceChips(hitPoint, direction, solution, projectile);

        SpawnFragments(unsupported, hitPoint, direction, solution.DepositedEnergyJoules * 0.06f, projectile.MassKg, false);
        TrimFragmentBudget();

        return new ConcreteImpactResult(
            destroyedCells.Count + unsupported.Count,
            destroyedCells.Count,
            unsupported.Count,
            Math.Max(0, _fragments.Count - fragmentsBefore),
            solution.Perforated,
            solution.ResidualEnergyJoules,
            solution.ResidualSpeedMps,
            solution.BallisticPenetrationDepthMeters,
            solution.CraterRadiusMeters,
            solution.DepositedEnergyJoules);
    }

    public void UpdatePhysics(float frameDt, float groundY)
    {
        _debrisAccumulator += MathF.Min(frameDt, 0.05f);

        int safety = 0;
        while (_debrisAccumulator >= DebrisPhysicsStepSeconds && safety < 10)
        {
            StepDebrisPhysics(DebrisPhysicsStepSeconds, groundY);
            _debrisAccumulator -= DebrisPhysicsStepSeconds;
            safety++;
        }

        if (safety == 10)
            _debrisAccumulator = 0f;
    }

    private void StepDebrisPhysics(float dt, float groundY)
    {
        float linearDamping = MathF.Exp(-0.10f * dt);
        float angularDamping = MathF.Exp(-0.28f * dt);

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

            bool contacted = ResolveFragmentAgainstHouse(fragment);
            contacted |= ResolveFragmentAgainstGround(fragment, groundY);

            if (contacted
                && fragment.Velocity.LengthSquared < 0.035f
                && fragment.AngularVelocityRadPerSec.LengthSquared < 0.24f)
            {
                fragment.SleepTimerSeconds += dt;
                if (fragment.SleepTimerSeconds > 0.70f)
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
    }


    private float EstimateContiguousSolidThickness(Vector3 hitPoint, Vector3 direction)
    {
        float step = MathF.Max(0.004f, CellSize * 0.08f);
        float maxDistance = MathF.Max(WidthMeters, MathF.Max(HeightMeters, DepthMeters)) * 1.2f;
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
        Vector3 half = new(WidthMeters * 0.5f, HeightMeters * 0.5f, DepthMeters * 0.5f);
        Vector3 min = Position - half;
        Vector3 local = point - min;

        if (local.X < 0f || local.Y < 0f || local.Z < 0f
            || local.X >= WidthMeters || local.Y >= HeightMeters || local.Z >= DepthMeters)
            return false;

        int x = Math.Clamp((int)(local.X / CellSize), 0, CellsX - 1);
        int y = Math.Clamp((int)(local.Y / CellSize), 0, CellsY - 1);
        int z = Math.Clamp((int)(local.Z / CellSize), 0, CellsZ - 1);
        return _intact[x, y, z];
    }

    private void SpawnSurfaceChips(
        Vector3 hitPoint,
        Vector3 incomingDirection,
        ConcreteImpactSolution solution,
        ProjectileImpactData projectile)
    {
        int count = Math.Clamp(
            1 + (int)(solution.DepositedEnergyJoules / 850f),
            1,
            projectile.DiameterMeters >= 0.012f ? 10 : 6);

        float chipBudget = solution.DepositedEnergyJoules * 0.030f;
        float energyPerChip = chipBudget / Math.Max(1, count);

        for (int i = 0; i < count && _fragments.Count < MaxFragments; i++)
        {
            float baseSize = MathHelper.Clamp(
                projectile.DiameterMeters * RandomRange(1.2f, 3.0f),
                0.010f,
                MathF.Min(0.060f, CellSize * 0.30f));

            Vector3 size = new(
                baseSize * RandomRange(0.65f, 1.30f),
                baseSize * RandomRange(0.55f, 1.15f),
                baseSize * RandomRange(0.65f, 1.35f));

            float mass = MathF.Max(0.003f, size.X * size.Y * size.Z * Material.DensityKgPerM3);
            float speed = MathHelper.Clamp(
                MathF.Sqrt(MathF.Max(0f, 2f * energyPerChip / mass)),
                0.20f,
                16f);

            Vector3 direction = -incomingDirection * RandomRange(0.45f, 0.82f)
                + RandomUnitVector() * RandomRange(0.35f, 0.82f)
                + Vector3.UnitY * RandomRange(0.02f, 0.22f);
            direction = direction.LengthSquared > 0.000001f
                ? Vector3.Normalize(direction)
                : Vector3.UnitY;

            float shade = RandomRange(0.32f, 0.45f);
            _fragments.Add(new ConcreteFragment(
                hitPoint + direction * (baseSize * 0.4f),
                direction * speed,
                RandomUnitVector() * RandomRange(-0.7f, 0.7f),
                RandomUnitVector() * RandomRange(2.5f, 14f),
                size,
                new Vector3(shade, shade + 0.006f, shade + 0.012f),
                mass));
        }
    }

    private void SpawnFragments(
        List<CellIndex> cells,
        Vector3 hitPoint,
        Vector3 incomingDirection,
        float energyBudgetJoules,
        float projectileMassKg,
        bool highEnergy)
    {
        if (cells.Count == 0)
            return;

        int piecesPerCell = highEnergy ? 2 : 1;
        float transferredEnergy = MathF.Max(0f, energyBudgetJoules) * (highEnergy ? Material.FragmentEnergyFraction : Material.FragmentEnergyFraction * 0.22f);
        float energyPerPiece = transferredEnergy / Math.Max(1, cells.Count * piecesPerCell);

        float projectileSpeed = projectileMassKg > 0.000001f && energyBudgetJoules > 0f
            ? MathF.Sqrt(2f * energyBudgetJoules / projectileMassKg)
            : 0f;
        float projectileMomentum = projectileMassKg * projectileSpeed;

        foreach (CellIndex cell in cells)
        {
            Vector3 center = GetCellWorldPosition(cell.X, cell.Y, cell.Z);

            for (int piece = 0; piece < piecesPerCell; piece++)
            {
                if (_fragments.Count >= MaxFragments)
                    return;

                float sx = CellSize * RandomRange(0.34f, 0.68f);
                float sy = CellSize * RandomRange(0.30f, 0.66f);
                float sz = CellSize * RandomRange(0.34f, 0.70f);
                Vector3 size = new(sx, sy, sz);
                float mass = MathF.Max(0.20f, sx * sy * sz * Material.DensityKgPerM3);

                Vector3 radial = center - hitPoint;
                radial -= incomingDirection * Vector3.Dot(radial, incomingDirection);
                Vector3 radialDir = radial.LengthSquared > 0.000001f
                    ? Vector3.Normalize(radial)
                    : RandomUnitVector();

                Vector3 direction = highEnergy
                    ? incomingDirection * 0.68f + radialDir * RandomRange(0.30f, 0.80f) + Vector3.UnitY * RandomRange(0.05f, 0.30f) + RandomUnitVector() * 0.16f
                    : radialDir * 0.12f + Vector3.UnitY * 0.04f + RandomUnitVector() * 0.12f;

                direction = direction.LengthSquared > 0.000001f ? Vector3.Normalize(direction) : Vector3.UnitY;

                float speedFromEnergy = MathF.Sqrt(MathF.Max(0f, 2f * energyPerPiece / mass));
                float speed = highEnergy
                    ? MathHelper.Clamp(speedFromEnergy, 0.35f, 10f)
                    : MathHelper.Clamp(speedFromEnergy, 0.08f, 1.8f);

                if (highEnergy)
                    speed += MathHelper.Clamp(projectileMomentum / MathF.Max(0.5f, mass * cells.Count), 0f, 1.5f);

                Vector3 spawnPosition = center + RandomUnitVector() * (CellSize * 0.08f);
                Vector3 velocity = direction * speed;
                Vector3 rotation = new(
                    RandomRange(-MathF.PI, MathF.PI),
                    RandomRange(-MathF.PI, MathF.PI),
                    RandomRange(-MathF.PI, MathF.PI));
                Vector3 angularVelocity = RandomUnitVector() * (highEnergy ? RandomRange(2f, 10f) : RandomRange(0.5f, 3f));

                float shade = RandomRange(0.33f, 0.45f);
                Vector3 color = new(shade, shade + 0.006f, shade + 0.012f);

                _fragments.Add(new ConcreteFragment(
                    spawnPosition,
                    velocity,
                    rotation,
                    angularVelocity,
                    size,
                    color,
                    mass));
            }
        }
    }

    /// <summary>
    /// Вся конструкция считается поддерживаемой, если через соседние бетонные
    /// ячейки она связана с нижним слоем/фундаментом. Полностью отделившиеся
    /// куски стен или крыши превращаются в падающие обломки.
    /// </summary>
    private List<CellIndex> DetachUnsupportedCells()
    {
        var supported = new bool[CellsX, CellsY, CellsZ];
        var queue = new Queue<CellIndex>();

        for (int x = 0; x < CellsX; x++)
        for (int z = 0; z < CellsZ; z++)
        {
            if (!_intact[x, 0, z])
                continue;

            supported[x, 0, z] = true;
            queue.Enqueue(new CellIndex(x, 0, z));
        }

        while (queue.Count > 0)
        {
            CellIndex c = queue.Dequeue();
            foreach ((int dx, int dy, int dz) in NeighbourOffsets)
            {
                int nx = c.X + dx;
                int ny = c.Y + dy;
                int nz = c.Z + dz;
                if (nx < 0 || nx >= CellsX || ny < 0 || ny >= CellsY || nz < 0 || nz >= CellsZ)
                    continue;
                if (!_intact[nx, ny, nz] || supported[nx, ny, nz])
                    continue;

                supported[nx, ny, nz] = true;
                queue.Enqueue(new CellIndex(nx, ny, nz));
            }
        }

        var detached = new List<CellIndex>();
        for (int x = 0; x < CellsX; x++)
        for (int y = 0; y < CellsY; y++)
        for (int z = 0; z < CellsZ; z++)
        {
            if (!_intact[x, y, z] || supported[x, y, z])
                continue;

            CellIndex cell = new(x, y, z);
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

        float groundTangential = MathHelper.Clamp(1f - Material.Friction * 0.47f, 0.20f, 0.95f);
        fragment.Velocity.X *= groundTangential;
        fragment.Velocity.Z *= groundTangential;
        fragment.AngularVelocityRadPerSec *= 0.70f;
        return true;
    }

    private bool ResolveFragmentAgainstHouse(ConcreteFragment fragment)
    {
        float radius = fragment.CollisionRadiusMeters;
        Vector3 half = new(WidthMeters * 0.5f + radius, HeightMeters * 0.5f + radius, DepthMeters * 0.5f + radius);
        Vector3 d = fragment.Position - Position;
        if (MathF.Abs(d.X) > half.X || MathF.Abs(d.Y) > half.Y || MathF.Abs(d.Z) > half.Z)
            return false;

        if (!TryWorldToCell(fragment.Position, out int cx, out int cy, out int cz))
        {
            cx = Math.Clamp((int)((fragment.Position.X - (Position.X - WidthMeters * 0.5f)) / CellSize), 0, CellsX - 1);
            cy = Math.Clamp((int)((fragment.Position.Y - (Position.Y - HeightMeters * 0.5f)) / CellSize), 0, CellsY - 1);
            cz = Math.Clamp((int)((fragment.Position.Z - (Position.Z - DepthMeters * 0.5f)) / CellSize), 0, CellsZ - 1);
        }

        bool collided = false;
        Vector3 cellHalf = new(CellSize * 0.5f);

        for (int x = Math.Max(0, cx - 2); x <= Math.Min(CellsX - 1, cx + 2); x++)
        for (int y = Math.Max(0, cy - 2); y <= Math.Min(CellsY - 1, cy + 2); y++)
        for (int z = Math.Max(0, cz - 2); z <= Math.Min(CellsZ - 1, cz + 2); z++)
        {
            if (!_intact[x, y, z])
                continue;

            Vector3 center = GetCellWorldPosition(x, y, z);
            if (!SphereAabbPenetration(fragment.Position, radius, center - cellHalf, center + cellHalf, out Vector3 normal, out float penetration))
                continue;

            fragment.Position += normal * penetration;
            float vn = Vector3.Dot(fragment.Velocity, normal);
            if (vn < 0f)
                fragment.Velocity -= normal * (1.08f * vn);

            fragment.Velocity *= 0.86f;
            fragment.AngularVelocityRadPerSec *= 0.85f;
            fragment.Wake();
            collided = true;
        }

        return collided;
    }

    private bool TryWorldToCell(Vector3 world, out int x, out int y, out int z)
    {
        float minX = Position.X - WidthMeters * 0.5f;
        float minY = Position.Y - HeightMeters * 0.5f;
        float minZ = Position.Z - DepthMeters * 0.5f;

        x = (int)MathF.Floor((world.X - minX) / CellSize);
        y = (int)MathF.Floor((world.Y - minY) / CellSize);
        z = (int)MathF.Floor((world.Z - minZ) / CellSize);

        return x >= 0 && x < CellsX && y >= 0 && y < CellsY && z >= 0 && z < CellsZ;
    }

    private bool TryFindClosestIntactCell(Vector3 point, out CellIndex closest)
    {
        closest = default;
        float bestDistanceSquared = float.PositiveInfinity;
        bool found = false;

        for (int x = 0; x < CellsX; x++)
        for (int y = 0; y < CellsY; y++)
        for (int z = 0; z < CellsZ; z++)
        {
            if (!_intact[x, y, z])
                continue;

            float d2 = (GetCellWorldPosition(x, y, z) - point).LengthSquared;
            if (d2 >= bestDistanceSquared)
                continue;

            bestDistanceSquared = d2;
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

    private int CountIntactCells()
    {
        int count = 0;
        for (int x = 0; x < CellsX; x++)
        for (int y = 0; y < CellsY; y++)
        for (int z = 0; z < CellsZ; z++)
            if (_intact[x, y, z]) count++;
        return count;
    }

    private void TrimFragmentBudget()
    {
        if (_fragments.Count <= MaxFragments)
            return;

        int remove = _fragments.Count - MaxFragments;
        _fragments.RemoveRange(0, remove);
    }

    private Vector3 RandomUnitVector()
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3 v = new(
                RandomRange(-1f, 1f),
                RandomRange(-1f, 1f),
                RandomRange(-1f, 1f));
            if (v.LengthSquared > 0.0001f)
                return Vector3.Normalize(v);
        }
        return Vector3.UnitY;
    }

    private float RandomRange(float min, float max)
        => min + (float)_random.NextDouble() * (max - min);

    private static bool TrySegmentAabb(
        Vector3 from,
        Vector3 to,
        Vector3 min,
        Vector3 max,
        out float enterT,
        out float exitT)
    {
        Vector3 d = to - from;
        enterT = 0f;
        exitT = 1f;

        for (int axis = 0; axis < 3; axis++)
        {
            float origin = axis == 0 ? from.X : axis == 1 ? from.Y : from.Z;
            float delta = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
            float axisMin = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            float axisMax = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;

            if (MathF.Abs(delta) < 0.000001f)
            {
                if (origin < axisMin || origin > axisMax)
                    return false;
                continue;
            }

            float inv = 1f / delta;
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
            return true;
        }

        // Центр сферы внутри AABB: выталкиваем к ближайшей грани.
        float left = sphereCenter.X - min.X;
        float right = max.X - sphereCenter.X;
        float down = sphereCenter.Y - min.Y;
        float up = max.Y - sphereCenter.Y;
        float back = sphereCenter.Z - min.Z;
        float front = max.Z - sphereCenter.Z;

        float smallest = left;
        normal = -Vector3.UnitX;
        if (right < smallest) { smallest = right; normal = Vector3.UnitX; }
        if (down < smallest) { smallest = down; normal = -Vector3.UnitY; }
        if (up < smallest) { smallest = up; normal = Vector3.UnitY; }
        if (back < smallest) { smallest = back; normal = -Vector3.UnitZ; }
        if (front < smallest) { smallest = front; normal = Vector3.UnitZ; }

        penetration = radius + smallest;
        return true;
    }

    private readonly record struct CellIndex(int X, int Y, int Z);
}
