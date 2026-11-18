using NetReporter.Core.Primitives;

namespace NetReporter.Viewer.Desktop;

/// <summary>How a desktop viewer picks its zoom.</summary>
public enum ZoomMode
{
    /// <summary>A fixed zoom factor.</summary>
    Custom,

    /// <summary>The page fills the viewport's width (recomputed when the viewport resizes).</summary>
    PageWidth,

    /// <summary>A whole page fits in the viewport.</summary>
    WholePage,
}

/// <summary>
/// Zoom factors (1 = 100 % = actual size: one point is 1/72 of an inch on screen) and the
/// preset steps the zoom-in/zoom-out commands walk through.
/// </summary>
public static class ZoomLevels
{
    public const double Min = 0.1;
    public const double Max = 4;

    /// <summary>The zoom-in/zoom-out steps (25 %–400 %, like the Designer).</summary>
    public static IReadOnlyList<double> Presets { get; } = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4];

    private const double Epsilon = 1e-3;

    public static double Clamp(double zoom) =>
        double.IsFinite(zoom) ? Math.Clamp(zoom, Min, Max) : 1;

    /// <summary>Device pixels per point for a zoom on a screen of <paramref name="dpi"/> (96 = 100 % scaling).</summary>
    public static double PixelsPerPoint(double zoom, double dpi) => Clamp(zoom) * dpi / 72.0;

    /// <summary>The next preset above <paramref name="zoom"/> (or <see cref="Max"/>).</summary>
    public static double ZoomIn(double zoom)
    {
        foreach (var preset in Presets)
            if (preset > zoom + Epsilon) return preset;
        return Max;
    }

    /// <summary>The next preset below <paramref name="zoom"/> (or <see cref="Min"/>).</summary>
    public static double ZoomOut(double zoom)
    {
        for (var i = Presets.Count - 1; i >= 0; i--)
            if (Presets[i] < zoom - Epsilon) return Presets[i];
        return Min;
    }

    /// <summary>Zoom at which the page width plus padding fills <paramref name="viewportWidth"/> pixels.</summary>
    public static double FitWidth(Size pageSize, int viewportWidth, double dpi, int padding = 16)
    {
        if (pageSize.Width <= 0 || dpi <= 0) return 1;
        var available = Math.Max(1, viewportWidth - 2 * padding);
        return Clamp(available / (pageSize.Width * dpi / 72.0));
    }

    /// <summary>Zoom at which a whole page plus padding fits in the viewport.</summary>
    public static double FitPage(Size pageSize, int viewportWidth, int viewportHeight, double dpi, int padding = 16)
    {
        if (pageSize.Height <= 0 || dpi <= 0) return FitWidth(pageSize, viewportWidth, dpi, padding);
        var availableHeight = Math.Max(1, viewportHeight - 2 * padding);
        var byHeight = availableHeight / (pageSize.Height * dpi / 72.0);
        return Clamp(Math.Min(FitWidth(pageSize, viewportWidth, dpi, padding), byHeight));
    }

    /// <summary>The zoom a mode resolves to for a viewport.</summary>
    public static double Resolve(ZoomMode mode, double customZoom, Size pageSize, int viewportWidth, int viewportHeight, double dpi,
        int padding = 16) => mode switch
    {
        ZoomMode.PageWidth => FitWidth(pageSize, viewportWidth, dpi, padding),
        ZoomMode.WholePage => FitPage(pageSize, viewportWidth, viewportHeight, dpi, padding),
        _ => Clamp(customZoom),
    };
}
