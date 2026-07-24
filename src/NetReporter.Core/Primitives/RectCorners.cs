namespace NetReporter.Core.Primitives;

/// <summary>
/// Esquinas a las que se aplica <c>CornerRadius</c>. Permite bandas apiladas que se leen como
/// una sola tarjeta: la cabecera redondea <see cref="Top"/> y el cuerpo <see cref="Bottom"/>,
/// de modo que el borde donde se tocan queda recto.
/// </summary>
[Flags]
public enum RectCorners
{
    None = 0,
    TopLeft = 1,
    TopRight = 2,
    BottomRight = 4,
    BottomLeft = 8,

    Top = TopLeft | TopRight,
    Bottom = BottomLeft | BottomRight,
    Left = TopLeft | BottomLeft,
    Right = TopRight | BottomRight,

    All = TopLeft | TopRight | BottomRight | BottomLeft
}
