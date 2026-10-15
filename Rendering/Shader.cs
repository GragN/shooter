using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Sniper3D;

internal sealed class Shader : IDisposable
{
    private readonly int _handle;
    private readonly Dictionary<string, int> _uniformLocations = new(StringComparer.Ordinal);

    public Shader(string vertexSource, string fragmentSource)
    {
        int vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
        int fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);

        _handle = GL.CreateProgram();
        GL.AttachShader(_handle, vertexShader);
        GL.AttachShader(_handle, fragmentShader);
        GL.LinkProgram(_handle);

        GL.GetProgram(_handle, GetProgramParameterName.LinkStatus, out int success);
        if (success == 0)
        {
            string log = GL.GetProgramInfoLog(_handle);
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
            GL.DeleteProgram(_handle);
            throw new InvalidOperationException($"Shader link error: {log}");
        }

        GL.DetachShader(_handle, vertexShader);
        GL.DetachShader(_handle, fragmentShader);
        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);
    }

    public void Use() => GL.UseProgram(_handle);

    public void SetMatrix4(string name, Matrix4 value)
    {
        int location = GetUniformLocation(name);
        GL.UniformMatrix4(location, false, ref value);
    }

    public void SetVector3(string name, Vector3 value)
    {
        int location = GetUniformLocation(name);
        GL.Uniform3(location, value);
    }

    public void SetInt(string name, int value)
    {
        int location = GetUniformLocation(name);
        GL.Uniform1(location, value);
    }

    /// <summary>
    /// Uniform locations are stable for the lifetime of a linked program.
    /// Caching them avoids an OpenGL driver lookup for every voxel draw call.
    /// </summary>
    private int GetUniformLocation(string name)
    {
        if (_uniformLocations.TryGetValue(name, out int location))
            return location;

        location = GL.GetUniformLocation(_handle, name);
        _uniformLocations.Add(name, location);
        return location;
    }

    private static int CompileShader(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);

        GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
        if (success == 0)
        {
            string log = GL.GetShaderInfoLog(shader);
            GL.DeleteShader(shader);
            throw new InvalidOperationException($"{type} compile error: {log}");
        }

        return shader;
    }

    public void Dispose()
    {
        GL.DeleteProgram(_handle);
    }
}
