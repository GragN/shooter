using OpenTK.Mathematics;

namespace Sniper3D;

internal readonly record struct VoxelRayCell(int X, int Y, int Z, float Entry, float Exit);

/// <summary>Ordered exact cell intervals; distances are metres, not sample counts.</summary>
internal static class VoxelRayTrace
{
    public static List<VoxelRayCell> Trace(Vector3 origin, Vector3 direction, Vector3 gridMin, int count, float cell, float length)
    {
        var result = new List<VoxelRayCell>(count * 3);
        Vector3 p = origin - gridMin;
        float enter = 0f, exit = length, size = count * cell;
        for (int axis = 0; axis < 3; axis++)
        {
            if (MathF.Abs(direction[axis]) < 1e-9f)
            {
                if (p[axis] < 0f || p[axis] > size) return result;
            }
            else
            {
                float a = -p[axis] / direction[axis], b = (size - p[axis]) / direction[axis];
                enter = MathF.Max(enter, MathF.Min(a, b));
                exit = MathF.Min(exit, MathF.Max(a, b));
            }
        }
        if (enter >= exit) return result;
        Vector3 probe = p + direction * (enter + 1e-6f);
        Vector3i index = new((int)MathF.Floor(probe.X / cell), (int)MathF.Floor(probe.Y / cell), (int)MathF.Floor(probe.Z / cell));
        for (int a = 0; a < 3; a++) index[a] = Math.Clamp(index[a], 0, count - 1);
        Vector3 boundary = new(float.PositiveInfinity), increment = new(float.PositiveInfinity);
        for (int a = 0; a < 3; a++)
            if (MathF.Abs(direction[a]) >= 1e-9f)
            {
                float edge = (index[a] + (direction[a] > 0f ? 1 : 0)) * cell;
                boundary[a] = (edge - p[a]) / direction[a];
                increment[a] = cell / MathF.Abs(direction[a]);
            }
        for (int step = 0; step < count * 3 + 3 && enter < exit; step++)
        {
            if (index.X < 0 || index.Y < 0 || index.Z < 0 || index.X >= count || index.Y >= count || index.Z >= count) break;
            float next = MathF.Min(exit, MathF.Min(boundary.X, MathF.Min(boundary.Y, boundary.Z)));
            if (next > enter + 1e-7f) result.Add(new(index.X, index.Y, index.Z, enter, next));
            if (next >= exit) break;
            for (int a = 0; a < 3; a++)
                if (boundary[a] <= next + 1e-7f)
                {
                    index[a] += Math.Sign(direction[a]);
                    boundary[a] += increment[a];
                }
            enter = next;
        }
        return result;
    }
}
