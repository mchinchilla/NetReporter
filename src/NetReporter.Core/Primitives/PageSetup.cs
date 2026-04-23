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

    // === Factories ===

    /// <summary>
    /// Página de tamaño arbitrario en puntos. Use <see cref="Units"/> para convertir desde cm/mm/in.
    /// </summary>
    public static PageSetup Custom(double widthPt, double heightPt, Thickness? margins = null) =>
        new(new Size(widthPt, heightPt), margins ?? DefaultMargins);

    // === Transformaciones ===

    /// <summary>Devuelve la misma página en orientación horizontal (intercambia ancho/alto).</summary>
    public PageSetup Landscape() => Orientation == PageOrientation.Landscape
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
