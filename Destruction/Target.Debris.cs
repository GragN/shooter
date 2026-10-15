using OpenTK.Mathematics;

namespace Sniper3D;

internal sealed partial class Target
{
    private const float DebrisStep = 1f/120f;

    public void UpdatePhysics(float frameDt,float groundY)
    {
        _debrisAccumulator+=Math.Clamp(frameDt,0f,0.05f);
        while(_debrisAccumulator>=DebrisStep)
        {
            StepDebris(DebrisStep,groundY);
            _debrisAccumulator-=DebrisStep;
        }
    }

    private void StepDebris(float dt,float groundY)
    {
        foreach(ConcreteFragment body in _fragments)
        {
            body.AgeSeconds+=dt;
            if(body.Sleeping) continue;
            Vector3 old=body.Position;
            body.Velocity.Y-=9.80665f*dt;
            body.Velocity*=MathF.Exp(-0.035f*dt);
            body.AngularVelocityRadPerSec*=MathF.Exp(-0.08f*dt);
            body.Position+=body.Velocity*dt;
            body.RotationRadians+=body.AngularVelocityRadPerSec*dt;
            if(body.FractureEnergyDebtJoules>0f && body.Position.Y<old.Y)
            {
                float work=MathF.Min(body.FractureEnergyDebtJoules,body.MassKg*9.80665f*(old.Y-body.Position.Y));
                float energy=body.KineticEnergyJoules;
                work=MathF.Min(work,energy);
                float scale=energy>0f?MathF.Sqrt(MathF.Max(0f,1f-work/energy)):0f;
                body.Velocity*=scale;body.AngularVelocityRadPerSec*=scale;
                body.FractureEnergyDebtJoules-=work;
            }
            bool contact=false;
            if(body.LocalVoxelCenters is null && TryHitSegment(old,body.Position,out _,out Vector3 point))
            {
                Vector3 relative=(point-Position+new Vector3(0.5f))/CellSizeMeters;
                Vector3i cell=new(Math.Clamp((int)relative.X,0,Resolution-1),Math.Clamp((int)relative.Y,0,Resolution-1),Math.Clamp((int)relative.Z,0,Resolution-1));
                Vector3 normal=ContactNormal(point,cell,body.Velocity);
                float radius=MathF.Min(body.SizeMeters.X,MathF.Min(body.SizeMeters.Y,body.SizeMeters.Z))*0.45f;
                body.Position=point+normal*(radius+0.0001f);
                StaticImpulse(body,normal,point);
                contact=true;
            }
            contact|=ResolveConcreteContact(body);
            // A real support slab under the cube replaces the old invisible rear wall.
            float top=Position.Y-0.5f;
            contact|=ResolveBoxContact(body,new(Position.X-0.55f,top-0.12f,Position.Z-0.55f),new(Position.X+0.55f,top,Position.Z+0.55f));
            Bounds(body,out Vector3 center,out Vector3 half);
            float penetration=groundY-(center.Y-half.Y);
            if(penetration>=-1e-5f)
            {
                body.Position.Y+=MathF.Max(0f,penetration);
                Vector3 pointGround=new(center.X,groundY,center.Z);
                StaticImpulse(body,Vector3.UnitY,pointGround);
                contact=true;
            }
            if(contact && body.Velocity.LengthSquared<0.0064f && body.AngularVelocityRadPerSec.LengthSquared<0.04f)
            {
                body.SleepTimerSeconds+=dt;
                if(body.SleepTimerSeconds>0.5f)
                { body.Sleeping=true; body.Velocity=body.AngularVelocityRadPerSec=Vector3.Zero; }
            }
            else body.SleepTimerSeconds=0f;
        }
        ResolvePairs();
    }

