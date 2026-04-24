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
}
