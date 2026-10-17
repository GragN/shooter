using System.Reflection;
using OpenTK.Mathematics;
using Sniper3D;

internal static class ConcretePhysicsTests
{
    public static void Run(Action<string,Action> run)
    {
        run("Concrete: cavity resistance, strength and energy trends",()=>
        {
            var weak=Material(20e6f); var strong=Material(60e6f);
            var p=Projectile(WeaponProfile.Svd(),820f);
            var a=ConcreteCubeImpactModel.Solve(weak,p,1f,Vector3.UnitZ);
            var b=ConcreteCubeImpactModel.Solve(strong,p,1f,Vector3.UnitZ);
            Check(a.PenetrationDepth>b.PenetrationDepth && !a.Perforated && !b.Perforated);
            var slow=ConcreteCubeImpactModel.Solve(weak,Projectile(WeaponProfile.Svd(),400f),1f,Vector3.UnitZ);
            Check(a.PenetrationDepth>slow.PenetrationDepth);
        });
        run("Concrete: intact 1 m cube stops all catalogued nominal projectiles",()=>
        {
            foreach(var weapon in Weapons())
            {
                var result=ConcreteCubeImpactModel.Solve(MaterialProfile.NormalConcrete,
                    Projectile(weapon,weapon.Ballistics.MuzzleVelocityMps),1f,Vector3.UnitZ);
                Check(!result.Perforated && result.ResidualEnergy==0f && result.PenetrationDepth<1f);
            }
        });
        run("Concrete: partial perforation loses energy monotonically",()=>
        {
            var p=Projectile(WeaponProfile.Svd(),820f); var material=MaterialProfile.NormalConcrete;
            float depth=ConcreteCubeImpactModel.Solve(material,p,1f,Vector3.UnitZ).PenetrationDepth;
            var thin=ConcreteCubeImpactModel.Solve(material,p,depth*0.2f,Vector3.UnitZ);
            var thick=ConcreteCubeImpactModel.Solve(material,p,depth*0.8f,Vector3.UnitZ);
            Check(thin.Perforated && thick.Perforated && thin.ResidualEnergy>thick.ResidualEnergy);
            Near(thin.ResidualEnergy+thin.DepositedEnergy,p.KineticEnergyJoules,0.01);
        });
        run("Concrete: grazing hits deflect with dissipated energy",()=>
        {
            var weapon=WeaponProfile.Svd(); float a=MathHelper.DegreesToRadians(78f);
            var p=ProjectileImpactData.From(weapon.Ballistics,new Vector3(MathF.Sin(a),0f,-MathF.Cos(a))*500f);
            var result=ConcreteCubeImpactModel.Solve(MaterialProfile.NormalConcrete,p,1f,Vector3.UnitZ);
            Check(result.Ricochet && result.ExitVelocity.Z>0f && result.ResidualEnergy<p.KineticEnergyJoules);
        });
        run("Concrete: exact DDA intervals sum to geometric solid thickness",()=>
        {
            var ray=VoxelRayTrace.Trace(new(0.01f,0.013f,2f),-Vector3.UnitZ,new(-0.5f),40,0.025f,4f);
            Check(ray.Count==40);
            Near(ray.Sum(c=>c.Exit-c.Entry),1f,1e-5);
            var diagonal=VoxelRayTrace.Trace(new(-1f,-1f,-1f),Vector3.Normalize(Vector3.One),new(-0.5f),40,0.025f,4f);
            Near(diagonal.Sum(c=>c.Exit-c.Entry),Math.Sqrt(3),1e-5);
        });
        run("Concrete: PM makes sub-voxel chips without exploding cells",()=>
        {
            var target=new Target(); var hit=Hit(target,WeaponProfile.MakarovPm());
            Check(hit.PenetrationDepthMeters<target.CellSizeMeters);
            Check(target.DetachedMassKg>0 && target.DetachedMassKg<0.02);
            Check(target.Integrity01>0.999f && !hit.Perforated && !hit.Ricochet);
            MassBalance(target);
        });
        run("Concrete: .50 causes a deeper larger cavity than PM",()=>
        {
            var pm=new Target(); var big=new Target();
            var a=Hit(pm,WeaponProfile.MakarovPm()); var b=Hit(big,WeaponProfile.BarrettM82A1());
            Check(b.PenetrationDepthMeters>a.PenetrationDepthMeters);
            Check(big.DetachedMassKg>pm.DetachedMassKg && b.RemovedCellCount>0);
        });
        run("Concrete: translation + spin + fracture stay inside impact energy",()=>
        {
            foreach(var weapon in Weapons())
            {
                var target=new Target(); var result=Hit(target,weapon);
                double live=target.Fragments.Sum(f=>(double)f.KineticEnergyJoules);
                Check(live<=result.DepositedEnergyJoules*0.025+0.01);
                Check(result.CrushingWorkJoules+result.FractureWorkJoules+live<=result.DepositedEnergyJoules+0.01);
                Near(live,result.DebrisKineticEnergyJoules,0.01);
                MassBalance(target);
            }
        });
        run("Concrete: repeated shots excavate the surface progressively",()=>
        {
            var target=new Target(); float first=0f,last=0f;
            for(int i=0;i<5;i++)
            {
                Vector3 contact=Contact(target);
                if(i==0)first=contact.Z; last=contact.Z;
                Hit(target,WeaponProfile.BarrettM82A1());
            }
            Check(last<first-0.05f);
            MassBalance(target);
        });
        run("Concrete: 60 repeated hits conserve attached/live/retired mass",()=>
        {
            var target=new Target();
            for(int i=0;i<60;i++)
            {
                float x=-0.25f+(i%8)*0.065f,y=-0.2f+(i/8)*0.05f;
                Hit(target,WeaponProfile.BarrettM82A1(),new(x,y,0f));
                MassBalance(target);
            }
            Check(target.AttachedMassKg>2000 && target.Fragments.Count<=600);
        });
        run("Concrete: reset restores full mass and clears all fields",()=>
        {
            var target=new Target(); Hit(target,WeaponProfile.BarrettM82A1()); int revision=target.Revision;
            target.ResetDamage();
            Check(target.Revision>revision && target.Fragments.Count==0 && target.IntactCellCount==target.TotalCellCount);
            Near(target.AttachedMassKg,2400,1e-6); Near(target.DetachedMassKg,0,1e-6);
        });
        run("Concrete: debris has no invisible rear wall",()=>
        {
            var target=new Target(); var body=Body(target.Position+new Vector3(0f,10f,-11f),new(0f,0f,-5f));
            Fragments(target).Add(body);
            for(int i=0;i<120;i++)target.UpdatePhysics(1f/120f,-100f);
            Check(body.Position.Z<target.Position.Z-15f && body.Velocity.Z<0f);
        });
        run("Concrete: resting debris actually goes to sleep",()=>
        {
            var target=new Target(); var body=Body(new(5f,0.025f,0f),Vector3.Zero);
            Fragments(target).Add(body);
            for(int i=0;i<180;i++)target.UpdatePhysics(1f/120f,0f);
            Check(body.Sleeping && body.Velocity.LengthSquared==0f);
        });
        run("Concrete: ground impact dissipates total mechanical energy",()=>
        {
            var target=new Target(); var body=Body(new(5f,0.25f,0f),new(2f,-1f,1f));
            Fragments(target).Add(body);
            double initial=body.KineticEnergyJoules+body.MassKg*9.80665*body.Position.Y;
            for(int i=0;i<120;i++)target.UpdatePhysics(1f/120f,0f);
            double final=body.KineticEnergyJoules+body.MassKg*9.80665*body.Position.Y;
            Check(final<=initial+0.01 && float.IsFinite(body.KineticEnergyJoules));
        });
        run("Concrete: connected detached upper half is one structural component",()=>
        {
            var target=new Target(); var field=typeof(Target).GetField("_intact",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var intact=(bool[,,])field.GetValue(target)!; int n=target.GridResolution;
            for(int x=0;x<n;x++)for(int z=0;z<n;z++)intact[x,n/2,z]=false;
            var method=typeof(Target).GetMethod("FindUnsupportedComponents",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var parts=(List<List<Vector3i>>)method.Invoke(target,null)!;
            Check(parts.Count==1 && parts[0].Count==n*n*(n/2-1));
        });
        run("Concrete: intact fine grid renders only six exterior surfaces",()=>
        {
            var target=new Target(); var mesh=ConcreteMeshGeometry.Build(target); int n=target.GridResolution;
            Check(mesh.Indices.Length==6*n*n*6 && mesh.Vertices.Length==mesh.Indices.Length*9);
            for(int i=0;i<mesh.Vertices.Length;i+=9)
            {
                Vector3 p=new(mesh.Vertices[i],mesh.Vertices[i+1],mesh.Vertices[i+2]);
                Check(MathF.Abs(p.X)<=0.50001f && MathF.Abs(p.Y)<=0.50001f && MathF.Abs(p.Z)<=0.50001f);
                Near(new Vector3(mesh.Vertices[i+3],mesh.Vertices[i+4],mesh.Vertices[i+5]).Length,1f,1e-5);
            }
        });
        run("Concrete: intact cube carries its own weight without bond failure",()=>
        {
            var target=new Target();
            typeof(Target).GetMethod("CheckGravityCapacity",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,null);
            Near(target.GravityFractureWorkJoules,0f,1e-7);
        });
        run("Concrete: an eccentric one-cell neck cannot hold a tonne of concrete",()=>
        {
            var target=new Target(); int n=target.GridResolution;
            var intact=(bool[,,])typeof(Target).GetField("_intact",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target)!;
            for(int x=0;x<n;x++)for(int z=0;z<n;z++)intact[x,n/2,z]=x==n-1 && z==n/2;
            typeof(Target).GetMethod("CheckGravityCapacity",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,null);
            var parts=(List<List<Vector3i>>)typeof(Target).GetMethod("FindUnsupportedComponents",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,null)!;
            Check(target.GravityFractureWorkJoules>0f && parts.Count>0 && parts.Max(p=>p.Count)>n*n*10);
        });
        run("Concrete: fixed-step debris agrees at 30 and 120 frame updates",()=>
        {
            var a=new Target();var b=new Target();
            var fa=Body(new(5f,5f,0f),new(1f,0f,-2f));var fb=Body(new(5f,5f,0f),new(1f,0f,-2f));
            Fragments(a).Add(fa);Fragments(b).Add(fb);
            for(int i=0;i<30;i++)a.UpdatePhysics(1f/30f,0f);
            for(int i=0;i<120;i++)b.UpdatePhysics(1f/120f,0f);
            Check((fa.Position-fb.Position).Length<0.01f && (fa.Velocity-fb.Velocity).Length<0.01f);
        });
    }

    private static ConcreteImpactResult Hit(Target target,WeaponProfile weapon,Vector3 offset=default)
        => target.ApplyImpact(Contact(target,offset),Projectile(weapon,weapon.Ballistics.MuzzleVelocityMps));
    private static Vector3 Contact(Target target,Vector3 offset=default)
    {
        Vector3 p=target.Position+offset;
        Check(target.TryHitSegment(p+Vector3.UnitZ*2f,p-Vector3.UnitZ*2f,out _,out Vector3 contact));
        return contact;
    }
    private static void MassBalance(Target target)
    {
        Near(target.AttachedMassKg+target.DetachedMassKg,target.InitialMassKg,0.002);
        Near(target.Fragments.Sum(f=>(double)f.MassKg)+target.ArchivedMassKg,target.DetachedMassKg,0.002);
    }
    private static List<ConcreteFragment> Fragments(Target t) => (List<ConcreteFragment>)typeof(Target)
        .GetField("_fragments",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(t)!;
    private static ConcreteFragment Body(Vector3 p,Vector3 v) => new(p,v,Vector3.Zero,Vector3.Zero,new(0.05f),new(0.4f),0.3f);
    private static MaterialProfile Material(float strength) => new()
    {Name="TEST",DensityKgPerM3=2400f,CompressiveStrengthPa=strength,TensileStrengthPa=3.6e6f,YoungModulusPa=30e9f,FractureEnergyJPerM2=120f};
    private static ProjectileImpactData Projectile(WeaponProfile w,float speed) => ProjectileImpactData.From(w.Ballistics,-Vector3.UnitZ*speed);
    private static IEnumerable<WeaponProfile> Weapons() => WeaponProfile.CreateAll();
    private static void Check(bool value) {if(!value)throw new Exception("Assertion failed");}
    private static void Near(double a,double b,double epsilon) {if(!double.IsFinite(a)||Math.Abs(a-b)>epsilon)throw new Exception($"Expected {b} +/- {epsilon}; got {a}");}
}
