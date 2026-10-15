using OpenTK.Mathematics;

namespace Sniper3D;

/// <summary>Visual tessellation only. Collision/holes retain analytic precision.</summary>
internal static class SteelPlateGeometry
{
    public static (float[] Vertices, uint[] Indices) Build(SteelPlateTarget plate)
    {
        const int cells = 320; // 3.125 mm; rebuilt on damage, not each frame.
        const float step = SteelPlateTarget.DiameterMeters / cells;
        const float radius = SteelPlateTarget.RadiusMeters;
        var vertices = new List<float>(6_000_000);
        for (int x = 0; x < cells; x++)
        for (int y = 0; y < cells; y++)
        {
            float px = -radius + x * step, py = -radius + y * step;
            Vector2 center = new(px + step * 0.5f, py + step * 0.5f);
            if (center.LengthSquared > (radius + step) * (radius + step))
                continue;
            Vector2 a = OnDisc(new(px, py));
            Vector2 b = OnDisc(new(px + step, py));
            Vector2 c = OnDisc(new(px + step, py + step));
            Vector2 d = OnDisc(new(px, py + step));
            for (int face = 0; face < 2; face++)
            {
                float sign = face == 0 ? 1f : -1f;
                Vector3 va = new(a.X, a.Y, plate.SurfaceZ(a, sign));
                Vector3 vb = new(b.X, b.Y, plate.SurfaceZ(b, sign));
                Vector3 vc = new(c.X, c.Y, plate.SurfaceZ(c, sign));
                Vector3 vd = new(d.X, d.Y, plate.SurfaceZ(d, sign));
                if (sign > 0f)
                {
                    Surface(va, vb, vc);
                    Surface(va, vc, vd);
                }
                else
                {
                    Surface(va, vc, vb);
                    Surface(va, vd, vc);
                }
            }
        }

        const int rimSegments = 512;
        for (int i = 0; i < rimSegments; i++)
        {
            float angle0 = i * MathF.Tau / rimSegments;
            float angle1 = (i + 1) * MathF.Tau / rimSegments;
            Vector2 a = new(MathF.Cos(angle0) * radius, MathF.Sin(angle0) * radius);
            Vector2 b = new(MathF.Cos(angle1) * radius, MathF.Sin(angle1) * radius);
            Vector3 af = new(a.X, a.Y, plate.SurfaceZ(a, 1f)), ab = new(a.X, a.Y, plate.SurfaceZ(a, -1f));
            Vector3 bf = new(b.X, b.Y, plate.SurfaceZ(b, 1f)), bb = new(b.X, b.Y, plate.SurfaceZ(b, -1f));
            Surface(af, ab, bb);
            Surface(af, bb, bf);
        }

        // Bore walls give perforations a real 10 mm depth from the observer camera.
        foreach (SteelHole hole in plate.Holes)
        {
            if (MathF.Abs(hole.Axis.Z) < 0.001f)
                continue;
            Vector3 u = Vector3.Normalize(Vector3.Cross(hole.Axis, Vector3.UnitY));
            Vector3 v = Vector3.Cross(hole.Axis, u);
            for (int i = 0; i < 32; i++)
            {
                Vector3 p = Offset(i), q = Offset(i + 1);
                Vector3 af = AtFace(p, 1f), ab = AtFace(p, -1f);
                Vector3 bf = AtFace(q, 1f), bb = AtFace(q, -1f);
                Triangle(af, bf, bb);
                Triangle(af, bb, ab);
            }
            Vector3 Offset(int i) => (u * MathF.Cos(i * MathF.Tau / 32f)
                + v * MathF.Sin(i * MathF.Tau / 32f)) * hole.Radius;
            Vector3 AtFace(Vector3 p, float sign)
            {
                Vector3 basePoint = hole.Entry + p;
                return basePoint + hole.Axis * ((sign * SteelPlateTarget.ThicknessMeters * 0.5f - basePoint.Z) / hole.Axis.Z);
            }
        }

        float[] data = vertices.ToArray();
        uint[] indices = new uint[data.Length / 6];
        for (uint i = 0; i < indices.Length; i++)
            indices[i] = i;
        return (data, indices);

        void Surface(Vector3 a, Vector3 b, Vector3 c)
        {
            if (!plate.IsHole((a + b + c) / 3f))
                Triangle(a, b, c);
        }
        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            if (a.Xy.LengthSquared > radius * radius + 1e-6f ||
                b.Xy.LengthSquared > radius * radius + 1e-6f ||
                c.Xy.LengthSquared > radius * radius + 1e-6f)
                return;
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared < 1e-16f)
                return;
            normal = Vector3.Normalize(normal);
            Add(a, normal); Add(b, normal); Add(c, normal);
        }
        void Add(Vector3 p, Vector3 n)
        {
            vertices.Add(p.X); vertices.Add(p.Y); vertices.Add(p.Z);
            vertices.Add(n.X); vertices.Add(n.Y); vertices.Add(n.Z);
        }
        static Vector2 OnDisc(Vector2 p) => p.LengthSquared > radius * radius ? Vector2.Normalize(p) * radius : p;
    }
}
