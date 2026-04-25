using NetReporter.Core.Bands;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Expressions;
using NetReporter.Core.Layout;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Tests.Layout;

public sealed class AutoHeightTests
{
    private static readonly StyleSheet Styles = new StyleSheetBuilder()
        .Add("Default", s => s.FontFamily("Helvetica").FontSize(10))
        .Build();

    private static ReportDefinition BuildReport(Band[] bands) => new()
    {
        Name = "Test",
        Page = PageSetup.Letter.WithMargins(new Thickness(20)),
        Styles = Styles,
        Bands = bands
    };

    private static TextElement Text(double x, double y, double w, double h, string content,
                                     bool wrap = true, bool autoHeight = false) => new()
    {
        Bounds = new Rect(x, y, w, h),
        Content = Expr.Str(content),
        WordWrap = wrap,
        AutoHeight = autoHeight
    };

    [Fact]
    public void Band_WithoutAutoHeight_AdvancesByDeclaredHeight()
    {
        var report = BuildReport(new Band[]
        {
            new ReportHeaderBand
            {
                Height = 30,
                Elements = new ReportElement[] { Text(0, 0, 100, 14, "Hola", wrap: false) }
            },
            new ReportFooterBand
            {
                Height = 20,
                Elements = new ReportElement[] { Text(0, 0, 100, 14, "Mundo", wrap: false) }
            }
        });

        var rl = new LayoutEngine().Layout(report);
        var commands = rl.Pages[0].Commands.OfType<DrawTextCommand>().ToArray();
        Assert.Equal(2, commands.Length);

        // ReportHeader empieza en margin top (20), height 30 → ReportFooter empieza en 50.
        Assert.Equal(20, commands[0].Bounds.Y, 1);
        Assert.Equal(50, commands[1].Bounds.Y, 1);
    }

    [Fact]
    public void Band_WithAutoHeight_AndNoOverflow_StillUsesDeclaredHeight()
    {
        var report = BuildReport(new Band[]
        {
            new ReportHeaderBand
            {
                Height = 50,
                AutoHeight = true,
                Elements = new ReportElement[] { Text(0, 0, 100, 14, "Hola", wrap: false) }
            },
            new ReportFooterBand
            {
                Height = 20,
                Elements = new ReportElement[] { Text(0, 0, 100, 14, "Mundo", wrap: false) }
            }
        });

        var rl = new LayoutEngine().Layout(report);
        var commands = rl.Pages[0].Commands.OfType<DrawTextCommand>().ToArray();

        // ReportHeader: height declarada 50, footer empieza en 70.
        Assert.Equal(20, commands[0].Bounds.Y, 1);
        Assert.Equal(70, commands[1].Bounds.Y, 1);
    }

    [Fact]
    public void Band_WithAutoHeight_GrowsWithWrappedText()
    {
        // Text con wrap genera múltiples líneas. Sin auto-height, ReportFooter overlap;
        // con auto-height, el footer empieza después de la última línea wrap'd.
        var longText = "Esta es una linea muy larga que va a ocupar varias lineas dentro de un text element angosto y debe crecer la banda";

        var report = BuildReport(new Band[]
        {
            new ReportHeaderBand
            {
                Height = 12,                  // chico a propósito
                AutoHeight = true,
                Elements = new ReportElement[]
                {
                    Text(0, 0, 100, 12, longText, wrap: true, autoHeight: true)
                }
            },
            new ReportFooterBand
            {
                Height = 20,
                Elements = new ReportElement[] { Text(0, 0, 100, 14, "Despues", wrap: false) }
            }
        });

        var rl = new LayoutEngine().Layout(report);
        var commands = rl.Pages[0].Commands.OfType<DrawTextCommand>().ToArray();

        var afterText = commands.Last(c => c.Text == "Despues");
        Assert.True(afterText.Bounds.Y > 32,
            $"AutoHeight band did not grow — 'Despues' at Y={afterText.Bounds.Y}, expected > 32");
    }

    [Fact]
    public void Band_WithoutAutoHeight_OverflowsAndDoesNotGrow()
    {
        // Sin auto-height, la banda no crece — la siguiente banda arranca al height declarado.
        var longText = "Linea uno con suficiente texto para ocupar muchas lineas en un text angosto";

        var report = BuildReport(new Band[]
        {
            new ReportHeaderBand
            {
                Height = 12,                  // chico, sin AutoHeight
                Elements = new ReportElement[]
                {
                    Text(0, 0, 100, 12, longText, wrap: true, autoHeight: true)
                }
            },
            new ReportFooterBand
            {
                Height = 20,
                Elements = new ReportElement[] { Text(0, 0, 100, 14, "Despues", wrap: false) }
            }
        });

        var rl = new LayoutEngine().Layout(report);
        var afterText = rl.Pages[0].Commands.OfType<DrawTextCommand>()
                          .First(c => c.Text == "Despues");

        // Margin top 20 + band1 height 12 = 32. ReportFooter empieza en 32 exacto.
        Assert.Equal(32, afterText.Bounds.Y, 1);
    }
}
