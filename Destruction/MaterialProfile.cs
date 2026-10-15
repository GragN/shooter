namespace Sniper3D;

/// <summary>
/// Физические параметры разрушаемого материала. Значения хранятся в SI.
/// Это не FEM-модель: параметры используются физически мотивированной
/// терминальной баллистикой и воксельной моделью разрушения.
/// </summary>
internal sealed class MaterialProfile
{
    public required string Name { get; init; }
    public float DensityKgPerM3 { get; init; }
    public float CompressiveStrengthPa { get; init; }
    public float TensileStrengthPa { get; init; }
    public float YoungModulusPa { get; init; }
    public float FractureEnergyJPerM2 { get; init; }

    // При высокоскоростном проникновении материал вокруг канала зажат
    // окружающим массивом, поэтому эффективное сопротивление выше обычной
    // одноосной прочности на сжатие.
    public float DynamicConfinementFactor { get; init; } = 3.3f;

    // Доля кинетической энергии, которая эффективно выполняет работу
    // проникновения. Остальное уходит в трещины, нагрев, деформацию пули,
    // звук и разлёт мелких частиц.
    public float PenetrationEfficiency { get; init; } = 0.18f;

    public float FragmentEnergyFraction { get; init; } = 0.16f;
    public float Restitution { get; init; } = 0.12f;
    public float Friction { get; init; } = 0.72f;

    /// <summary>
    /// Неармированный бетон с номинальной прочностью на сжатие 40 МПа.
    /// Набор игровых параметров, а не аттестованный состав или точный класс.
    /// </summary>
    public static MaterialProfile NormalConcrete { get; } = new()
    {
        Name = "NORMAL CONCRETE",
        DensityKgPerM3 = 2400f,
        CompressiveStrengthPa = 40_000_000f,
        TensileStrengthPa = 3_600_000f,
        YoungModulusPa = 30_000_000_000f,
        FractureEnergyJPerM2 = 120f,
        DynamicConfinementFactor = 3.3f,
        PenetrationEfficiency = 0.18f,
        FragmentEnergyFraction = 0.16f,
        Restitution = 0.12f,
        Friction = 0.72f
    };
}