    private bool ResolveConcreteContact(ConcreteFragment body)
    {
        if((body.Position-Position).Length>body.CollisionRadiusMeters+0.9f) return false;
        bool contact=false;
        Vector3[] samples=body.LocalVoxelCenters??new[]{Vector3.Zero};
        int stride=Math.Max(1,samples.Length/96);
        float radius=body.LocalVoxelCenters is null
            ? MathF.Min(body.SizeMeters.X,MathF.Min(body.SizeMeters.Y,body.SizeMeters.Z))*0.45f : CellSizeMeters*0.45f;
        Matrix4 rotation=Rotation(body);
        for(int i=0;i<samples.Length;i+=stride)
        {
            Vector3 sample=body.Position+Vector3.TransformNormal(samples[i],rotation);
            Vector3 local=(sample-Position+new Vector3(0.5f))/CellSizeMeters;
            int cx=(int)MathF.Floor(local.X),cy=(int)MathF.Floor(local.Y),cz=(int)MathF.Floor(local.Z);
            for(int x=Math.Max(0,cx-1);x<=Math.Min(Resolution-1,cx+1);x++)
            for(int y=Math.Max(0,cy-1);y<=Math.Min(Resolution-1,cy+1);y++)
            for(int z=Math.Max(0,cz-1);z<=Math.Min(Resolution-1,cz+1);z++)
            {
                if(!_intact[x,y,z]) continue;
                Vector3 center=GetCellWorldPosition(x,y,z),half=new(CellSizeMeters*0.5f);
                if(!SphereBox(sample,radius,center-half,center+half,out Vector3 normal,out float depth)) continue;
                body.Position+=normal*depth; sample+=normal*depth;
                StaticImpulse(body,normal,sample-normal*radius);
                contact=true;
            }
        }
        return contact;
    }

    private bool ResolveBoxContact(ConcreteFragment body,Vector3 min,Vector3 max)
    {
        Bounds(body,out Vector3 center,out Vector3 half);
        Vector3 overlap=Vector3.ComponentMin(center+half,max)-Vector3.ComponentMax(center-half,min);
        if(overlap.X<=0f || overlap.Y<=0f || overlap.Z<=0f) return false;
        int axis=0;
        for(int a=1;a<3;a++) if(overlap[a]<overlap[axis]) axis=a;
        Vector3 normal=Vector3.Zero;
        normal[axis]=center[axis]>=(min[axis]+max[axis])*0.5f?1f:-1f;
        body.Position+=normal*overlap[axis];
        Vector3 point=Vector3.Clamp(center,min,max);
        StaticImpulse(body,normal,point);
        return true;
    }

    private void StaticImpulse(ConcreteFragment body,Vector3 normal,Vector3 point)
    {
        Vector3 arm=point-body.Position;
        Vector3 velocity=body.Velocity+Vector3.Cross(body.AngularVelocityRadPerSec,arm);
        float speed=Vector3.Dot(velocity,normal);
        if(speed>=0f) return;
        float restitution=speed < -0.5f ? Material.Restitution : 0f;
        float impulse=-(1f+restitution)*speed/EffectiveInverseMass(body,arm,normal);
        ApplyImpulse(body,normal*impulse,arm);
        velocity=body.Velocity+Vector3.Cross(body.AngularVelocityRadPerSec,arm);
        Vector3 tangent=velocity-normal*Vector3.Dot(velocity,normal);
        if(tangent.LengthSquared>1e-10f)
        {
            Vector3 t=Vector3.Normalize(tangent);
            float friction=MathF.Min(Material.Friction*impulse,tangent.Length/EffectiveInverseMass(body,arm,t));
            ApplyImpulse(body,-t*friction,arm);
        }
        if(speed < -0.15f && body.Sleeping) body.Wake();
    }

