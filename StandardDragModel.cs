namespace Sniper3D;

internal enum DragModelKind
{
    G1,
    G7
}

/// <summary>
/// Стандартные G1/G7 reference drag curves: Cd как функция числа Маха.
/// Значения интерполируются линейно между узлами таблицы.
/// </summary>
internal static class StandardDragModel
{
    private readonly record struct DragPoint(float Mach, float Cd);

    private static readonly DragPoint[] G1 =
    {
        new(0.00f, 0.2629f), new(0.05f, 0.2558f), new(0.10f, 0.2487f), new(0.15f, 0.2413f),
        new(0.20f, 0.2344f), new(0.25f, 0.2278f), new(0.30f, 0.2214f), new(0.35f, 0.2155f),
        new(0.40f, 0.2104f), new(0.45f, 0.2061f), new(0.50f, 0.2032f), new(0.55f, 0.2020f),
        new(0.60f, 0.2034f), new(0.70f, 0.2165f), new(0.725f, 0.2230f), new(0.75f, 0.2313f),
        new(0.775f, 0.2417f), new(0.80f, 0.2546f), new(0.825f, 0.2706f), new(0.85f, 0.2901f),
        new(0.875f, 0.3136f), new(0.90f, 0.3415f), new(0.925f, 0.3734f), new(0.95f, 0.4084f),
        new(0.975f, 0.4448f), new(1.00f, 0.4805f), new(1.025f, 0.5136f), new(1.05f, 0.5427f),
        new(1.075f, 0.5677f), new(1.10f, 0.5883f), new(1.125f, 0.6053f), new(1.15f, 0.6191f),
        new(1.20f, 0.6393f), new(1.25f, 0.6518f), new(1.30f, 0.6589f), new(1.35f, 0.6621f),
        new(1.40f, 0.6625f), new(1.45f, 0.6607f), new(1.50f, 0.6573f), new(1.55f, 0.6528f),
        new(1.60f, 0.6474f), new(1.65f, 0.6413f), new(1.70f, 0.6347f), new(1.75f, 0.6280f),
        new(1.80f, 0.6210f), new(1.85f, 0.6141f), new(1.90f, 0.6072f), new(1.95f, 0.6003f),
        new(2.00f, 0.5934f), new(2.05f, 0.5867f), new(2.10f, 0.5804f), new(2.15f, 0.5743f),
        new(2.20f, 0.5685f), new(2.25f, 0.5630f), new(2.30f, 0.5577f), new(2.35f, 0.5527f),
        new(2.40f, 0.5481f), new(2.45f, 0.5438f), new(2.50f, 0.5397f), new(2.60f, 0.5325f),
        new(2.70f, 0.5264f), new(2.80f, 0.5211f), new(2.90f, 0.5168f), new(3.00f, 0.5133f),
        new(3.10f, 0.5105f), new(3.20f, 0.5084f), new(3.30f, 0.5067f), new(3.40f, 0.5054f),
        new(3.50f, 0.5040f), new(3.60f, 0.5030f), new(3.70f, 0.5022f), new(3.80f, 0.5016f),
        new(3.90f, 0.5010f), new(4.00f, 0.5006f), new(4.20f, 0.4998f), new(4.40f, 0.4995f),
        new(4.60f, 0.4992f), new(4.80f, 0.4990f), new(5.00f, 0.4988f)
    };

    private static readonly DragPoint[] G7 =
    {
        new(0.00f, 0.1198f), new(0.05f, 0.1197f), new(0.10f, 0.1196f), new(0.15f, 0.1194f),
        new(0.20f, 0.1193f), new(0.25f, 0.1194f), new(0.30f, 0.1194f), new(0.35f, 0.1194f),
        new(0.40f, 0.1193f), new(0.45f, 0.1193f), new(0.50f, 0.1194f), new(0.55f, 0.1193f),
        new(0.60f, 0.1194f), new(0.65f, 0.1197f), new(0.70f, 0.1202f), new(0.725f, 0.1207f),
        new(0.75f, 0.1215f), new(0.775f, 0.1226f), new(0.80f, 0.1242f), new(0.825f, 0.1266f),
        new(0.85f, 0.1306f), new(0.875f, 0.1368f), new(0.90f, 0.1464f), new(0.925f, 0.1660f),
        new(0.95f, 0.2054f), new(0.975f, 0.2993f), new(1.00f, 0.3803f), new(1.025f, 0.4015f),
        new(1.05f, 0.4043f), new(1.075f, 0.4034f), new(1.10f, 0.4014f), new(1.125f, 0.3987f),
        new(1.15f, 0.3955f), new(1.20f, 0.3884f), new(1.25f, 0.3810f), new(1.30f, 0.3732f),
        new(1.35f, 0.3657f), new(1.40f, 0.3580f), new(1.50f, 0.3440f), new(1.55f, 0.3376f),
        new(1.60f, 0.3315f), new(1.65f, 0.3260f), new(1.70f, 0.3209f), new(1.75f, 0.3160f),
        new(1.80f, 0.3117f), new(1.85f, 0.3078f), new(1.90f, 0.3042f), new(1.95f, 0.3010f),
        new(2.00f, 0.2980f), new(2.05f, 0.2951f), new(2.10f, 0.2922f), new(2.15f, 0.2892f),
        new(2.20f, 0.2864f), new(2.25f, 0.2835f), new(2.30f, 0.2807f), new(2.35f, 0.2779f),
        new(2.40f, 0.2752f), new(2.45f, 0.2725f), new(2.50f, 0.2697f), new(2.55f, 0.2670f),
        new(2.60f, 0.2643f), new(2.65f, 0.2615f), new(2.70f, 0.2588f), new(2.75f, 0.2561f),
        new(2.80f, 0.2533f), new(2.85f, 0.2506f), new(2.90f, 0.2479f), new(2.95f, 0.2451f),
        new(3.00f, 0.2424f), new(3.10f, 0.2368f), new(3.20f, 0.2313f), new(3.30f, 0.2258f),
        new(3.40f, 0.2205f), new(3.50f, 0.2154f), new(3.60f, 0.2106f), new(3.70f, 0.2060f),
        new(3.80f, 0.2017f), new(3.90f, 0.1975f), new(4.00f, 0.1935f), new(4.20f, 0.1861f),
        new(4.40f, 0.1793f), new(4.60f, 0.1730f), new(4.80f, 0.1672f), new(5.00f, 0.1618f)
    };

    public static float ReferenceCd(DragModelKind model, float mach)
    {
        DragPoint[] table = model == DragModelKind.G7 ? G7 : G1;
        mach = MathF.Max(0f, mach);

        if (mach <= table[0].Mach)
            return table[0].Cd;
        if (mach >= table[^1].Mach)
            return table[^1].Cd;

        int low = 0;
        int high = table.Length - 1;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (table[mid].Mach <= mach)
                low = mid;
            else
                high = mid;
        }

        DragPoint a = table[low];
        DragPoint b = table[high];
        float t = (mach - a.Mach) / (b.Mach - a.Mach);
        return a.Cd + (b.Cd - a.Cd) * t;
    }
}
