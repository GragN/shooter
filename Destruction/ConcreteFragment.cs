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
        MassKg = MathF.Max(0.01f, massKg);
    }

    /// <summary>
    /// Радиус ограничивающей сферы. Используется для дешёвых столкновений
    /// вращающегося обломка с землёй, стеной и оставшимся бетоном.
    /// </summary>
    public float CollisionRadiusMeters
        => 0.5f * SizeMeters.Length;

    public float SpeedMps => Velocity.Length;

    public void Wake()
    {
        Sleeping = false;
        SleepTimerSeconds = 0f;
    }
}
