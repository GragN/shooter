using OpenTK.Mathematics;

namespace Sniper3D;

/// <summary>Unreinforced 1 m³ concrete sample resting on a rigid support table.</summary>
internal sealed partial class Target
{
    private const int Resolution = 40;
    private const float CubeSizeMeters = 1f;
    private const int MaxFragments = 600;
    private readonly bool[,,] _intact = new bool[Resolution, Resolution, Resolution];
    private readonly float[,,] _damage = new float[Resolution, Resolution, Resolution];
    private readonly float[,,] _crushing = new float[Resolution, Resolution, Resolution];
    private readonly float[,,] _erodedMass = new float[Resolution, Resolution, Resolution];
    private readonly bool[,,] _failedVerticalBonds = new bool[Resolution, Resolution, Resolution];
    private float _pendingGravityFractureWork;
    public float GravityFractureWorkJoules { get; private set; }
    private readonly List<ConcreteFragment> _fragments = new();
    private readonly Random _random = new(481516);
    private float _debrisAccumulator;
    private double _attachedErodedMass;
    private double _detachedMass;
    private double _archivedMass;
    private static readonly Vector3i[] Neighbours =
    { new(1,0,0), new(-1,0,0), new(0,1,0), new(0,-1,0), new(0,0,1), new(0,0,-1) };

    public Vector3 Position { get; private set; } = new(0f, 2.2f, -995f);
    public float DistanceMeters { get; private set; } = 1000f;
    public float SizeMeters => CubeSizeMeters;
    public float HalfSizeMeters => CubeSizeMeters * 0.5f;
    public float CellSizeMeters => CubeSizeMeters / Resolution;
    public int GridResolution => Resolution;
    public int TotalCellCount => Resolution * Resolution * Resolution;
    public int IntactCellCount { get; private set; }
    public int Revision { get; private set; }
    public MaterialProfile Material => MaterialProfile.NormalConcrete;
    public IReadOnlyList<ConcreteFragment> Fragments => _fragments;
    public double InitialMassKg => Material.DensityKgPerM3;
    public double AttachedMassKg => IntactCellCount * CellMass - _attachedErodedMass;
    public double DetachedMassKg => _detachedMass;
    public double ArchivedMassKg => _archivedMass;
    public ConcreteImpactResult LastImpact { get; private set; }
    private double CellMass => (double)Material.DensityKgPerM3 / TotalCellCount;
    public float Integrity01 => (float)(AttachedMassKg / InitialMassKg);

    public Target() => ResetDamage();

    public void SetDistance(float distanceMeters, float shooterZ)
    {
        float next = Math.Clamp(distanceMeters, 100f, 2000f);
        Vector3 position = new(Position.X, Position.Y, shooterZ - next);
        bool moved = (position - Position).LengthSquared > 1e-8f;
        Position = position;
        DistanceMeters = next;
        if (moved) ResetDamage();
    }

    public void ResetDamage()
    {
        for (int x = 0; x < Resolution; x++)
        for (int y = 0; y < Resolution; y++)
        for (int z = 0; z < Resolution; z++) _intact[x, y, z] = true;
        Array.Clear(_damage); Array.Clear(_crushing); Array.Clear(_erodedMass);
        Array.Clear(_failedVerticalBonds);
        _pendingGravityFractureWork = GravityFractureWorkJoules = 0f;
        _fragments.Clear();
        _attachedErodedMass = _detachedMass = _archivedMass = 0;
        _debrisAccumulator = 0f;
        IntactCellCount = TotalCellCount;
        LastImpact = default;
        Revision++;
    }

