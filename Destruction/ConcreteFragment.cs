using OpenTK.Mathematics;

namespace Sniper3D;

/// <summary>
/// Отделившийся кусок бетона. Это небольшой rigid-body-подобный объект:
/// у него есть масса, линейная и угловая скорость, вращение и состояние сна.
/// Геометрия фрагмента пока приближена неровным прямоугольным куском.
/// </summary>
internal sealed class ConcreteFragment
{
    public Vector3 Position;
    public Vector3 Velocity;
    public Vector3 RotationRadians;
    public Vector3 AngularVelocityRadPerSec;

    public Vector3 SizeMeters { get; }
    public Vector3 Color { get; }
    public float MassKg { get; }
    public float AgeSeconds { get; set; }
    public bool Sleeping { get; set; }
    public float SleepTimerSeconds { get; set; }
    public Vector3[]? LocalVoxelCenters { get; init; }
    public float VoxelSizeMeters { get; init; }
    public Vector3 LocalBoundsCenter { get; init; }
    public Vector3 InertiaOverride { get; init; }
    public float FractureEnergyDebtJoules { get; set; }
    public Vector3 Inertia => InertiaOverride.X > 0f ? InertiaOverride : new Vector3(SizeMeters.Y * SizeMeters.Y + SizeMeters.Z * SizeMeters.Z,
        SizeMeters.X * SizeMeters.X + SizeMeters.Z * SizeMeters.Z,
        SizeMeters.X * SizeMeters.X + SizeMeters.Y * SizeMeters.Y) * (MassKg / 12f);
    public float KineticEnergyJoules
    {
        get
        {
            Matrix4 rotation = Matrix4.CreateRotationX(RotationRadians.X)
                * Matrix4.CreateRotationY(RotationRadians.Y) * Matrix4.CreateRotationZ(RotationRadians.Z);
            Vector3 spin = Vector3.TransformNormal(AngularVelocityRadPerSec, rotation.Inverted());
            return 0.5f * MassKg * Velocity.LengthSquared + 0.5f * Vector3.Dot(Inertia, spin * spin);
        }
    }

    public ConcreteFragment(
        Vector3 position,
        Vector3 velocity,
        Vector3 rotationRadians,
        Vector3 angularVelocityRadPerSec,
        Vector3 sizeMeters,
        Vector3 color,
        float massKg)
    {
        Position = position;
        Velocity = velocity;
        RotationRadians = rotationRadians;
        AngularVelocityRadPerSec = angularVelocityRadPerSec;
        SizeMeters = sizeMeters;
        Color = color;
        MassKg = MathF.Max(1e-7f, massKg);
    }

    /// <summary>
    /// Радиус ограничивающей сферы. Используется для дешёвых столкновений
    /// вращающегося обломка с землёй, стеной и оставшимся бетоном.
    /// </summary>
    public float CollisionRadiusMeters
        => 0.5f * SizeMeters.Length + LocalBoundsCenter.Length;

    public float SpeedMps => Velocity.Length;

    public void Wake()
    {
        Sleeping = false;
        SleepTimerSeconds = 0f;
    }
}
