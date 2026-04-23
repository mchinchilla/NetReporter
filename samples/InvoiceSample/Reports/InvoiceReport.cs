using System.Globalization;
using InvoiceSample.Domain;
using NetReporter.Core.Bands;
using NetReporter.Core.DataBinding;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Expressions;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace InvoiceSample.Reports;

public static class InvoiceReport
{
    // Default: Letter con márgenes 1.5cm lateral / 1.8cm vertical.
    private static readonly PageSetup DefaultPage =
        PageSetup.Letter.WithMargins(new Thickness(Units.Cm(1.5), Units.Cm(1.8)));

    /// <summary>
    /// Construye la definición del reporte. Si no se pasa <paramref name="page"/>, se usa Letter
    /// con márgenes razonables. El caller puede pasar cualquier tamaño estándar
    /// (<see cref="PageSetup.Letter"/>, <see cref="PageSetup.Legal"/>, <see cref="PageSetup.A4"/>, etc.)
    /// combinado con <see cref="PageSetup.Landscape"/> y <see cref="PageSetup.WithMargins(Thickness)"/>.
    /// </summary>
    public static ReportDefinition Build(Factura factura, PageSetup? page = null)
    {
        var subtotal = factura.Lineas.Sum(l => l.Subtotal);
        var isv = Math.Round(subtotal * 0.15m, 2);
        var total = subtotal + isv;

        var cultureHn = CultureInfo.GetCultureInfo("es-HN");
        var effectivePage = page ?? DefaultPage;
        var w = effectivePage.ContentWidth;

        return new ReportDefinition
        {
            Name = "Factura",
            Title = $"Factura {factura.Numero}",
            Page = effectivePage,
            Styles = ErpTheme.Default,
            Culture = cultureHn,
            Bands = new Band[]
            {
                BuildReportHeader(factura, w),
                BuildPageHeader(),
                BuildDetail(factura, w),
                BuildReportFooter(subtotal, isv, total, cultureHn, w),
                BuildPageFooter(w)
            }
        };
    }

