using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Sniper3D;

internal sealed class TracerRenderer : IDisposable
{
    private const int MaxPoints = 128;

    private readonly int _vao;
    private readonly int _vbo;
    private readonly float[] _vertices = new float[MaxPoints * 3];

    public TracerRenderer()
    {
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(
            BufferTarget.ArrayBuffer,
            _vertices.Length * sizeof(float),
            IntPtr.Zero,
            BufferUsageHint.DynamicDraw);

        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.BindVertexArray(0);
    }

    public void Draw(
        IReadOnlyList<Vector3> points,
        Shader shader,
        Matrix4 view,
        Matrix4 projection,
        Vector3 color)
    {
        int count = Math.Min(points.Count, MaxPoints);
        if (count < 2)
            return;

        int first = points.Count - count;
        for (int i = 0; i < count; i++)
        {
            Vector3 p = points[first + i];
            int j = i * 3;
            _vertices[j] = p.X;
            _vertices[j + 1] = p.Y;
            _vertices[j + 2] = p.Z;
        }

        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferSubData(
            BufferTarget.ArrayBuffer,
            IntPtr.Zero,
            count * 3 * sizeof(float),
            _vertices);

        shader.Use();
        shader.SetMatrix4("model", Matrix4.Identity);
        shader.SetMatrix4("view", view);
        shader.SetMatrix4("projection", projection);
        shader.SetVector3("objectColor", color);

        GL.BindVertexArray(_vao);
        GL.LineWidth(2.5f);
        GL.DrawArrays(PrimitiveType.LineStrip, 0, count);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        GL.DeleteVertexArray(_vao);
        GL.DeleteBuffer(_vbo);
    }
}
