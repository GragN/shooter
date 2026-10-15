using OpenTK.Mathematics;

namespace Sniper3D;

internal sealed partial class Target
{
    /// <summary>
    /// Coarse section-capacity check: self-weight plus eccentric bending must
    /// fit compressive/tensile strength. Connectivity alone is not sufficient.
    /// </summary>
    private void CheckGravityCapacity()
    {
        double[] mass=new double[Resolution],momentX=new double[Resolution],momentZ=new double[Resolution];
        for(int x=0;x<Resolution;x++)
        for(int y=0;y<Resolution;y++)
        for(int z=0;z<Resolution;z++)
            if(_intact[x,y,z])
            {
                double m=CellMass-_erodedMass[x,y,z]; Vector3 p=GetCellLocalPosition(x,y,z);
                mass[y]+=m; momentX[y]+=m*p.X; momentZ[y]+=m*p.Z;
            }
        double above=0,cx=0,cz=0;
        float bondWork=Material.FractureEnergyJPerM2*CellSizeMeters*CellSizeMeters;
        for(int y=Resolution-1;y>=1;y--)
        {
            above+=mass[y]; cx+=momentX[y]; cz+=momentZ[y];
            if(above<=0) continue;
            var section=new List<Vector3i>(); double sx=0,sz=0;
            for(int x=0;x<Resolution;x++)
            for(int z=0;z<Resolution;z++)
                if(_intact[x,y,z] && _intact[x,y-1,z] && !_failedVerticalBonds[x,y,z])
                { section.Add(new(x,y,z)); Vector3 p=GetCellLocalPosition(x,y,z); sx+=p.X;sz+=p.Z; }
            if(section.Count==0) continue;
            sx/=section.Count; sz/=section.Count;
            double area=CellSizeMeters*CellSizeMeters,ix=0,iz=0;
            foreach(Vector3i cell in section)
            {
                Vector3 p=GetCellLocalPosition(cell.X,cell.Y,cell.Z);
                ix+=area*((p.Z-sz)*(p.Z-sz)+CellSizeMeters*CellSizeMeters/12.0);
                iz+=area*((p.X-sx)*(p.X-sx)+CellSizeMeters*CellSizeMeters/12.0);
            }
            double weight=above*9.80665,baseStress=weight/(area*section.Count);
            double mx=weight*(cz/above-sz),mz=weight*(cx/above-sx);
            float gravityBudget=(float)(weight*CellSizeMeters);
            foreach(Vector3i cell in section)
            {
                Vector3 p=GetCellLocalPosition(cell.X,cell.Y,cell.Z);
                // Include the edge of each cell: a one-cell neck still has a finite section modulus.
                double bending=Math.Abs(mx)*(Math.Abs(p.Z-sz)+CellSizeMeters*0.5)/ix
                    +Math.Abs(mz)*(Math.Abs(p.X-sx)+CellSizeMeters*0.5)/iz;
                float remaining=1f-0.8f*MathF.Max(_damage[cell.X,cell.Y,cell.Z],_damage[cell.X,cell.Y-1,cell.Z]);
                bool fails=baseStress+bending>Material.CompressiveStrengthPa*remaining
                    || bending-baseStress>Material.TensileStrengthPa*remaining;
                if(fails && gravityBudget>=bondWork)
                {
                    _failedVerticalBonds[cell.X,cell.Y,cell.Z]=true;
                    gravityBudget-=bondWork;
                    _pendingGravityFractureWork+=bondWork;
                    GravityFractureWorkJoules+=bondWork;
                }
            }
        }
    }

    private int DetachUnsupported()
    {
        int detached=0;
        var bodies=new List<ConcreteFragment>();
        foreach(List<Vector3i> component in FindUnsupportedComponents())
        {
            float[] masses=component.Select(RemoveCell).ToArray();
            bodies.Add(MakeFragment(component,masses));
            detached+=component.Count;
        }
        if(bodies.Count>0)
        {
            float mass=bodies.Sum(b=>b.MassKg);
            foreach(ConcreteFragment body in bodies)
            {
                body.FractureEnergyDebtJoules=_pendingGravityFractureWork*(body.MassKg/mass);
                _fragments.Add(body);
            }
            _pendingGravityFractureWork=0f;
        }
        return detached;
    }
}
