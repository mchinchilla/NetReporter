using System.ComponentModel;
using System.Globalization;
using NetReporter.Core.Definition;
using NetReporter.Viewer.Desktop;

namespace NetReporter.Viewer.WinForms;

/// <summary>
/// Report viewer for Windows Forms: a toolbar (page navigation, zoom, export) over a
/// <see cref="ReportPageView"/>. Open a report with <see cref="LoadAsync"/> — the layout runs once, off
/// the UI thread — or show a document you built with <see cref="SetDocument"/>.
/// <code>
/// var viewer = new ReportViewer { Dock = DockStyle.Fill };
/// form.Controls.Add(viewer);
/// await viewer.LoadAsync(template.Bind(json));
/// </code>
/// </summary>
[ToolboxItem(true)]
public class ReportViewer : UserControl
{
    private static readonly Font SymbolFont = new("Segoe UI Symbol", 9f);

    private readonly ReportPageView _view = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.Gainsboro,
        AutoSize = false,
    };
    private readonly ToolStrip _toolbar = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
    private readonly ToolStripButton _first = SymbolButton("⏮");
    private readonly ToolStripButton _previous = SymbolButton("◀");
    private readonly ToolStripTextBox _pageBox = new() { AutoSize = false, Width = 44, TextBoxTextAlign = HorizontalAlignment.Center };
    private readonly ToolStripLabel _pageOf = new();
    private readonly ToolStripButton _next = SymbolButton("▶");
    private readonly ToolStripButton _last = SymbolButton("⏭");
    private readonly ToolStripButton _zoomOut = SymbolButton("−");
    private readonly ToolStripComboBox _zoomBox = new() { AutoSize = false, Width = 120, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ToolStripButton _zoomIn = SymbolButton("+");
    private readonly ToolStripDropDownButton _export = new() { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripMenuItem _exportPdf = new() { Tag = ExportFormat.Pdf };
    private readonly ToolStripMenuItem _exportXlsx = new() { Tag = ExportFormat.Xlsx };
    private readonly ToolStripMenuItem _exportHtml = new() { Tag = ExportFormat.Html };

    private DesktopReportDocument? _document;
    private bool _ownsDocument;
    private CancellationTokenSource? _loading;
    private CultureInfo? _uiCulture;
    private ViewerStrings _strings = new(null);
    private bool _syncing;
    private bool _busy;

    public ReportViewer()
    {
        SuspendLayout();
        _toolbar.Items.AddRange([
            _first, _previous, _pageBox, _pageOf, _next, _last,
            new ToolStripSeparator(),
            _zoomOut, _zoomBox, _zoomIn,
            new ToolStripSeparator(),
            _export,
        ]);
        _export.DropDownItems.AddRange([_exportPdf, _exportXlsx, _exportHtml]);

        _first.Click += (_, _) => GoToPage(0);
        _previous.Click += (_, _) => GoToPage(CurrentPage - 1);
        _next.Click += (_, _) => GoToPage(CurrentPage + 1);
        _last.Click += (_, _) => GoToPage(PageCount - 1);
        _pageBox.KeyDown += OnPageBoxKeyDown;
        _pageBox.Leave += (_, _) => SyncToolbar();
        _zoomIn.Click += (_, _) => _view.ZoomIn();
        _zoomOut.Click += (_, _) => _view.ZoomOut();
        _zoomBox.SelectedIndexChanged += OnZoomBoxSelected;
        _zoomBox.KeyDown += OnZoomBoxKeyDown;
        _zoomBox.Leave += (_, _) => SyncToolbar();
        foreach (var item in new[] { _exportPdf, _exportXlsx, _exportHtml })
            item.Click += OnExportClick;

        _view.CurrentPageChanged += (_, _) =>
        {
            SyncToolbar();
            CurrentPageChanged?.Invoke(this, EventArgs.Empty);
        };
        _view.ZoomChanged += (_, _) =>
        {
            SyncToolbar();
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        };
        _status.BackColor = _view.BackColor;

        Controls.Add(_view);
        Controls.Add(_status);
        Controls.Add(_toolbar);
        Size = new Size(640, 480);
        ResumeLayout(true);

        ApplyStrings();
        ShowStatus(_strings.NoDocument);
    }

    /// <summary>Raised after the shown document changes (opened, replaced or cleared).</summary>
    public event EventHandler? DocumentChanged;

    /// <summary>Raised when the reader moves to another page.</summary>
    public event EventHandler? CurrentPageChanged;

    /// <summary>Raised when the zoom or zoom mode changes.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>Layout and export options used by <see cref="LoadAsync"/>.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DesktopReportOptions Options { get; set; } = new();

    /// <summary>The document shown, or null.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DesktopReportDocument? Document => _document;

    /// <summary>The page view, for hosts that want to tweak colors or handle its events directly.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ReportPageView PageView => _view;

    /// <summary>Culture of the toolbar and dialogs (English and Spanish built in). Null = the thread's UI culture.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CultureInfo? UICulture
    {
        get => _uiCulture;
        set
        {
            _uiCulture = value;
            _strings = new ViewerStrings(value);
            ApplyStrings();
        }
    }

    [DefaultValue(true)]
    [Category("Appearance")]
    public bool ShowToolbar
    {
        get => _toolbar.Visible;
        set => _toolbar.Visible = value;
    }

    [DefaultValue(ZoomMode.PageWidth)]
    [Category("Behavior")]
    public ZoomMode ZoomMode
    {
        get => _view.ZoomMode;
        set => _view.ZoomMode = value;
    }

    /// <summary>The effective zoom (1 = actual size). Setting it switches to <see cref="Desktop.ZoomMode.Custom"/>.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Zoom
    {
        get => _view.Zoom;
        set => _view.Zoom = value;
    }

    /// <summary>Zero-based index of the current page.</summary>
    [Browsable(false)]
    public int CurrentPage => _view.CurrentPage;

    [Browsable(false)]
    public int PageCount => _view.PageCount;

    public void GoToPage(int pageIndex) => _view.GoToPage(pageIndex);

    /// <summary>
    /// Lays <paramref name="report"/> out on the thread pool and shows it; the viewer owns and disposes the
    /// document. A newer call (or <see cref="SetDocument"/>) supersedes a pending one, which then returns
    /// without showing anything. Layout errors are shown in the viewer and rethrown.
    /// </summary>
    public async Task LoadAsync(ReportDefinition report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        _loading?.Cancel();
        using var loading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loading = loading;
        ShowStatus(_strings.Loading);
        SetBusy(true);
        try
        {
            var document = await DesktopReportDocument.CreateAsync(report, Options, loading.Token);
            if (!ReferenceEquals(_loading, loading) || IsDisposed)
            {
                document.Dispose();     // superseded while laying out
                return;
            }
            Show(document, owns: true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Superseded by a newer load.
        }
        catch (OperationCanceledException) when (ReferenceEquals(_loading, loading) && !IsDisposed)
        {
            // Cancelled by the caller: back to whatever was shown before.
            if (_document is null) ShowStatus(_strings.NoDocument);
            else HideStatus();
            throw;
        }
        catch (Exception ex) when (ReferenceEquals(_loading, loading) && !IsDisposed && ex is not OperationCanceledException)
        {
            Show(null, owns: false);
            ShowStatus(_strings.Format(_strings.LoadFailed, ex.Message));
            throw;
        }
        finally
        {
            if (ReferenceEquals(_loading, loading))
            {
                _loading = null;
                SetBusy(false);
            }
        }
    }

    /// <summary>Shows a document (null clears the viewer). With <paramref name="takeOwnership"/> the viewer disposes it.</summary>
    public void SetDocument(DesktopReportDocument? document, bool takeOwnership = false)
    {
        _loading?.Cancel();
        _loading = null;
        SetBusy(false);
        Show(document, takeOwnership);
    }

    /// <summary>Exports the shown document to a file.</summary>
    public async Task ExportAsync(ExportFormat format, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var document = _document ?? throw new InvalidOperationException("No document is open.");
        // Render first, then write: a render error must not leave a truncated file over the user's old one.
        var bytes = await Task.Run(() => document.Export(format, cancellationToken), cancellationToken);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
    }

    /// <summary>
    /// Asks for a file name (save dialog, pre-filled with the report's file name) and exports to it.
    /// Returns false when the user cancels; errors are reported in a message box.
    /// </summary>
    public async Task<bool> ExportWithDialogAsync(ExportFormat format)
    {
        var document = _document;
        if (document is null || _busy) return false;

        using var dialog = new SaveFileDialog
        {
            FileName = document.SuggestedFileName(format),
            Filter = format switch
            {
                ExportFormat.Pdf => _strings.FilterPdf,
                ExportFormat.Xlsx => _strings.FilterXlsx,
                _ => _strings.FilterHtml,
            },
            DefaultExt = format.Extension(),
            AddExtension = true,
            OverwritePrompt = true,
            RestoreDirectory = true,    // a control must not move the host's current directory
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return false;

        SetBusy(true);
        var cursor = Cursor;
        Cursor = Cursors.WaitCursor;
        try
        {
            await ExportAsync(format, dialog.FileName);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), _strings.Format(_strings.ExportFailed, ex.Message), _strings.ErrorTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            Cursor = cursor;
            SetBusy(false);
        }
    }

    // ---- internals ------------------------------------------------------------------------------

    private void Show(DesktopReportDocument? document, bool owns)
    {
        var previous = _document;
        var ownedPrevious = _ownsDocument;
        _document = document;
        _ownsDocument = owns && document is not null;
        _view.Document = document;
        if (ownedPrevious && previous is not null && !ReferenceEquals(previous, document))
            previous.Dispose();

        if (document is null) ShowStatus(_strings.NoDocument);
        else HideStatus();
        SyncToolbar();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowStatus(string text)
    {
        _status.Text = text;
        _status.Visible = true;
        _status.BringToFront();
        _toolbar.SendToBack();      // keep the toolbar docked above the status
    }

    private void HideStatus() => _status.Visible = false;

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SyncToolbar();
    }

    private void ApplyStrings()
    {
        // The symbol buttons' text is a glyph: screen readers get the tooltip as their name.
        Label(_first, _strings.FirstPage);
        Label(_previous, _strings.PreviousPage);
        Label(_next, _strings.NextPage);
        Label(_last, _strings.LastPage);
        Label(_pageBox, _strings.PageNumber);
        Label(_zoomIn, _strings.ZoomIn);
        Label(_zoomOut, _strings.ZoomOut);
        Label(_zoomBox, _strings.ZoomLevel);
        _export.Text = _strings.Export;
        _exportPdf.Text = _strings.ExportPdf;
        _exportXlsx.Text = _strings.ExportXlsx;
        _exportHtml.Text = _strings.ExportHtml;

        _syncing = true;
        try
        {
            _zoomBox.Items.Clear();
            _zoomBox.Items.Add(_strings.PageWidth);
            _zoomBox.Items.Add(_strings.WholePage);
            foreach (var preset in ZoomLevels.Presets)
                _zoomBox.Items.Add(ZoomText(preset));
        }
        finally
        {
            _syncing = false;
        }
        if (_document is null && _status.Visible && _loading is null) _status.Text = _strings.NoDocument;
        SyncToolbar();
    }

    private static void Label(ToolStripItem item, string text)
    {
        item.ToolTipText = text;
        item.AccessibleName = text;
    }

    private string ZoomText(double zoom) => (zoom * 100).ToString("0", _strings.Culture) + " %";

    private void SyncToolbar()
    {
        var hasDocument = _document is not null;
        var page = CurrentPage;
        var count = PageCount;
        _first.Enabled = _previous.Enabled = hasDocument && page > 0;
        _next.Enabled = _last.Enabled = hasDocument && page < count - 1;
        _pageBox.Enabled = hasDocument && count > 1;
        _zoomIn.Enabled = hasDocument && Zoom < ZoomLevels.Max - 1e-9;
        _zoomOut.Enabled = hasDocument && Zoom > ZoomLevels.Min + 1e-9;
        _zoomBox.Enabled = hasDocument;
        _export.Enabled = hasDocument && !_busy;

        _syncing = true;
        try
        {
            if (!_pageBox.Focused)
                _pageBox.Text = hasDocument ? (page + 1).ToString(_strings.Culture) : "";
            _pageOf.Text = hasDocument ? _strings.Format(_strings.PageOf, count) : "";
            if (!_zoomBox.Focused)
            {
                switch (ZoomMode)
                {
                    case ZoomMode.PageWidth: _zoomBox.SelectedIndex = 0; break;
                    case ZoomMode.WholePage: _zoomBox.SelectedIndex = 1; break;
                    default:
                        var preset = -1;
                        for (var i = 0; i < ZoomLevels.Presets.Count; i++)
                            if (Math.Abs(ZoomLevels.Presets[i] - Zoom) < 1e-6) preset = i;
                        if (preset >= 0) _zoomBox.SelectedIndex = preset + 2;
                        else
                        {
                            _zoomBox.SelectedIndex = -1;
                            _zoomBox.Text = ZoomText(Zoom);
                        }
                        break;
                }
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnPageBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        if (int.TryParse(_pageBox.Text.Trim(), NumberStyles.Integer, _strings.Culture, out var number))
            GoToPage(number - 1);
        _view.Focus();
        SyncToolbar();
    }

    private void OnZoomBoxSelected(object? sender, EventArgs e)
    {
        if (_syncing) return;
        switch (_zoomBox.SelectedIndex)
        {
            case 0: ZoomMode = ZoomMode.PageWidth; break;
            case 1: ZoomMode = ZoomMode.WholePage; break;
            case > 1 and var i when i - 2 < ZoomLevels.Presets.Count: Zoom = ZoomLevels.Presets[i - 2]; break;
            default: return;
        }
        _view.Focus();
    }

    private void OnZoomBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        var text = _zoomBox.Text.Replace("%", "").Trim();
        if (double.TryParse(text, NumberStyles.Float, _strings.Culture, out var percent) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
            Zoom = percent / 100;
        _view.Focus();
        SyncToolbar();
    }

    private async void OnExportClick(object? sender, EventArgs e)
    {
        // async void is the event-handler contract; ExportWithDialogAsync reports its own errors.
        if (sender is ToolStripItem { Tag: ExportFormat format })
            await ExportWithDialogAsync(format);
    }

    private static ToolStripButton SymbolButton(string glyph) => new(glyph)
    {
        DisplayStyle = ToolStripItemDisplayStyle.Text,
        Font = SymbolFont,
        AutoToolTip = false,
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _loading?.Cancel();
            _loading = null;
            var owned = _ownsDocument ? _document : null;
            _view.Document = null;
            _document = null;
            owned?.Dispose();
        }
        base.Dispose(disposing);
    }
}
