namespace NetReporter.Core.Primitives;

public enum PageOrientation { Portrait, Landscape }

public sealed record PageSetup(
    Size Size,
    Thickness Margins,
    PageOrientation Orientation = PageOrientation.Portrait)
{
    private static readonly Thickness DefaultMargins = new(Units.Cm(2));

    // === Tamaños US / ANSI ===

    /// <summary>8.5" × 11" (216 × 279 mm). US Letter.</summary>
    public static readonly PageSetup Letter    = new(new Size(612, 792),  DefaultMargins);

    /// <summary>8.5" × 14" (216 × 356 mm). US Legal.</summary>
    public static readonly PageSetup Legal     = new(new Size(612, 1008), DefaultMargins);

    /// <summary>11" × 17" (279 × 432 mm). Tabloid / Ledger.</summary>
    public static readonly PageSetup Tabloid   = new(new Size(792, 1224), DefaultMargins);

    /// <summary>7.25" × 10.5" (184 × 267 mm). Executive.</summary>
    public static readonly PageSetup Executive = new(new Size(522, 756),  DefaultMargins);

    /// <summary>5.5" × 8.5" (140 × 216 mm). Statement / Half Letter.</summary>
    public static readonly PageSetup Statement = new(new Size(396, 612),  DefaultMargins);

    // === Tamaños ISO A ===

    /// <summary>297 × 420 mm.</summary>
    public static readonly PageSetup A3 = new(new Size(841.890, 1190.551), DefaultMargins);

    /// <summary>210 × 297 mm. Estándar ISO para documentos.</summary>
    public static readonly PageSetup A4 = new(new Size(595.276, 841.890),  DefaultMargins);

    /// <summary>148 × 210 mm.</summary>
    public static readonly PageSetup A5 = new(new Size(419.528, 595.276),  DefaultMargins);

    /// <summary>105 × 148 mm.</summary>
    public static readonly PageSetup A6 = new(new Size(297.638, 419.528),  DefaultMargins);

    // === Tamaños ISO B ===

    /// <summary>250 × 353 mm.</summary>
    public static readonly PageSetup B4 = new(new Size(708.661, 1000.630), DefaultMargins);

    /// <summary>176 × 250 mm.</summary>
    public static readonly PageSetup B5 = new(new Size(498.898, 708.661),  DefaultMargins);

    // === Papel continuo (rollo de impresora POS) ===

    /// <summary>
    /// Papel continuo: no hay saltos de página y la altura la decide el contenido. El
    /// <c>LayoutEngine</c> devuelve una sola página cuyo <c>RenderList.Page.Size.Height</c> es la
    /// altura final (contenido + pie de página + margen inferior); <see cref="Size"/>.Height aquí es
    /// solo nominal. Las bandas PageHeader/PageFooter se imprimen una vez, arriba y al final.
    /// </summary>
    public bool Continuous { get; init; }

    // El área imprimible de un rollo de 80 mm son 72 mm (576 puntos a 203 dpi) y la de 58 mm son
    // 48 mm (384 puntos): los márgenes laterales por defecto dejan el contenido dentro de ella.
    private static readonly Thickness Roll80Margins = new(Units.Mm(4), Units.Mm(4));
    private static readonly Thickness Roll58Margins = new(Units.Mm(5), Units.Mm(4));

    /// <summary>Rollo de 80 mm (226.77 pt) de ancho, continuo; márgenes 4 mm (área imprimible de 72 mm).</summary>
    public static readonly PageSetup Roll80 = new(new Size(Units.Mm(80), 0), Roll80Margins) { Continuous = true };

    /// <summary>Rollo de 58 mm (164.41 pt) de ancho, continuo; márgenes 5 mm a los lados (área imprimible de 48 mm) y 4 mm arriba/abajo.</summary>
    public static readonly PageSetup Roll58 = new(new Size(Units.Mm(58), 0), Roll58Margins) { Continuous = true };

    /// <summary>Rollo continuo de ancho arbitrario en puntos (<see cref="Units"/> convierte desde mm); márgenes 4 mm salvo que se indiquen.</summary>
    public static PageSetup Roll(double widthPt, Thickness? margins = null)
    {
        if (!(widthPt > 0)) throw new ArgumentOutOfRangeException(nameof(widthPt), widthPt, "El ancho del rollo debe ser mayor que 0.");
        return new(new Size(widthPt, 0), margins ?? Roll80Margins) { Continuous = true };
    }

    // === Factories ===

    /// <summary>
    /// Página de tamaño arbitrario en puntos. Use <see cref="Units"/> para convertir desde cm/mm/in.
    /// </summary>
    public static PageSetup Custom(double widthPt, double heightPt, Thickness? margins = null) =>
        new(new Size(widthPt, heightPt), margins ?? DefaultMargins);

    // === Transformaciones ===

    /// <summary>Devuelve la misma página en orientación horizontal (intercambia ancho/alto). No aplica a papel continuo.</summary>
    public PageSetup Landscape() => Continuous
        ? throw new InvalidOperationException("Un rollo continuo no tiene orientación: su altura la decide el contenido.")
        : Orientation == PageOrientation.Landscape
        ? this
        : this with
        {
            Size = new Size(Size.Height, Size.Width),
            Orientation = PageOrientation.Landscape
        };

    /// <summary>Devuelve la misma página en orientación vertical (revierte Landscape si aplica).</summary>
    public PageSetup Portrait() => Orientation == PageOrientation.Portrait
        ? this
        : this with
        {
            Size = new Size(Size.Height, Size.Width),
            Orientation = PageOrientation.Portrait
        };

    /// <summary>Devuelve la misma página con márgenes distintos.</summary>
    public PageSetup WithMargins(Thickness margins) => this with { Margins = margins };

    /// <summary>Devuelve la misma página con márgenes uniformes (en puntos).</summary>
    public PageSetup WithMargins(double uniform) => this with { Margins = new Thickness(uniform) };

    // === Derivados ===

    public double ContentWidth  => Size.Width  - Margins.Horizontal;
    public double ContentHeight => Size.Height - Margins.Vertical;

    public Point ContentOrigin => new(Margins.Left, Margins.Top);
}