    private static ReportHeaderBand BuildReportHeader(Factura f, double w)
    {
        // Layout responsivo: bloque izquierdo ocupa hasta la mitad; derecho ocupa 1/3.
        const double leftLabelW  = 80;
        const double leftValueW  = 300;
        const double rightBlockW = 192;
        var rightX = w - rightBlockW;

        // Bloque derecho interior (etiquetas 80 / valores resto)
        const double rightLabelW = 80;
        var rightValueX = rightX + rightLabelW;
        var rightValueW = rightBlockW - rightLabelW;

        return new ReportHeaderBand
        {
            Height = 120,
            Elements = new ReportElement[]
            {
                // Título "FACTURA"
                new TextElement
                {
                    Bounds = new Rect(0, 0, w, 24),
                    Content = Expr.Str("FACTURA"),
                    Style = ErpStyles.Title
                },
                // Número de factura
                new TextElement
                {
                    Bounds = new Rect(0, 28, w, 14),
                    Content = Expr.Str($"No. {f.Numero}"),
                    Style = ErpStyles.Subtitle
                },

                // Datos del emisor (derecha)
                new TextElement
                {
                    Bounds = new Rect(rightX, 0, rightBlockW, 12),
                    Content = Expr.Str(f.Emisor.Nombre),
                    Style = ErpStyles.Value
                },
                new TextElement
                {
                    Bounds = new Rect(rightX, 14, rightBlockW, 10),
                    Content = Expr.Str($"RTN: {f.Emisor.Rtn}"),
                    Style = ErpStyles.Caption
                },
                new TextElement
                {
                    Bounds = new Rect(rightX, 26, rightBlockW, 10),
                    Content = Expr.Str(f.Emisor.Direccion),
                    Style = ErpStyles.Caption
                },
                new TextElement
                {
                    Bounds = new Rect(rightX, 38, rightBlockW, 10),
                    Content = Expr.Str($"Tel: {f.Emisor.Telefono}"),
                    Style = ErpStyles.Caption
                },

                // Separador
                new LineElement
                {
                    Bounds = new Rect(0, 56, w, 0),
                    Color = ErpColors.Gray300,
                    Thickness = 0.5
                },

                // Cliente (izquierda)
                new TextElement
                {
                    Bounds = new Rect(0, 64, leftLabelW, 12),
                    Content = Expr.Str("Cliente:"),
                    Style = ErpStyles.Label
                },
                new TextElement
                {
                    Bounds = new Rect(leftLabelW, 64, leftValueW, 12),
                    Content = Expr.Str(f.Cliente.Nombre),
                    Style = ErpStyles.Value
                },
                new TextElement
                {
                    Bounds = new Rect(0, 78, leftLabelW, 12),
                    Content = Expr.Str("RTN:"),
                    Style = ErpStyles.Label
                },
                new TextElement
                {
                    Bounds = new Rect(leftLabelW, 78, leftValueW, 12),
                    Content = Expr.Str(f.Cliente.Rtn),
                    Style = ErpStyles.Default
                },
                new TextElement
                {
                    Bounds = new Rect(0, 92, leftLabelW, 12),
                    Content = Expr.Str("Dirección:"),
                    Style = ErpStyles.Label
                },
                new TextElement
                {
                    Bounds = new Rect(leftLabelW, 92, leftValueW, 12),
                    Content = Expr.Str(f.Cliente.Direccion),
                    Style = ErpStyles.Default
                },

                // Fecha (derecha)
                new TextElement
                {
                    Bounds = new Rect(rightX, 64, rightLabelW, 12),
                    Content = Expr.Str("Fecha:"),
                    Style = ErpStyles.Label
                },
                new TextElement
                {
                    Bounds = new Rect(rightValueX, 64, rightValueW, 12),
                    Content = Expr.Str(f.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                    Style = ErpStyles.Default
                },
                new TextElement
                {
                    Bounds = new Rect(rightX, 78, rightLabelW, 12),
                    Content = Expr.Str("CAI:"),
                    Style = ErpStyles.Label
                },
                new TextElement
                {
                    Bounds = new Rect(rightValueX, 78, rightValueW, 10),
                    Content = Expr.Str(f.Cai),
                    Style = ErpStyles.Caption
                },
            }
        };
    }

    private static PageHeaderBand BuildPageHeader() => new()
    {
        Height = 0,  // sin page header en factura
        Elements = Array.Empty<ReportElement>()
    };

    private static DetailBand BuildDetail(Factura f, double w)
    {
        // Distribución proporcional de columnas (suma = 1.0).
        // Código 14%, Descripción 49%, Cant 9%, P.Unit 14%, Subtotal 14%.
        var codigoW   = Math.Round(w * 0.14);
        var cantW     = Math.Round(w * 0.09);
        var pUnitW    = Math.Round(w * 0.14);
        var subtotalW = Math.Round(w * 0.14);
        var descW     = w - codigoW - cantW - pUnitW - subtotalW; // resto

        return new DetailBand
        {
            Height = 0,  // altura la determina el contenido
            Elements = new ReportElement[]
            {
                new TableElement<LineaFactura>
                {
                    Bounds = new Rect(0, 0, w, 0),
                    Rows = f.Lineas,
                    HeaderStyle = ErpStyles.TableHeader,
                    RowStyle = ErpStyles.TableRow,
                    AlternateRowStyle = ErpStyles.TableRowAlt,
                    HeaderMode = TableHeaderMode.RepeatOnPageBreak,
                    HeaderHeight = 22,
                    RowHeight = 18,
                    Columns = new[]
                    {
                        new TableColumn<LineaFactura>(
                            "Código",
                            Bind.From<LineaFactura, object?>(l => l.Codigo),
                            Width: codigoW),
                        new TableColumn<LineaFactura>(
                            "Descripción",
                            Bind.From<LineaFactura, object?>(l => l.Descripcion),
                            Width: descW),
                        new TableColumn<LineaFactura>(
                            "Cant.",
                            Bind.From<LineaFactura, object?>(l => l.Cantidad),
                            Width: cantW,
                            Align: TextAlignment.Right),
                        new TableColumn<LineaFactura>(
                            "P. Unit.",
                            Bind.From<LineaFactura, object?>(l => l.PrecioUnitario),
                            Width: pUnitW,
                            Format: "N2",
                            Align: TextAlignment.Right),
                        new TableColumn<LineaFactura>(
                            "Subtotal",
                            Bind.From<LineaFactura, object?>(l => l.Subtotal),
                            Width: subtotalW,
                            Format: "N2",
                            Align: TextAlignment.Right),
                    }
                }
            }
        };
    }

    private static ReportFooterBand BuildReportFooter(
        decimal subtotal, decimal isv, decimal total, CultureInfo culture, double w)
    {
        // Bloque de totales: 1/3 del ancho alineado a la derecha.
        var totalsW  = Math.Max(192, w / 3);
        var totalsX  = w - totalsW;
        var labelW   = totalsW * 0.55;
        var valueX   = totalsX + labelW;
        var valueW   = totalsW - labelW;

        return new ReportFooterBand
        {
            Height = 80,
            Elements = new ReportElement[]
            {
                // Separador superior
                new LineElement
                {
                    Bounds = new Rect(totalsX, 8, totalsW, 0),
                    Color = ErpColors.Gray300,
                    Thickness = 0.5
                },

                // Subtotal
                new TextElement
                {
                    Bounds = new Rect(totalsX, 14, labelW, 14),
                    Content = Expr.Str("Subtotal:"),
                    Style = ErpStyles.LabelRight
                },
                new TextElement
                {
                    Bounds = new Rect(valueX, 14, valueW, 14),
                    Content = Expr.Str(subtotal.ToString("N2", culture)),
                    Style = ErpStyles.ValueRight
                },

                // ISV 15%
                new TextElement
                {
                    Bounds = new Rect(totalsX, 30, labelW, 14),
                    Content = Expr.Str("ISV 15%:"),
                    Style = ErpStyles.LabelRight
                },
                new TextElement
                {
                    Bounds = new Rect(valueX, 30, valueW, 14),
                    Content = Expr.Str(isv.ToString("N2", culture)),
                    Style = ErpStyles.ValueRight
                },

                // Separador antes del total
                new LineElement
                {
                    Bounds = new Rect(totalsX, 46, totalsW, 0),
                    Color = ErpColors.Ink,
                    Thickness = 1
                },

                // Total
                new TextElement
                {
                    Bounds = new Rect(totalsX, 50, labelW, 14),
                    Content = Expr.Str("Total:"),
                    Style = ErpStyles.BoldRight
                },
                new TextElement
                {
                    Bounds = new Rect(valueX, 50, valueW, 14),
                    Content = Expr.Str(total.ToString("N2", culture)),
                    Style = ErpStyles.BoldRight
                },
            }
        };
    }

    private static PageFooterBand BuildPageFooter(double w) => new()
    {
        Height = 28,
        Elements = new ReportElement[]
        {
            new LineElement
            {
                Bounds = new Rect(0, 0, w, 0),
                Color = ErpColors.Gray300,
                Thickness = 0.5
            },
            new TextElement
            {
                Bounds = new Rect(0, 8, w, 10),
                Content = Expr.PageOfTotal,
                Style = ErpStyles.CaptionCenter
            }
        }
    };
}
