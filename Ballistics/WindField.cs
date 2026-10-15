using OpenTK.Mathematics;

namespace Sniper3D;

/// <summary>
/// Плавно меняющееся пространственно-временное поле ветра.
///
/// Скорость ветра задаётся относительно активного лимита:
///   средняя/базовая доля = 85% лимита;
///   плавные колебания     = +/-15% лимита;
///   итоговый диапазон     = 70..100% лимита.
///
/// Лимит не хранится внутри WindField: он передаётся явно в каждый Sample(),
/// поэтому HUD и физика пули используют один и тот же предел.
/// </summary>
internal sealed class WindField
{
    private readonly Random _random;
    private Vector3 _prevailingWind;
    private Vector3 _targetPrevailingWind;
    private float _secondsUntilChange;

    public float TimeSeconds { get; private set; }

    public WindField(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();

        // Здесь величина нужна в основном для формирования плавно меняющегося
        // направления. Финальная скорость задаётся отдельно как доля лимита.
        _prevailingWind = RandomHorizontalWind(3.5f, 7.0f);
        _targetPrevailingWind = RandomHorizontalWind(3.0f, 8.5f);
        _secondsUntilChange = NextChangeInterval();
    }

    public void Update(float dt)
    {
        TimeSeconds += dt;
        _secondsUntilChange -= dt;

        if (_secondsUntilChange <= 0f)
        {
            _targetPrevailingWind = RandomHorizontalWind(2.0f, 9.0f);
            _secondsUntilChange = NextChangeInterval();
        }

        // Очень медленное изменение преобладающего направления ветра.
        // Новое направление выбирается примерно раз в 45-90 секунд, а сам
        // переход занимает десятки секунд вместо резких игровых поворотов.
        const float response = 0.030f;
        float alpha = 1f - MathF.Exp(-response * dt);
        _prevailingWind += (_targetPrevailingWind - _prevailingWind) * alpha;
    }

    /// <summary>
    /// Возвращает локальный ветер.
    /// При limit > 0 его модуль всегда находится в диапазоне 70..100% limit.
    /// При limit == 0 ветер полностью отключён.
    /// </summary>
    public Vector3 Sample(Vector3 position, float timeSeconds, float speedLimitMps)
    {
        float limit = WindMath.SanitizeLimit(speedLimitMps);
        if (limit <= 0f)
            return Vector3.Zero;

        // --- 1. Плавно меняющееся направление ---
        Vector3 horizontal = new(_prevailingWind.X, 0f, _prevailingWind.Z);
        float horizontalLength = horizontal.Length;

        if (horizontalLength < 0.001f)
            horizontal = Vector3.UnitX;
        else
            horizontal /= horizontalLength;

        Vector3 perpendicular = new(-horizontal.Z, 0f, horizontal.X);

        // Локальный поворот ветра вдоль дистанции и со временем.
        float crossSignal =
            0.65f * MathF.Sin(position.Z * 0.010f - timeSeconds * 0.035f + 1.7f) +
            0.35f * MathF.Sin(position.Z * 0.021f + timeSeconds * 0.055f + position.X * 0.035f);

        // Небольшая вертикальная составляющая. Она влияет на направление,
        // но не может увеличить модуль выше заданной скорости.
        float verticalSignal =
            0.70f * MathF.Sin(timeSeconds * 0.080f + position.Z * 0.016f) +
            0.30f * MathF.Sin(timeSeconds * 0.130f - position.Z * 0.009f + 0.8f);

        Vector3 direction =
            horizontal +
            perpendicular * (0.24f * crossSignal) +
            Vector3.UnitY * (0.035f * verticalSignal);

        float directionLengthSquared = direction.LengthSquared;
        if (!float.IsFinite(directionLengthSquared) || directionLengthSquared < 0.000001f)
            direction = horizontal;
        else
            direction /= MathF.Sqrt(directionLengthSquared);

        // --- 2. Скорость: 85% +/- 15% от ограничения ---
        // Весовые коэффициенты дают сигнал строго в диапазоне [-1; +1].
        // Среднее значение сигнала около нуля, поэтому средняя скорость ~85%.
        float fluctuationSignal =
            0.50f * MathF.Sin(timeSeconds * 0.31f + position.Z * 0.0045f) +
            0.30f * MathF.Sin(timeSeconds * 0.79f - position.Z * 0.0080f + 1.9f) +
            0.20f * MathF.Sin(timeSeconds * 1.63f + position.Z * 0.0140f + position.X * 0.025f + 4.2f);

        fluctuationSignal = MathHelper.Clamp(fluctuationSignal, -1f, 1f);

        // 0.85 +/- 0.15 => строго 0.70 .. 1.00 от лимита.
        float speedFraction = 0.85f + 0.15f * fluctuationSignal;
        float speed = limit * speedFraction;

        Vector3 wind = direction * speed;

        // Финальная страховка: даже при ошибке вычислений модуль не превысит limit.
        return WindMath.ClampVelocity(wind, limit);
    }

    public Vector3 SampleNow(Vector3 position, float speedLimitMps)
        => Sample(position, TimeSeconds, speedLimitMps);

    private Vector3 RandomHorizontalWind(float minSpeed, float maxSpeed)
    {
        float angle = (float)(_random.NextDouble() * MathF.Tau);
        float speed = minSpeed + (float)_random.NextDouble() * (maxSpeed - minSpeed);

        return new Vector3(
            MathF.Cos(angle) * speed,
            0f,
            MathF.Sin(angle) * speed);
    }

    private float NextChangeInterval()
        => 45f + (float)_random.NextDouble() * 45f;
}