    private static Matrix4 Rotation(ConcreteFragment body) => Matrix4.CreateRotationX(body.RotationRadians.X)
        *Matrix4.CreateRotationY(body.RotationRadians.Y)*Matrix4.CreateRotationZ(body.RotationRadians.Z);
    private static Vector3 InverseInertia(ConcreteFragment body,Vector3 torque)
    {
        Matrix4 rotation=Rotation(body);
        Vector3 local=Vector3.TransformNormal(torque,rotation.Inverted());
        Vector3 inertia=body.Inertia;
        local=new(local.X/MathF.Max(inertia.X,1e-12f),local.Y/MathF.Max(inertia.Y,1e-12f),local.Z/MathF.Max(inertia.Z,1e-12f));
        return Vector3.TransformNormal(local,rotation);
    }
    private static float EffectiveInverseMass(ConcreteFragment body,Vector3 arm,Vector3 normal)
        => 1f/body.MassKg+Vector3.Dot(normal,Vector3.Cross(InverseInertia(body,Vector3.Cross(arm,normal)),arm));
    private static void ApplyImpulse(ConcreteFragment body,Vector3 impulse,Vector3 arm)
    {
        body.Velocity+=impulse/body.MassKg;
        body.AngularVelocityRadPerSec+=InverseInertia(body,Vector3.Cross(arm,impulse));
    }
    private static void Bounds(ConcreteFragment body,out Vector3 center,out Vector3 half)
    {
        Matrix4 r=Rotation(body);
        center=body.Position+Vector3.TransformNormal(body.LocalBoundsCenter,r);
        Vector3 x=Vector3.TransformNormal(new(body.SizeMeters.X*0.5f,0f,0f),r);
        Vector3 y=Vector3.TransformNormal(new(0f,body.SizeMeters.Y*0.5f,0f),r);
        Vector3 z=Vector3.TransformNormal(new(0f,0f,body.SizeMeters.Z*0.5f),r);
        half=new(MathF.Abs(x.X)+MathF.Abs(y.X)+MathF.Abs(z.X),MathF.Abs(x.Y)+MathF.Abs(y.Y)+MathF.Abs(z.Y),MathF.Abs(x.Z)+MathF.Abs(y.Z)+MathF.Abs(z.Z));
    }

    private void ResolvePairs()
    {
        // Sweep-and-prune broad phase; collisions do not disappear at a fragment count threshold.
        ConcreteFragment[] sorted=_fragments.OrderBy(f=>f.Position.X-f.CollisionRadiusMeters).ToArray();
        for(int i=0;i<sorted.Length;i++)
        for(int j=i+1;j<sorted.Length;j++)
        {
            ConcreteFragment a=sorted[i],b=sorted[j];
            if(b.Position.X-b.CollisionRadiusMeters>a.Position.X+a.CollisionRadiusMeters) break;
            if(a.Sleeping && b.Sleeping) continue;
            Bounds(a,out Vector3 ca,out Vector3 ha); Bounds(b,out Vector3 cb,out Vector3 hb);
            Vector3 overlap=ha+hb-new Vector3(MathF.Abs(cb.X-ca.X),MathF.Abs(cb.Y-ca.Y),MathF.Abs(cb.Z-ca.Z));
            if(overlap.X<=0f || overlap.Y<=0f || overlap.Z<=0f) continue;
            int axis=0; for(int k=1;k<3;k++) if(overlap[k]<overlap[axis]) axis=k;
            Vector3 n=Vector3.Zero; n[axis]=cb[axis]>=ca[axis]?1f:-1f;
            float ia=1f/a.MassKg,ib=1f/b.MassKg;
            a.Position-=n*overlap[axis]*(ia/(ia+ib)); b.Position+=n*overlap[axis]*(ib/(ia+ib));
            float speed=Vector3.Dot(b.Velocity-a.Velocity,n);
            if(speed>=0f) continue;
            Vector3 impulse=n*(-(1f+Material.Restitution)*speed/(ia+ib));
            a.Velocity-=impulse*ia; b.Velocity+=impulse*ib;
            if(speed < -0.15f) { a.Wake(); b.Wake(); }
        }
    }

    private static bool SphereBox(Vector3 center,float radius,Vector3 min,Vector3 max,out Vector3 normal,out float depth)
    {
        Vector3 nearest=Vector3.Clamp(center,min,max),delta=center-nearest;
        float squared=delta.LengthSquared;
        if(squared>=radius*radius) { normal=default; depth=0f; return false; }
        if(squared>1e-12f)
        { float distance=MathF.Sqrt(squared); normal=delta/distance; depth=radius-distance; return true; }
        int axis=0; float closest=float.PositiveInfinity; normal=Vector3.UnitY;
        for(int a=0;a<3;a++)
        {
            float low=center[a]-min[a],high=max[a]-center[a];
            if(MathF.Min(low,high)<closest) { axis=a; closest=MathF.Min(low,high); normal=Vector3.Zero; normal[axis]=low<high?-1f:1f; }
        }
        depth=radius+closest; return true;
    }
}
