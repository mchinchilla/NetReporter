namespace NetReporter.Html;

public sealed class HtmlRenderOptions
{
    /// <summary>Título del documento (meta title).</summary>
    public string Title { get; init; } = "Report";

    /// <summary>
    /// Mostrar separación visual entre páginas (sombra + margen) cuando se ve en pantalla.
    /// En print siempre una página por hoja via @page/page-break.
    /// </summary>
    public bool ShowScreenChrome { get; init; } = true;

    /// <summary>Incluir &lt;html&gt;&lt;head&gt;&lt;body&gt;. False emite solo el markup de páginas (fragmento).</summary>
    public bool FullDocument { get; init; } = true;

    /// <summary>
    /// Emite una barra flotante (esquina inferior-derecha) con controles de zoom
    /// que escalan los <c>.nr-page</c> via CSS <c>zoom</c>. Persiste el nivel por
    /// documento en <c>localStorage</c>. Oculta al imprimir. Requiere
    /// <see cref="FullDocument"/> = true.
    /// </summary>
    public bool ShowZoomControls { get; init; } = false;

    /// <summary>
    /// Zoom inicial (1.0 = 100%). Se usa la primera vez; después se respeta el
    /// valor en localStorage. Solo aplica con <see cref="ShowZoomControls"/> = true.
    /// </summary>
    public double InitialZoom { get; init; } = 1.0;
}
