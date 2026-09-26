using ClosedXML.Excel;
using NetReporter.Core.Bands;
using NetReporter.Core.DataBinding;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Expressions;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;
using NetReporter.Xlsx;

namespace NetReporter.Xlsx.Tests;

public sealed class XlsxRendererTests
{
    private static StyleSheet TestStyles() => new StyleSheetBuilder()
        .Add("Default", s => s.FontFamily("Helvetica").FontSize(10))
        .Add("Title",   s => s.BasedOn("Default").FontSize(18).Bold())
        .Add("TableHeader", s => s.BasedOn("Default").Bold())
        .Add("TableRow",    s => s.BasedOn("Default"))
        .Add("GroupHeader", s => s.BasedOn("Default").Bold())
        .Add("GroupFooter", s => s.BasedOn("Default").Bold())
        .Build();

    private static ReportDefinition MakeReport(params Band[] bands) => new()
    {
        Name = "Test",
        Page = PageSetup.Letter,
        Styles = TestStyles(),
        Bands = bands
    };

    private static IXLWorksheet OpenFirstSheet(byte[] xlsx)
    {
        var wb = new XLWorkbook(new MemoryStream(xlsx));
        return wb.Worksheet(1);
    }

    [Fact]
    public void Render_Empty_ProducesValidWorkbook()
    {
        var report = MakeReport();
        var bytes = new XlsxRenderer().Render(report);

        Assert.NotEmpty(bytes);
        var ws = OpenFirstSheet(bytes);
        Assert.Equal("Test", ws.Name);
    }

    [Fact]
    public void Render_TextElement_EmitsCellWithContent()
    {
        var report = MakeReport(new ReportHeaderBand
        {
            Height = 30,
            Elements = new ReportElement[]
            {
                new TextElement
                {
                    Bounds = new Rect(0, 0, 200, 20),
                    Content = Expr.Str("Hello"),
                    Style = new StyleRef("Title")
                }
            }
        });

        var bytes = new XlsxRenderer().Render(report);
        var ws = OpenFirstSheet(bytes);

        Assert.Equal("Hello", ws.Cell(1, 1).GetString());
        Assert.True(ws.Cell(1, 1).Style.Font.Bold, "Title style is bold");
        Assert.Equal(18, ws.Cell(1, 1).Style.Font.FontSize);
    }

    [Fact]
    public void Render_StacksMultipleTextElementsOnSeparateRows()
    {
        var report = MakeReport(new ReportHeaderBand
        {
            Height = 60,
            Elements = new ReportElement[]
            {
                new TextElement { Bounds = new Rect(0, 0, 200, 20), Content = Expr.Str("Line 1") },
                new TextElement { Bounds = new Rect(0, 0, 200, 20), Content = Expr.Str("Line 2") },
                new TextElement { Bounds = new Rect(0, 0, 200, 20), Content = Expr.Str("Line 3") }
            }
        });

        var ws = OpenFirstSheet(new XlsxRenderer().Render(report));
        Assert.Equal("Line 1", ws.Cell(1, 1).GetString());
        Assert.Equal("Line 2", ws.Cell(2, 1).GetString());
        Assert.Equal("Line 3", ws.Cell(3, 1).GetString());
    }

    private sealed record Item(string Code, string Name, decimal Price, int Qty);

    [Fact]
    public void Render_Table_EmitsHeaderRowAndDataRows()
    {
        var rows = new[]
        {
            new Item("A001", "Apple",  1.50m, 10),
            new Item("B002", "Banana", 0.75m, 20),
            new Item("C003", "Cherry", 5.00m, 5)
        };

        var report = MakeReport(new DetailBand
        {
            Height = 0,
            Elements = new ReportElement[]
            {
                new TableElement<Item>
                {
                    Bounds = new Rect(0, 0, 400, 0),
                    Rows = rows,
                    Columns = new[]
                    {
                        new TableColumn<Item>("Code",  Bind.From<Item, object?>(r => r.Code), 80),
                        new TableColumn<Item>("Name",  Bind.From<Item, object?>(r => r.Name), 160),
                        new TableColumn<Item>("Price", Bind.From<Item, object?>(r => r.Price), 80, "N2", TextAlignment.Right),
                        new TableColumn<Item>("Qty",   Bind.From<Item, object?>(r => r.Qty),   80, null, TextAlignment.Right)
                    }
                }
            }
        });

        var ws = OpenFirstSheet(new XlsxRenderer().Render(report));

        // Header
        Assert.Equal("Code",  ws.Cell(1, 1).GetString());
        Assert.Equal("Name",  ws.Cell(1, 2).GetString());
        Assert.Equal("Price", ws.Cell(1, 3).GetString());
        Assert.Equal("Qty",   ws.Cell(1, 4).GetString());

        // Data
        Assert.Equal("A001", ws.Cell(2, 1).GetString());
        Assert.Equal("Apple", ws.Cell(2, 2).GetString());
        Assert.Equal(1.50, ws.Cell(2, 3).GetDouble());  // numeric, not string
        Assert.Equal(10, ws.Cell(2, 4).GetDouble());
        Assert.Equal(0.75, ws.Cell(3, 3).GetDouble());
        Assert.Equal(5.00, ws.Cell(4, 3).GetDouble());
    }

