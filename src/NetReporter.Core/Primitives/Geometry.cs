namespace NetReporter.Core.Primitives;

public readonly record struct Point(double X, double Y)
{
    public static readonly Point Zero = default;
}

public readonly record struct Size(double Width, double Height)
{
    public static readonly Size Zero = default;
    public static readonly Size Infinite = new(double.PositiveInfinity, double.PositiveInfinity);
}

public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public Point TopLeft => new(X, Y);
    public Size Size => new(Width, Height);

    public static readonly Rect Empty = default;

    public Rect WithX(double x) => this with { X = x };
    public Rect WithY(double y) => this with { Y = y };
    public Rect WithWidth(double w) => this with { Width = w };
    public Rect WithHeight(double h) => this with { Height = h };
    public Rect Offset(double dx, double dy) => new(X + dx, Y + dy, Width, Height);
}

public readonly record struct Thickness(double Left, double Top, double Right, double Bottom)
{
    public static readonly Thickness Zero = default;

    public Thickness(double uniform) : this(uniform, uniform, uniform, uniform) { }
    public Thickness(double horizontal, double vertical)
        : this(horizontal, vertical, horizontal, vertical) { }

    public double Horizontal => Left + Right;
    public double Vertical => Top + Bottom;
}

public static class Units
{
    public const double PointsPerInch = 72.0;
    public const double PointsPerCm   = 28.3464567;
    public const double PointsPerMm   = 2.83464567;

    public static double Cm(double cm)     => cm * PointsPerCm;
    public static double Mm(double mm)     => mm * PointsPerMm;
    public static double Inch(double inch) => inch * PointsPerInch;
}
