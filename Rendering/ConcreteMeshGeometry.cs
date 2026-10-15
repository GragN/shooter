using OpenTK.Mathematics;

namespace Sniper3D;

/// <summary>Only exposed voxel faces, batched into a single coloured mesh.</summary>
internal static class ConcreteMeshGeometry
{
    private static readonly Vector3i[] Neighbours =
    {new(1,0,0),new(-1,0,0),new(0,1,0),new(0,-1,0),new(0,0,1),new(0,0,-1)};

    public static (float[] Vertices,uint[] Indices) Build(Target target)
    {
        var data=new List<float>(); int n=target.GridResolution;
        for(int x=0;x<n;x++)
        for(int y=0;y<n;y++)
        for(int z=0;z<n;z++)
        {
            if(!target.IsCellIntact(x,y,z)) continue;
            float damage=target.GetCellDamage01(x,y,z);
            float shade=(0.40f+((x*17+y*7+z*13)%6)*0.008f)*(1f-0.40f*damage);
            Vector3 color=new(shade,shade+0.006f,shade+0.012f);
            foreach(Vector3i d in Neighbours)
            {
                int a=x+d.X,b=y+d.Y,c=z+d.Z;
                if(a>=0 && b>=0 && c>=0 && a<n && b<n && c<n && target.IsCellIntact(a,b,c)) continue;
                Face(data,target.GetCellLocalPosition(x,y,z),target.CellSizeMeters,new(d.X,d.Y,d.Z),color);
            }
        }
        return Finish(data);
    }

    public static (float[] Vertices,uint[] Indices) BuildFragment(ConcreteFragment body)
    {
        Vector3[] positions=body.LocalVoxelCenters??new[]{Vector3.Zero};
        float size=body.VoxelSizeMeters;
        Vector3 anchor=positions[0];
        Vector3i Key(Vector3 p) => new((int)MathF.Round((p.X-anchor.X)/size),
            (int)MathF.Round((p.Y-anchor.Y)/size),(int)MathF.Round((p.Z-anchor.Z)/size));
        var cells=positions.Select(Key).ToHashSet();
        var data=new List<float>();
        foreach(Vector3 p in positions)
        foreach(Vector3i d in Neighbours)
            if(!cells.Contains(Key(p)+d)) Face(data,p,size,new(d.X,d.Y,d.Z),body.Color);
        return Finish(data);
    }

    private static void Face(List<float> data,Vector3 center,float size,Vector3 normal,Vector3 color)
    {
        Vector3 u=MathF.Abs(normal.Y)>0.5f?Vector3.UnitX:Vector3.UnitY;
        Vector3 v=Vector3.Cross(normal,u);
        float h=size*0.5f; Vector3 face=center+normal*h;
        Vector3 a=face-u*h-v*h,b=face+u*h-v*h,c=face+u*h+v*h,d=face-u*h+v*h;
        Add(a);Add(b);Add(c);Add(a);Add(c);Add(d);
        void Add(Vector3 p)
        {
            data.Add(p.X);data.Add(p.Y);data.Add(p.Z);
            data.Add(normal.X);data.Add(normal.Y);data.Add(normal.Z);
            data.Add(color.X);data.Add(color.Y);data.Add(color.Z);
        }
    }
    private static (float[],uint[]) Finish(List<float> data)
    {
        float[] vertices=data.ToArray(); uint[] indices=new uint[vertices.Length/9];
        for(uint i=0;i<indices.Length;i++) indices[i]=i;
        return(vertices,indices);
    }
}