    [Fact]
    public void Render_Table_PreservesNumericTypeForSorting()
    {
        var rows = new[] { new Item("A", "x", 100m, 1), new Item("B", "y", 50m, 2) };
        var report = MakeReport(new DetailBand
        {
            Height = 0,
            Elements = new ReportElement[]
            {
                new TableElement<Item>
                {
                    Bounds = new Rect(0, 0, 400, 0),
                    Rows = rows,
                    Columns = new[]
                    {
                        new TableColumn<Item>("Price", Bind.From<Item, object?>(r => r.Price), 80, "N2")
                    }
                }
            }
        });

        var ws = OpenFirstSheet(new XlsxRenderer().Render(report));
        // El valor debe ser numérico (no string) para que Excel pueda sortear/sumar.
        Assert.Equal(XLDataType.Number, ws.Cell(2, 1).DataType);
        Assert.Equal(XLDataType.Number, ws.Cell(3, 1).DataType);
    }

    [Fact]
    public void Render_Table_AppliesNumberFormat()
    {
        var rows = new[] { new Item("A", "x", 1234.5m, 1) };
        var report = MakeReport(new DetailBand
        {
            Height = 0,
            Elements = new ReportElement[]
            {
                new TableElement<Item>
                {
                    Bounds = new Rect(0, 0, 400, 0),
                    Rows = rows,
                    Columns = new[]
                    {
                        new TableColumn<Item>("P", Bind.From<Item, object?>(r => r.Price), 80, "N2")
                    }
                }
            }
        });

        var ws = OpenFirstSheet(new XlsxRenderer().Render(report));
        // N2 → Excel format con 2 decimales y miles.
        Assert.Equal("#,##0.00", ws.Cell(2, 1).Style.NumberFormat.Format);
    }

    [Fact]
    public void Render_Table_CreatesNativeExcelTableWhenEnabled()
    {
        var rows = new[] { new Item("A", "x", 1m, 1) };
        var report = MakeReport(new DetailBand
        {
            Height = 0,
            Elements = new ReportElement[]
            {
                new TableElement<Item>
                {
                    Bounds = new Rect(0, 0, 400, 0),
                    Rows = rows,
                    Columns = new[]
                    {
                        new TableColumn<Item>("Code", Bind.From<Item, object?>(r => r.Code), 80)
                    }
                }
            }
        });

        var bytes = new XlsxRenderer().Render(report, new XlsxRenderOptions { EmitNativeTables = true });
        var ws = OpenFirstSheet(bytes);
        Assert.NotEmpty(ws.Tables); // ClosedXML registró al menos una tabla.
    }

    [Fact]
    public void Render_GroupedTable_EmitsHeadersFootersAndAggregates()
    {
        var rows = new[]
        {
            new Item("A001", "Apple",  1m, 10) with { },
            new Item("A002", "Almond", 2m, 20) with { },
            new Item("B001", "Banana", 3m, 30) with { }
        };
        // Group by primera letra del código.
        var report = MakeReport(new DetailBand
        {
            Height = 0,
            Elements = new ReportElement[]
            {
                new TableElement<Item>
                {
                    Bounds = new Rect(0, 0, 400, 0),
                    Rows = rows,
                    GroupBy = Bind.From<Item, object?>(r => r.Code[0]),
                    GroupHeader = new GroupHeader
                    {
                        Height = 18,
                        Content = Expr.Of<string>(c => $"Group {c.GroupKey}")
                    },
                    GroupFooter = new GroupFooter
                    {
                        Height = 18,
                        Cells = new GroupFooterCell?[]
                        {
                            new GroupFooterCell { Content = Expr.Str("Subtotal") },
                            null,
                            new GroupFooterCell { Aggregate = AggregateKind.Sum, Format = "N2" },
                            new GroupFooterCell { Aggregate = AggregateKind.Sum }
                        }
                    },
                    Columns = new[]
                    {
                        new TableColumn<Item>("Code",  Bind.From<Item, object?>(r => r.Code), 80),
                        new TableColumn<Item>("Name",  Bind.From<Item, object?>(r => r.Name), 160),
                        new TableColumn<Item>("Price", Bind.From<Item, object?>(r => r.Price), 80, "N2"),
                        new TableColumn<Item>("Qty",   Bind.From<Item, object?>(r => r.Qty),   80)
                    }
                }
            }
        });

        var ws = OpenFirstSheet(new XlsxRenderer().Render(report));

        // Layout esperado:
        // 1: header row (Code, Name, Price, Qty)
        // 2: group A header (merged)
        // 3-4: 2 rows (A001, A002)
        // 5: footer A (Subtotal, _, sum=3, sum=30)
        // 6: group B header
        // 7: 1 row (B001)
        // 8: footer B (Subtotal, _, sum=3, sum=30)

        Assert.Equal("Code", ws.Cell(1, 1).GetString());
        Assert.Equal("Group A", ws.Cell(2, 1).GetString());
        Assert.Equal("A001", ws.Cell(3, 1).GetString());
        Assert.Equal("A002", ws.Cell(4, 1).GetString());
        Assert.Equal("Subtotal", ws.Cell(5, 1).GetString());
        Assert.Equal(3.0, ws.Cell(5, 3).GetDouble());      // 1 + 2
        Assert.Equal(30.0, ws.Cell(5, 4).GetDouble());     // 10 + 20

        Assert.Equal("Group B", ws.Cell(6, 1).GetString());
        Assert.Equal("B001", ws.Cell(7, 1).GetString());
        Assert.Equal("Subtotal", ws.Cell(8, 1).GetString());
        Assert.Equal(3.0, ws.Cell(8, 3).GetDouble());
        Assert.Equal(30.0, ws.Cell(8, 4).GetDouble());
    }

