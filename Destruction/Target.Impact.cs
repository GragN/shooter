using OpenTK.Mathematics;

namespace Sniper3D;

internal sealed partial class Target
{
    private readonly record struct DamageCandidate(Vector3i Cell, float Crush, float Crack, float CrushCost, float CrackCost);

    public ConcreteImpactResult ApplyImpact(Vector3 hitPoint, ProjectileImpactData projectile)
    {
        if (IntactCellCount == 0 || projectile.KineticEnergyJoules <= 0f) return default;
        Vector3 direction = Vector3.Normalize(projectile.Direction);
        List<VoxelRayCell> ray = VoxelRayTrace.Trace(hitPoint, direction, Position-new Vector3(0.5f), Resolution, CellSizeMeters, 2f);
        var span = new List<VoxelRayCell>();
        foreach (VoxelRayCell cell in ray)
        {
            if (!_intact[cell.X,cell.Y,cell.Z])
            {
                if (span.Count>0) break;
                continue;
            }
            span.Add(cell);
        }
        if (span.Count==0) return default;
        VoxelRayCell first=span[0];
        float thickness=span[^1].Exit-first.Entry;
        Vector3 contact=hitPoint+direction*first.Entry;
        Vector3 normal=ContactNormal(contact,new(first.X,first.Y,first.Z),direction);
        ConcreteCubeImpactSolution solution=ConcreteCubeImpactModel.Solve(Material,projectile,thickness,normal,
            _damage[first.X,first.Y,first.Z]);
        var core=new Dictionary<Vector3i,float>();
        foreach(VoxelRayCell cell in span)
        {
            float overlap=MathF.Min(cell.Exit-first.Entry,solution.MaterialTravel)-(cell.Entry-first.Entry);
            if(overlap>0f) core[new(cell.X,cell.Y,cell.Z)]=Math.Clamp(overlap/(cell.Exit-cell.Entry),0f,1f);
        }

        float crushBudget=solution.DepositedEnergy*0.55f;
        float crackBudget=solution.DepositedEnergy*0.15f;
        float kineticBudget=solution.DepositedEnergy*0.025f;
        float fullCrushWork=ConcreteCubeImpactModel.CrushingWorkPerVolume(Material)*MathF.Pow(CellSizeMeters,3f);
        float fullCrackWork=ConcreteCubeImpactModel.FractureWorkPerCell(Material,CellSizeMeters);
        var candidates=new List<DamageCandidate>();
        float desiredCrush=0f,desiredCrack=0f;
        float half=CellSizeMeters*0.5f;
        float axialHalf=half*(MathF.Abs(direction.X)+MathF.Abs(direction.Y)+MathF.Abs(direction.Z));
        for(int x=0;x<Resolution;x++)
        for(int y=0;y<Resolution;y++)
        for(int z=0;z<Resolution;z++)
        {
            if(!_intact[x,y,z]) continue;
            Vector3 delta=GetCellWorldPosition(x,y,z)-contact;
            float axial=Vector3.Dot(delta,direction);
            if(axial+axialHalf<0f || axial-axialHalf>solution.MaterialTravel) continue;
            float radial=(delta-direction*axial).Length;
            float entryWeight=1f-Math.Clamp(MathF.Max(0f,axial-axialHalf)/MathF.Max(solution.EntryDepth,1e-6f),0f,1f);
            float exitWeight=solution.Perforated ? 1f-Math.Clamp(MathF.Max(0f,thickness-axial-axialHalf)/MathF.Max(solution.EntryDepth*2f,1e-6f),0f,1f) : 0f;
            float radius=MathF.Max(solution.BoreRadius,MathF.Max(solution.EntryRadius*entryWeight,solution.ExitRadius*exitWeight));
            float radialWeight=Math.Clamp(1f-radial/MathF.Max(radius+half,1e-6f),0f,1f);
            if(radialWeight<=0f) continue;
            float overlap=MathF.Max(0f,MathF.Min(axial+axialHalf,solution.MaterialTravel)-MathF.Max(0f,axial-axialHalf))/(2f*axialHalf);
            Vector3i index=new(x,y,z);
            float crush=MathF.Min(1f-_crushing[x,y,z],overlap*radialWeight);
            if(core.TryGetValue(index,out float coreDose) && !solution.Ricochet)
                crush=MathF.Max(crush,MathF.Min(1f-_crushing[x,y,z],coreDose));
            float crack=MathF.Min(1f-_damage[x,y,z],0.75f*overlap*radialWeight*MathF.Max(entryWeight,exitWeight));
            // Fixed spatial heterogeneity represents aggregate variation, not per-frame noise.
            float strength=0.9f+((x*73856093^y*19349663^z*83492791)&255)/1275f;
            float crushCost=crush*fullCrushWork*strength;
            float crackCost=crack*fullCrackWork*strength;
            desiredCrush+=crushCost; desiredCrack+=crackCost;
            candidates.Add(new(index,crush,crack,crushCost,crackCost));
        }
        float crushScale=desiredCrush>0f?MathF.Min(1f,crushBudget/desiredCrush):0f;
        float crackScale=desiredCrack>0f?MathF.Min(1f,crackBudget/desiredCrack):0f;
        float crushingWork=desiredCrush*crushScale,fractureWork=desiredCrack*crackScale;
        var destroyed=new List<Vector3i>();
        foreach(DamageCandidate c in candidates)
        {
            Vector3i p=c.Cell;
            _crushing[p.X,p.Y,p.Z]=MathF.Min(1f,_crushing[p.X,p.Y,p.Z]+c.Crush*crushScale);
            _damage[p.X,p.Y,p.Z]=MathF.Min(1f,_damage[p.X,p.Y,p.Z]+c.Crack*crackScale);
            if(_crushing[p.X,p.Y,p.Z]>=0.999f ||
                (_crushing[p.X,p.Y,p.Z]+0.5f*_damage[p.X,p.Y,p.Z]>=1f && IsCellExposed(p.X,p.Y,p.Z)))
                destroyed.Add(p);
        }
        int start=_fragments.Count;
        foreach(Vector3i p in destroyed)
        {
            float mass=RemoveCell(p);
            ConcreteFragment fragment=MakeFragment(new[]{p},new[]{mass});
            Vector3 offset=fragment.Position-contact;
            float depth=Vector3.Dot(offset,direction);
            Vector3 radial=offset-direction*depth;
            Vector3 outward=depth<solution.EntryDepth*2f ? -direction : direction*0.2f;
            Vector3 ejection=outward+radial/MathF.Max(radial.Length,0.001f)+RandomVector()*0.2f;
            fragment.Velocity=Vector3.Normalize(ejection)*(0.5f+(float)_random.NextDouble());
            fragment.AngularVelocityRadPerSec=RandomVector()*10f;
            _fragments.Add(fragment);
        }
        // Sub-voxel chips come from a real cell's mass ledger, even when the
        // 25 mm cell has not been fully crushed. No mass is conjured from a decal.
        if(_intact[first.X,first.Y,first.Z])
        {
            float chipMass=MathF.Min((float)CellMass*0.02f,projectile.KineticEnergyJoules*1e-5f);
            chipMass=MathF.Min(chipMass,(float)CellMass-_erodedMass[first.X,first.Y,first.Z]);
            float chipWork=chipMass/Material.DensityKgPerM3*ConcreteCubeImpactModel.CrushingWorkPerVolume(Material);
            float chipFracture=Material.FractureEnergyJPerM2*24f*MathF.Pow(chipMass/(4f*Material.DensityKgPerM3),2f/3f);
            if(chipMass>1e-7f && chipWork<=crushBudget-crushingWork && chipFracture<=crackBudget-fractureWork)
            {
                _erodedMass[first.X,first.Y,first.Z]+=chipMass;
                _attachedErodedMass+=chipMass; _detachedMass+=chipMass;
                crushingWork+=chipWork; fractureWork+=chipFracture;
                float size=MathF.Cbrt(chipMass/(4f*Material.DensityKgPerM3));
                for(int i=0;i<4;i++)
                {
                    Vector3 eject=Vector3.Normalize(normal+RandomVector()*0.4f);
                    _fragments.Add(new(contact+normal*(size+0.001f),eject,Vector3.Zero,RandomVector()*10f,
                        new(size),new(0.42f),chipMass*0.25f));
                }
            }
        }
        float initialKinetic=0f;
        for(int i=start;i<_fragments.Count;i++) initialKinetic+=_fragments[i].KineticEnergyJoules;
        float velocityScale=initialKinetic>0f?MathF.Sqrt(kineticBudget/initialKinetic):0f;
        for(int i=start;i<_fragments.Count;i++)
        {
            float speed=_fragments[i].Velocity.Length,spin=_fragments[i].AngularVelocityRadPerSec.Length;
            if(speed>0f) velocityScale=MathF.Min(velocityScale,85f/speed);
            if(spin>0f) velocityScale=MathF.Min(velocityScale,80f/spin);
        }
        // Scale both translation and rotation together; no separate momentum kick or minimum speed.
        float finalKinetic=0f;
        for(int i=start;i<_fragments.Count;i++)
        {
            _fragments[i].Velocity*=velocityScale;
            _fragments[i].AngularVelocityRadPerSec*=velocityScale;
            finalKinetic+=_fragments[i].KineticEnergyJoules;
        }
        int detached=0;
        if(candidates.Count>0)
        {
            detached+=DetachUnsupported();
            CheckGravityCapacity();
            detached+=DetachUnsupported();
        }
        foreach(ConcreteFragment fragment in _fragments)
            if((fragment.Position-contact).LengthSquared<1f) fragment.Wake();
        int spawned=_fragments.Count-start;
        TrimFragments();
        Revision++;
        bool perforated=solution.Perforated;
        // If discretisation could not afford to open the whole channel, the
        // projectile must stop; it may not emerge through intact concrete.
        if(perforated)
            foreach(VoxelRayCell cell in span)
                if(_intact[cell.X,cell.Y,cell.Z]) { perforated=false; break; }
        float residual=perforated||solution.Ricochet?solution.ResidualEnergy:0f;
        Vector3 exitDirection=solution.ExitVelocity.LengthSquared>0f?Vector3.Normalize(solution.ExitVelocity):direction;
        Vector3 exit=solution.Ricochet?contact+normal*0.001f+exitDirection*0.001f
            : contact+direction*(thickness+0.001f);
        LastImpact=new(destroyed.Count+detached,destroyed.Count,detached,spawned,perforated,
            residual,residual>0f?solution.ExitVelocity.Length:0f,solution.PenetrationDepth,solution.EntryRadius,
            projectile.KineticEnergyJoules-residual,solution.Ricochet,solution.ExitVelocity,exit,
            residual>0f?solution.ResidualMass:0f,crushingWork,fractureWork,finalKinetic);
        return LastImpact;
    }

