using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace InvoiceSample.Reports;

/// <summary>Paleta de colores del ERP. Cambiar aquí cambia todos los reportes.</summary>
public static class ErpColors
{
    public static readonly Color Gray50  = Color.FromHex("#F8FAFC");
    public static readonly Color Gray100 = Color.FromHex("#F1F5F9");
    public static readonly Color Gray300 = Color.FromHex("#CBD5E1");
    public static readonly Color Gray500 = Color.FromHex("#64748B");
    public static readonly Color Gray700 = Color.FromHex("#334155");
    public static readonly Color Gray900 = Color.FromHex("#0F172A");

    public static readonly Color Primary = Color.FromHex("#0284C7");
    public static readonly Color Ink     = Gray900;
    public static readonly Color Muted   = Gray500;
}

/// <summary>StyleRefs tipados para autocompletar.</summary>
public static class ErpStyles
{
    public static readonly StyleRef Default     = new("Default");
    public static readonly StyleRef Title       = new("Title");
    public static readonly StyleRef Subtitle    = new("Subtitle");
    public static readonly StyleRef Caption     = new("Caption");
    public static readonly StyleRef CaptionCenter = new("CaptionCenter");
    public static readonly StyleRef Label       = new("Label");
    public static readonly StyleRef LabelRight  = new("LabelRight");
    public static readonly StyleRef Value       = new("Value");
    public static readonly StyleRef ValueRight  = new("ValueRight");
    public static readonly StyleRef BoldRight   = new("BoldRight");
    public static readonly StyleRef TableHeader = new("TableHeader");
    public static readonly StyleRef TableRow    = new("TableRow");
    public static readonly StyleRef TableRowAlt = new("TableRowAlt");
    public static readonly StyleRef TableTotal  = new("TableTotal");
}

public static class ErpTheme
{
    public static readonly StyleSheet Default = new StyleSheetBuilder()
        .Add("Default", s => s
            .FontFamily("Helvetica")
            .FontSize(10)
            .Foreground(ErpColors.Ink))

        .Add("Title",    s => s.BasedOn("Default").FontSize(18).Bold())
        .Add("Subtitle", s => s.BasedOn("Default").FontSize(12).Bold()
            .Foreground(ErpColors.Gray700))
        .Add("Caption",  s => s.BasedOn("Default").FontSize(8)
            .Foreground(ErpColors.Muted))
        .Add("CaptionCenter", s => s.BasedOn("Caption").Align(TextAlignment.Center))
        .Add("Label",    s => s.BasedOn("Default").FontSize(9)
            .Foreground(ErpColors.Muted))
        .Add("LabelRight", s => s.BasedOn("Label").Align(TextAlignment.Right))
        .Add("Value",    s => s.BasedOn("Default").FontSize(10).Bold())
        .Add("ValueRight", s => s.BasedOn("Default").Align(TextAlignment.Right))
        .Add("BoldRight",  s => s.BasedOn("Value").Align(TextAlignment.Right))

        .Add("TableHeader", s => s.BasedOn("Default")
            .Bold()
            .Background(ErpColors.Gray100)
            .Padding(new Thickness(4, 3))
            .Border(BorderSet.OnlyBottom(0.5, ErpColors.Gray300)))

        .Add("TableRow", s => s.BasedOn("Default")
            .Padding(new Thickness(4, 3)))

        .Add("TableRowAlt", s => s.BasedOn("TableRow")
            .Background(ErpColors.Gray50))

        .Add("TableTotal", s => s.BasedOn("TableRow")
            .Bold()
            .Border(BorderSet.OnlyTop(1, ErpColors.Ink)))

        .Build();
}