    [Fact]
    public void Render_GroupedTable_CountAndAvgAggregates()
    {
        var rows = new[]
        {
            new Item("A001", "X", 10m, 1),
            new Item("A002", "Y", 20m, 1),
            new Item("A003", "Z", 30m, 1)
        };
        var report = MakeReport(new DetailBand
        {
            Height = 0,
            Elements = new ReportElement[]
            {
                new TableElement<Item>
                {
                    Bounds = new Rect(0, 0, 400, 0),
                    Rows = rows,
                    GroupBy = Bind.From<Item, object?>(r => r.Code[0]),
                    GroupFooter = new GroupFooter
                    {
                        Height = 18,
                        Cells = new GroupFooterCell?[]
                        {
                            new GroupFooterCell { Aggregate = AggregateKind.Count },
                            null,
                            new GroupFooterCell { Aggregate = AggregateKind.Avg, Format = "N2" }
                        }
                    },
                    Columns = new[]
                    {
                        new TableColumn<Item>("C", Bind.From<Item, object?>(r => r.Code),  80),
                        new TableColumn<Item>("N", Bind.From<Item, object?>(r => r.Name),  80),
                        new TableColumn<Item>("P", Bind.From<Item, object?>(r => r.Price), 80, "N2")
                    }
                }
            }
        });

        var ws = OpenFirstSheet(new XlsxRenderer().Render(report));

        // header(1) + 3 rows(2-4) + footer(5)
        Assert.Equal(3.0, ws.Cell(5, 1).GetDouble());      // count
        Assert.Equal(20.0, ws.Cell(5, 3).GetDouble());     // avg = (10+20+30)/3
    }

    [Fact]
    public void Render_PageSetup_AppliesPaperSize()
    {
        var report = new ReportDefinition
        {
            Name = "Test",
            Page = PageSetup.A4,
            Styles = TestStyles(),
            Bands = Array.Empty<Band>()
        };

        var ws = OpenFirstSheet(new XlsxRenderer().Render(report));
        Assert.Equal(XLPaperSize.A4Paper, ws.PageSetup.PaperSize);
    }

    [Fact]
    public void Render_SheetName_TruncatesAndSanitizes()
    {
        var report = new ReportDefinition
        {
            Name = "Invalid:Name/With\\Bad?Chars*And[Stuff]ThatIsWayTooLongFor31",
            Page = PageSetup.Letter,
            Styles = TestStyles(),
            Bands = Array.Empty<Band>()
        };

        var ws = OpenFirstSheet(new XlsxRenderer().Render(report));
        Assert.True(ws.Name.Length <= 31, $"Sheet name length: {ws.Name.Length}");
        Assert.DoesNotContain(":",  ws.Name);
        Assert.DoesNotContain("/",  ws.Name);
        Assert.DoesNotContain("\\", ws.Name);
        Assert.DoesNotContain("?",  ws.Name);
        Assert.DoesNotContain("*",  ws.Name);
        Assert.DoesNotContain("[",  ws.Name);
        Assert.DoesNotContain("]",  ws.Name);
    }