    public bool IsCellIntact(int x, int y, int z) => _intact[x, y, z];
    public float GetCellDamage01(int x, int y, int z) => Math.Clamp(MathF.Max(_damage[x,y,z], _crushing[x,y,z]), 0f, 1f);
    public bool IsCellExposed(int x, int y, int z)
    {
        if (!_intact[x,y,z]) return false;
        foreach (Vector3i n in Neighbours)
        {
            int a = x+n.X, b=y+n.Y, c=z+n.Z;
            if (!Inside(a,b,c) || !_intact[a,b,c]) return true;
        }
        return false;
    }
    public Vector3 GetCellWorldPosition(int x, int y, int z) => Position + GetCellLocalPosition(x,y,z);
    public Vector3 GetCellLocalPosition(int x, int y, int z) => new Vector3(x+0.5f,y+0.5f,z+0.5f) * CellSizeMeters - new Vector3(0.5f);
    public bool TryHitSegment(Vector3 from, Vector3 to, out float hitT, out Vector3 hitPoint)
        => VoxelSegmentCaster.TryHit(_intact, Position-new Vector3(0.5f), CellSizeMeters, from, to, out hitT, out hitPoint);
    public bool TryHitSegment(Vector3 from, Vector3 to, out Vector3 hitPoint) => TryHitSegment(from,to,out _,out hitPoint);
    private static bool Inside(int x,int y,int z) => x>=0 && y>=0 && z>=0 && x<Resolution && y<Resolution && z<Resolution;

    private float RemoveCell(Vector3i cell)
    {
        float mass = (float)(CellMass - _erodedMass[cell.X,cell.Y,cell.Z]);
        _attachedErodedMass -= _erodedMass[cell.X,cell.Y,cell.Z];
        _erodedMass[cell.X,cell.Y,cell.Z] = 0f;
        _intact[cell.X,cell.Y,cell.Z] = false;
        IntactCellCount--;
        _detachedMass += mass;
        return mass;
    }

    /// <summary>Unsupported connected pieces remain rigid aggregates.</summary>
    private List<List<Vector3i>> FindUnsupportedComponents()
    {
        var supported = new bool[Resolution,Resolution,Resolution];
        var queue = new Queue<Vector3i>();
        for (int x=0;x<Resolution;x++)
        for (int z=0;z<Resolution;z++)
            if (_intact[x,0,z]) { supported[x,0,z]=true; queue.Enqueue(new(x,0,z)); }
        while(queue.Count>0)
        {
            Vector3i p=queue.Dequeue();
            foreach(Vector3i n in Neighbours)
            {
                Vector3i q=p+n;
                if(!Inside(q.X,q.Y,q.Z) || supported[q.X,q.Y,q.Z] || !_intact[q.X,q.Y,q.Z]) continue;
                if(!BondIntact(p,q)) continue;
                supported[q.X,q.Y,q.Z]=true; queue.Enqueue(q);
            }
        }
        var components = new List<List<Vector3i>>();
        for(int x=0;x<Resolution;x++)
        for(int y=0;y<Resolution;y++)
        for(int z=0;z<Resolution;z++)
        {
            if(!_intact[x,y,z] || supported[x,y,z]) continue;
            var component=new List<Vector3i>();
            supported[x,y,z]=true; queue.Enqueue(new(x,y,z));
            while(queue.Count>0)
            {
                Vector3i p=queue.Dequeue(); component.Add(p);
                foreach(Vector3i n in Neighbours)
                {
                    Vector3i q=p+n;
                    if(!Inside(q.X,q.Y,q.Z) || supported[q.X,q.Y,q.Z] || !_intact[q.X,q.Y,q.Z]) continue;
                    if(!BondIntact(p,q)) continue;
                    supported[q.X,q.Y,q.Z]=true; queue.Enqueue(q);
                }
            }
            components.Add(component);
        }
        return components;
    }

    private bool BondIntact(Vector3i a,Vector3i b)
    {
        if(_damage[a.X,a.Y,a.Z]>=1f && _damage[b.X,b.Y,b.Z]>=1f) return false;
        return a.Y==b.Y || !_failedVerticalBonds[a.X,Math.Max(a.Y,b.Y),a.Z];
    }
}

internal readonly record struct ConcreteImpactResult(
    int RemovedCellCount, int DirectlyDestroyedCellCount, int StructurallyDetachedCellCount,
    int SpawnedFragmentCount, bool Perforated, float ResidualEnergyJoules, float ResidualSpeedMps,
    float PenetrationDepthMeters, float CraterRadiusMeters, float DepositedEnergyJoules,
    bool Ricochet = false, Vector3 ExitVelocity = default, Vector3 ExitPosition = default,
    float ResidualMassKg = 0f, float CrushingWorkJoules = 0f, float FractureWorkJoules = 0f,
    float DebrisKineticEnergyJoules = 0f);
