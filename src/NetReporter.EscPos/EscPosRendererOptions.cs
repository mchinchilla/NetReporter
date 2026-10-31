namespace NetReporter.EscPos;

/// <summary>Cómo se encaja el ancho de la página en el ancho imprimible de la impresora.</summary>
public enum EscPosPageFit
{
    /// <summary>
    /// 1:1 a la resolución de la impresora cuando el contenido (página menos márgenes) cabe en el área
    /// imprimible, recortando los márgenes; si no cabe, escala como <see cref="Scale"/>. Con
    /// <c>PageSetup.Roll80</c>/<c>Roll58</c> y sus márgenes por defecto el recorte es exactamente el margen.
    /// </summary>
    Auto,
    /// <summary>Siempre 1:1, centrado: lo que sobresale del área imprimible se pierde.</summary>
    Crop,
    /// <summary>Escala la página completa al ancho imprimible (texto más pequeño en páginas anchas).</summary>
    Scale,
}

/// <summary>Conversión de la página gris a los puntos negro/blanco de la impresora.</summary>
public enum EscPosDithering
{
    /// <summary>Umbral fijo (<see cref="EscPosRendererOptions.Threshold"/>): texto y líneas nítidos; los grises se pierden.</summary>
    Threshold,
    /// <summary>Difusión de error Floyd–Steinberg: conserva grises de logos y fotos a costa de texto algo más rugoso.</summary>
    FloydSteinberg,
}

/// <summary>Corte de papel al final (comando <c>GS V</c>).</summary>
public enum EscPosCut
{
    None,
    /// <summary>Corte parcial: deja un puente de papel (lo habitual en POS).</summary>
    Partial,
    Full,
}

/// <summary>
/// Perfil de impresora y comportamiento del <see cref="EscPosRenderer"/>. Todo se fija en tiempo de
/// ejecución por código; <see cref="ForRoll80"/> y <see cref="ForRoll58"/> devuelven una instancia
/// nueva con los valores típicos que el llamador puede seguir ajustando.
/// </summary>
public sealed class EscPosRendererOptions
{
    /// <summary>Puntos imprimibles por línea: 576 en rollos de 80 mm (72 mm útiles a 203 dpi), 384 en los de 58 mm. Múltiplo de 8.</summary>
    public int DotsPerLine { get; set; } = 576;

    /// <summary>Resolución del cabezal. 203.2 dpi (8 puntos/mm) es lo habitual; algunas impresoras usan 180 dpi.</summary>
    public double Dpi { get; set; } = 203.2;

    public EscPosPageFit PageFit { get; set; } = EscPosPageFit.Auto;

    public EscPosDithering Dithering { get; set; } = EscPosDithering.Threshold;

    /// <summary>
    /// Gris (0 negro – 255 blanco) por debajo del cual un punto se imprime. 160 engorda un poco los
    /// trazos finos del texto antialiasado; con <see cref="EscPosDithering.FloydSteinberg"/> use ~128.
    /// </summary>
    public int Threshold { get; set; } = 160;

    /// <summary>
    /// Filas de puntos por bloque <c>GS v 0</c>. Bloques pequeños evitan desbordar el búfer de
    /// impresoras económicas; el máximo que admite el comando es 2303.
    /// </summary>
    public int BandRows { get; set; } = 256;

    /// <summary>Envía <c>ESC @</c> al inicio para dejar la impresora en su estado por defecto.</summary>
    public bool Initialize { get; set; } = true;

    /// <summary>Bytes propios justo después de la inicialización (página de códigos, densidad, etc.).</summary>
    public byte[]? Prologue { get; set; }

    /// <summary>Bytes propios al final, tras el último corte.</summary>
    public byte[]? Epilogue { get; set; }

    /// <summary>Líneas que se alimentan antes de cortar para que el final del ticket salga del cabezal (0–255).</summary>
    public int FeedLinesBeforeCut { get; set; } = 3;

    public EscPosCut Cut { get; set; } = EscPosCut.Partial;

    /// <summary>Con documentos de varias páginas, cortar tras cada una (un ticket por página) o solo al final.</summary>
    public bool CutAfterEachPage { get; set; } = true;

    /// <summary>Abre la gaveta de dinero (<c>ESC p</c>) antes de imprimir.</summary>
    public bool OpenCashDrawer { get; set; }

    /// <summary>Conector de la gaveta: 0 (pin 2) o 1 (pin 5).</summary>
    public int CashDrawerPin { get; set; }

    /// <summary>Copia superficial: para derivar opciones por petición sin tocar las compartidas.</summary>
    public EscPosRendererOptions Clone() => (EscPosRendererOptions)MemberwiseClone();

    /// <summary>Rollo de 80 mm: 576 puntos por línea.</summary>
    public static EscPosRendererOptions ForRoll80() => new();

    /// <summary>Rollo de 58 mm: 384 puntos por línea.</summary>
    public static EscPosRendererOptions ForRoll58() => new() { DotsPerLine = 384 };

    internal void Validate()
    {
        if (DotsPerLine <= 0 || DotsPerLine % 8 != 0)
            throw new ArgumentOutOfRangeException(nameof(DotsPerLine), DotsPerLine, "DotsPerLine debe ser positivo y múltiplo de 8 (576 para 80 mm, 384 para 58 mm).");
        if (!(Dpi > 0) || double.IsInfinity(Dpi))
            throw new ArgumentOutOfRangeException(nameof(Dpi), Dpi, "Dpi debe ser mayor que 0.");
        if (Threshold is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(Threshold), Threshold, "Threshold va de 0 a 255.");
        if (BandRows is < 1 or > 2303)
            throw new ArgumentOutOfRangeException(nameof(BandRows), BandRows, "BandRows va de 1 a 2303 (límite del comando GS v 0).");
        if (FeedLinesBeforeCut is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(FeedLinesBeforeCut), FeedLinesBeforeCut, "FeedLinesBeforeCut va de 0 a 255.");
        if (CashDrawerPin is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(CashDrawerPin), CashDrawerPin, "CashDrawerPin es 0 (pin 2) o 1 (pin 5).");
    }
}