    private Vector3 ContactNormal(Vector3 point,Vector3i cell,Vector3 incoming)
    {
        Vector3 offset=point-GetCellWorldPosition(cell.X,cell.Y,cell.Z);
        Vector3 unit=Vector3.Normalize(incoming);
        int axis=0; float best=float.PositiveInfinity;
        for(int a=0;a<3;a++)
        {
            float sign=offset[a]>=0f?1f:-1f;
            if(sign*unit[a]>=-1e-4f) continue;
            float distance=MathF.Abs(CellSizeMeters*0.5f-MathF.Abs(offset[a]));
            if(distance<best) { axis=a; best=distance; }
        }
        Vector3 normal=Vector3.Zero; normal[axis]=offset[axis]>=0f?1f:-1f;
        return Vector3.Dot(normal,incoming)>0f?-normal:normal;
    }

    private ConcreteFragment MakeFragment(IReadOnlyList<Vector3i> cells,IReadOnlyList<float> masses)
    {
        Vector3 center=Vector3.Zero; float mass=0f;
        for(int i=0;i<cells.Count;i++)
        {
            Vector3i p=cells[i]; center+=GetCellLocalPosition(p.X,p.Y,p.Z)*masses[i]; mass+=masses[i];
        }
        center/=mass;
        Vector3[] offsets=new Vector3[cells.Count];
        Vector3 min=new(float.PositiveInfinity),max=new(float.NegativeInfinity);
        for(int i=0;i<cells.Count;i++)
        {
            Vector3i p=cells[i]; offsets[i]=GetCellLocalPosition(p.X,p.Y,p.Z)-center;
            min=Vector3.ComponentMin(min,offsets[i]-new Vector3(CellSizeMeters*0.5f));
            max=Vector3.ComponentMax(max,offsets[i]+new Vector3(CellSizeMeters*0.5f));
        }
        Vector3 inertia=Vector3.Zero;
        for(int i=0;i<cells.Count;i++)
        {
            Vector3 r=offsets[i]; float own=CellSizeMeters*CellSizeMeters/6f;
            inertia+=new Vector3(own+r.Y*r.Y+r.Z*r.Z,own+r.X*r.X+r.Z*r.Z,own+r.X*r.X+r.Y*r.Y)*masses[i];
        }
        return new(Position+center,Vector3.Zero,Vector3.Zero,Vector3.Zero,max-min,new(0.42f,0.425f,0.43f),mass)
        { LocalVoxelCenters=cells.Count>1?offsets:null,VoxelSizeMeters=CellSizeMeters,
            LocalBoundsCenter=(min+max)*0.5f,InertiaOverride=inertia };
    }

    private Vector3 RandomVector() => new((float)_random.NextDouble()-0.5f,(float)_random.NextDouble()-0.5f,(float)_random.NextDouble()-0.5f);
    private void TrimFragments()
    {
        while(_fragments.Count>MaxFragments)
        {
            // Retire old settled pieces first; their mass remains in the archive ledger.
            int index=_fragments.FindIndex(f=>f.Sleeping);
            if(index<0) index=0;
            _archivedMass+=_fragments[index].MassKg;
            _fragments.RemoveAt(index);
        }
    }
}