    [Fact]
    public void Render_SkipsOrnamentalElements()
    {
        // Líneas, rectángulos y barcodes no son semánticos en XLSX → se ignoran.
        var report = MakeReport(new ReportHeaderBand
        {
            Height = 30,
            Elements = new ReportElement[]
            {
                new TextElement { Bounds = new Rect(0, 0, 100, 14), Content = Expr.Str("Title") },
                new LineElement { Bounds = new Rect(0, 20, 100, 1) },
                new RectangleElement { Bounds = new Rect(0, 25, 100, 5) }
            }
        });

        var ws = OpenFirstSheet(new XlsxRenderer().Render(report));
        Assert.Equal("Title", ws.Cell(1, 1).GetString());
        // No deberían existir filas adicionales.
        Assert.True(ws.Cell(2, 1).IsEmpty());
    }

    private sealed record Sale(string Region, string Category, double Amount);

    [Fact]
    public void Render_NestedGroups_EmitsHeadersFootersSummary_AndOutline()
    {
        var rows = new[]
        {
            new Sale("Norte", "HW", 100), new Sale("Norte", "HW", 50), new Sale("Norte", "SW", 30),
            new Sale("Sur", "HW", 20)
        };
        GroupFooter Sum(string label) => new()
        {
            Height = 16,
            Cells = new GroupFooterCell?[]
            {
                new() { Content = Expr.Of(ctx => $"{label} {ctx.GroupKey}") },
                new() { Aggregate = AggregateKind.Sum }
            }
        };
        var table = new TableElement<Sale>
        {
            Bounds = new Rect(0, 0, 200, 0),
            Rows = rows,
            Columns = new[]
            {
                new TableColumn<Sale>("Cat", Bind.From<Sale, object?>(s => s.Category), 100),
                new TableColumn<Sale>("Monto", Bind.From<Sale, object?>(s => s.Amount), 100)
            },
            Groups = new[]
            {
                new TableGroupLevel<Sale>
                {
                    By = Bind.From<Sale, object?>(s => s.Region),
                    Header = new GroupHeader { Height = 16, Content = Expr.Of(ctx => $"Región {ctx.GroupKey}") },
                    Footer = Sum("Total")
                },
                new TableGroupLevel<Sale> { By = Bind.From<Sale, object?>(s => s.Category), Footer = Sum("Sub") }
            },
            Summary = new GroupFooter
            {
                Height = 16,
                Cells = new GroupFooterCell?[] { new() { Content = Expr.Str("General") }, new() { Aggregate = AggregateKind.Max } }
            }
        };
        var ws = OpenFirstSheet(new XlsxRenderer().Render(MakeReport(new DetailBand { Height = 0, Elements = new ReportElement[] { table } })));

        var values = ws.RowsUsed().Select(r => r.Cell(1).GetString() + "|" + r.Cell(2).GetString()).ToList();
        Assert.Equal(new[]
        {
            "Cat|Monto", "Región Norte|", "HW|100", "HW|50", "Sub HW|150", "SW|30", "Sub SW|30", "Total Norte|180",
            "Región Sur|", "HW|20", "Sub HW|20", "Total Sur|20", "General|100"
        }, values);

        // Las filas de detalle quedan agrupadas en el outline de Excel.
        Assert.True(ws.Row(3).OutlineLevel >= 1);
        Assert.Empty(ws.Tables); // tablas agrupadas no se envuelven en Excel Table
    }

    [Fact]
    public void Render_Chart_EmitsDataTable()
    {
        var chart = new ChartElement
        {
            Bounds = new Rect(0, 0, 300, 200),
            Kind = ChartKind.Bar,
            Title = "Ventas",
            Categories = new[] { "Ene", "Feb" },
            Series = new[] { new ChartSeries("2025", new double?[] { 10, 20 }), new ChartSeries("2026", new double?[] { 15, null }) }
        };
        var ws = OpenFirstSheet(new XlsxRenderer().Render(MakeReport(new ReportHeaderBand { Height = 200, Elements = new ReportElement[] { chart } })));

        Assert.Equal("Ventas", ws.Cell(1, 1).GetString());
        Assert.Equal("Category", ws.Cell(2, 1).GetString());
        Assert.Equal("2026", ws.Cell(2, 3).GetString());
        Assert.Equal("Feb", ws.Cell(4, 1).GetString());
        Assert.Equal(20, ws.Cell(4, 2).GetDouble());
        Assert.True(ws.Cell(4, 3).IsEmpty());
        Assert.Single(ws.Tables);
    }

    [Fact]
    public void Render_StyleSelector_AppliesDynamicStyle()
    {
        var report = MakeReport(new ReportHeaderBand
        {
            Height = 20,
            Elements = new ReportElement[]
            {
                new TextElement { Bounds = new Rect(0, 0, 100, 20), Content = Expr.Str("X"), StyleSelector = Expr.Of(_ => "Title") }
            }
        });
        var ws = OpenFirstSheet(new XlsxRenderer().Render(report));
        Assert.True(ws.Cell(1, 1).Style.Font.Bold);
        Assert.Equal(18, ws.Cell(1, 1).Style.Font.FontSize);
    }
}
