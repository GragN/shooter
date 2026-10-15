using OpenTK.Mathematics;

namespace Sniper3D;

/// <summary>
/// Непрерывный ray/segment traversal по равномерной 3D-воксельной сетке.
/// Использует 3D DDA (Amanatides-Woo): ни одна пересечённая ячейка не
/// пропускается, даже если пуля за один физический шаг пролетает несколько
/// десятков сантиметров.
/// </summary>
internal static class VoxelSegmentCaster
{
    private const float Epsilon = 1e-7f;

    public static bool TryHit(
        bool[,,] solid,
        Vector3 gridMin,
        float cellSize,
        Vector3 from,
        Vector3 to,
        out float hitT,
        out Vector3 hitPoint)
    {
        hitT = 0f;
        hitPoint = default;

        int nx = solid.GetLength(0);
        int ny = solid.GetLength(1);
        int nz = solid.GetLength(2);
        if (nx <= 0 || ny <= 0 || nz <= 0 || cellSize <= 0f)
            return false;

        Vector3 gridMax = gridMin + new Vector3(nx * cellSize, ny * cellSize, nz * cellSize);
        if (!TrySegmentAabb(from, to, gridMin, gridMax, out float enterT, out float exitT))
            return false;

        enterT = MathHelper.Clamp(enterT, 0f, 1f);
        exitT = MathHelper.Clamp(exitT, 0f, 1f);
        if (enterT > exitT)
            return false;

        Vector3 d = to - from;
        float segmentLength = d.Length;
        if (segmentLength < 1e-8f)
        {
            if (!TryWorldToCell(from, gridMin, cellSize, nx, ny, nz, out int px, out int py, out int pz))
                return false;

            if (!solid[px, py, pz])
                return false;

            hitPoint = from;
            hitT = 0f;
            return true;
        }

        // Чуть заходим внутрь AABB только для выбора стартовой ячейки.
        // Сам hitT остаётся точным enterT.
        float probeStepT = MathF.Max(1e-7f, 1e-5f / segmentLength);
        float probeT = MathF.Min(exitT, enterT + probeStepT);
        Vector3 probe = from + d * probeT;

        int x = ClampCell((int)MathF.Floor((probe.X - gridMin.X) / cellSize), nx);
        int y = ClampCell((int)MathF.Floor((probe.Y - gridMin.Y) / cellSize), ny);
        int z = ClampCell((int)MathF.Floor((probe.Z - gridMin.Z) / cellSize), nz);

        int stepX = Math.Sign(d.X);
        int stepY = Math.Sign(d.Y);
        int stepZ = Math.Sign(d.Z);

        float tMaxX = InitialBoundaryT(from.X, d.X, gridMin.X, cellSize, x, stepX);
        float tMaxY = InitialBoundaryT(from.Y, d.Y, gridMin.Y, cellSize, y, stepY);
        float tMaxZ = InitialBoundaryT(from.Z, d.Z, gridMin.Z, cellSize, z, stepZ);

        float tDeltaX = stepX == 0 ? float.PositiveInfinity : cellSize / MathF.Abs(d.X);
        float tDeltaY = stepY == 0 ? float.PositiveInfinity : cellSize / MathF.Abs(d.Y);
        float tDeltaZ = stepZ == 0 ? float.PositiveInfinity : cellSize / MathF.Abs(d.Z);

        // Из-за численной погрешности граница может оказаться на несколько ulp
        // позади enterT. Подтягиваем её вперёд, не меняя порядок обхода.
        while (tMaxX < enterT - Epsilon) tMaxX += tDeltaX;
        while (tMaxY < enterT - Epsilon) tMaxY += tDeltaY;
        while (tMaxZ < enterT - Epsilon) tMaxZ += tDeltaZ;

        float currentT = enterT;
        int maxSteps = nx + ny + nz + 16;

        for (int iteration = 0; iteration < maxSteps; iteration++)
        {
            if (x < 0 || x >= nx || y < 0 || y >= ny || z < 0 || z >= nz)
                return false;

            if (solid[x, y, z])
            {
                hitT = MathHelper.Clamp(currentT, 0f, 1f);
                hitPoint = from + d * hitT;
                return true;
            }

            float nextT = MathF.Min(tMaxX, MathF.Min(tMaxY, tMaxZ));
            if (float.IsPositiveInfinity(nextT) || nextT > exitT + Epsilon)
                break;

            currentT = MathF.Max(currentT, nextT);

            // На ребре/углу луч может одновременно пересечь 2–3 границы.
            // Продвигаем все соответствующие оси, чтобы не потерять ячейку.
            if (tMaxX <= nextT + Epsilon)
            {
                x += stepX;
                tMaxX += tDeltaX;
            }
            if (tMaxY <= nextT + Epsilon)
            {
                y += stepY;
                tMaxY += tDeltaY;
            }
            if (tMaxZ <= nextT + Epsilon)
            {
                z += stepZ;
                tMaxZ += tDeltaZ;
            }
        }

        return false;
    }

    private static int ClampCell(int value, int count)
        => Math.Clamp(value, 0, count - 1);

    private static bool TryWorldToCell(
        Vector3 point,
        Vector3 gridMin,
        float cellSize,
        int nx,
        int ny,
        int nz,
        out int x,
        out int y,
        out int z)
    {
        x = (int)MathF.Floor((point.X - gridMin.X) / cellSize);
        y = (int)MathF.Floor((point.Y - gridMin.Y) / cellSize);
        z = (int)MathF.Floor((point.Z - gridMin.Z) / cellSize);
        return x >= 0 && x < nx && y >= 0 && y < ny && z >= 0 && z < nz;
    }

    private static float InitialBoundaryT(
        float origin,
        float direction,
        float gridMin,
        float cellSize,
        int cell,
        int step)
    {
        if (step == 0 || MathF.Abs(direction) < 1e-12f)
            return float.PositiveInfinity;

        float boundary = gridMin + (step > 0 ? cell + 1 : cell) * cellSize;
        return (boundary - origin) / direction;
    }

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

            if (MathF.Abs(delta) < 1e-12f)
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
}
