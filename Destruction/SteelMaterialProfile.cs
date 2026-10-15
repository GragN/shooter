namespace Sniper3D;

/// <summary>Nominal S355 structural steel, not hardened ballistic target steel. SI units.</summary>
internal sealed record SteelMaterialProfile
{
    public float DensityKgPerM3 { get; init; } = 7850f;
    public float YieldStrengthPa { get; init; } = 355_000_000f;
    public float TensileStrengthPa { get; init; } = 510_000_000f;
    public float YoungModulusPa { get; init; } = 210_000_000_000f;
    // Engineering approximation of dynamic strengthening, not measured V50 data.
    public float DynamicStrengthFactor { get; init; } = 1.35f;
    public float DynamicFlowStressPa => (YieldStrengthPa + TensileStrengthPa) * 0.5f * DynamicStrengthFactor;
    public float DynamicShearStrengthPa => TensileStrengthPa / MathF.Sqrt(3f) * DynamicStrengthFactor;
}
