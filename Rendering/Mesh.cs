using OpenTK.Graphics.OpenGL4;

namespace Sniper3D;

internal sealed class Mesh : IDisposable
{
    private readonly int _vao;
    private readonly int _vbo;
    private readonly int _ebo;
    private readonly int _indexCount;

    internal Mesh(float[] vertices, uint[] indices, bool vertexColors = false)
    {
        _indexCount = indices.Length;

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        _ebo = GL.GenBuffer();

        GL.BindVertexArray(_vao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

        int stride = (vertexColors ? 9 : 6) * sizeof(float);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);

        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        if (vertexColors)
        {
            GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
            GL.EnableVertexAttribArray(2);
        }

        GL.BindVertexArray(0);
    }

    public static Mesh CreateCube()
    {
        // 24 вершины вместо 8: у каждой грани собственная нормаль,
        // благодаря чему бетон и вращающиеся обломки выглядят объёмными.
        float[] vertices =
        {
            // -Z
            -0.5f, -0.5f, -0.5f,   0f,  0f, -1f,
             0.5f, -0.5f, -0.5f,   0f,  0f, -1f,
             0.5f,  0.5f, -0.5f,   0f,  0f, -1f,
            -0.5f,  0.5f, -0.5f,   0f,  0f, -1f,

            // +Z
            -0.5f, -0.5f,  0.5f,   0f,  0f,  1f,
             0.5f, -0.5f,  0.5f,   0f,  0f,  1f,
             0.5f,  0.5f,  0.5f,   0f,  0f,  1f,
            -0.5f,  0.5f,  0.5f,   0f,  0f,  1f,

            // -X
            -0.5f, -0.5f, -0.5f,  -1f,  0f,  0f,
            -0.5f,  0.5f, -0.5f,  -1f,  0f,  0f,
            -0.5f,  0.5f,  0.5f,  -1f,  0f,  0f,
            -0.5f, -0.5f,  0.5f,  -1f,  0f,  0f,

            // +X
             0.5f, -0.5f, -0.5f,   1f,  0f,  0f,
             0.5f, -0.5f,  0.5f,   1f,  0f,  0f,
             0.5f,  0.5f,  0.5f,   1f,  0f,  0f,
             0.5f,  0.5f, -0.5f,   1f,  0f,  0f,

            // -Y
            -0.5f, -0.5f, -0.5f,   0f, -1f,  0f,
            -0.5f, -0.5f,  0.5f,   0f, -1f,  0f,
             0.5f, -0.5f,  0.5f,   0f, -1f,  0f,
             0.5f, -0.5f, -0.5f,   0f, -1f,  0f,

            // +Y
            -0.5f,  0.5f, -0.5f,   0f,  1f,  0f,
             0.5f,  0.5f, -0.5f,   0f,  1f,  0f,
             0.5f,  0.5f,  0.5f,   0f,  1f,  0f,
            -0.5f,  0.5f,  0.5f,   0f,  1f,  0f
        };

        uint[] indices =
        {
             0,  1,  2,  2,  3,  0,
             4,  6,  5,  6,  4,  7,
             8,  9, 10, 10, 11,  8,
            12, 13, 14, 14, 15, 12,
            16, 17, 18, 18, 19, 16,
            20, 21, 22, 22, 23, 20
        };

        return new Mesh(vertices, indices);
    }

    public static Mesh CreateDisc(int segments = 64)
    {
        if (segments < 3)
            throw new ArgumentOutOfRangeException(nameof(segments));

        var vertices = new List<float>((segments + 1) * 6)
        {
            0f, 0f, 0f,  0f, 0f, 1f
        };

        for (int i = 0; i < segments; i++)
        {
            float angle = MathF.Tau * i / segments;
            vertices.Add(MathF.Cos(angle));
            vertices.Add(MathF.Sin(angle));
            vertices.Add(0f);
            vertices.Add(0f);
            vertices.Add(0f);
            vertices.Add(1f);
        }

        var indices = new List<uint>(segments * 3);
        for (uint i = 0; i < segments; i++)
        {
            uint next = (i + 1) % (uint)segments;
            indices.Add(0);
            indices.Add(i + 1);
            indices.Add(next + 1);
        }

        return new Mesh(vertices.ToArray(), indices.ToArray());
    }

    public void Draw()
    {
        GL.BindVertexArray(_vao);
        GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, 0);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        GL.DeleteVertexArray(_vao);
        GL.DeleteBuffer(_vbo);
        GL.DeleteBuffer(_ebo);
    }
}
