using System.Globalization;
using System.Resources;

namespace NetReporter.Viewer.WinForms;

/// <summary>
/// UI strings in one culture (Resources/ViewerStrings.resx = English, the neutral fallback;
/// ViewerStrings.es.resx = Spanish). Null culture = the thread's current UI culture at lookup time.
/// </summary>
internal sealed class ViewerStrings(CultureInfo? culture)
{
    private static readonly ResourceManager Resources =
        new("NetReporter.Viewer.WinForms.Resources.ViewerStrings", typeof(ViewerStrings).Assembly);

    public CultureInfo Culture => culture ?? CultureInfo.CurrentUICulture;

    public string Get(string key) => Resources.GetString(key, Culture) ?? key;

    public string Format(string text, object? arg) => string.Format(Culture, text, arg);

    public string FirstPage => Get(nameof(FirstPage));
    public string PreviousPage => Get(nameof(PreviousPage));
    public string NextPage => Get(nameof(NextPage));
    public string LastPage => Get(nameof(LastPage));
    public string PageNumber => Get(nameof(PageNumber));
    public string PageOf => Get(nameof(PageOf));
    public string ZoomIn => Get(nameof(ZoomIn));
    public string ZoomOut => Get(nameof(ZoomOut));
    public string ZoomLevel => Get(nameof(ZoomLevel));
    public string PageWidth => Get(nameof(PageWidth));
    public string WholePage => Get(nameof(WholePage));
    public string Export => Get(nameof(Export));
    public string ExportPdf => Get(nameof(ExportPdf));
    public string ExportXlsx => Get(nameof(ExportXlsx));
    public string ExportHtml => Get(nameof(ExportHtml));
    public string FilterPdf => Get(nameof(FilterPdf));
    public string FilterXlsx => Get(nameof(FilterXlsx));
    public string FilterHtml => Get(nameof(FilterHtml));
    public string Loading => Get(nameof(Loading));
    public string Exporting => Get(nameof(Exporting));
    public string NoDocument => Get(nameof(NoDocument));
    public string LoadFailed => Get(nameof(LoadFailed));
    public string ExportFailed => Get(nameof(ExportFailed));
    public string ErrorTitle => Get(nameof(ErrorTitle));
}
