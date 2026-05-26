namespace TextAPI.PerfViewer;

public sealed partial class MainForm : Form
{
    // ── State ──────────────────────────────────────────────────────────────
    private List<BenchmarkRun> _runs   = [];
    private string?            _historyPath;
    private string?            _selectedBenchmark;
    private int                _lastRunCount = 10;

    // ── Layout constants ───────────────────────────────────────────────────
    private const int SideW    = 240;
    private const int TopH     = 44;
    private const int StatusH  = 28;
    private const int Pad      = 10;

    // ── Controls ───────────────────────────────────────────────────────────
    private readonly ToolStrip          _toolbar      = new();
    private readonly SplitContainer     _split        = new();
    private readonly Panel              _leftPanel    = new();
    private readonly ListBox            _benchList    = new();
    private readonly Label              _lblFilter    = new();
    private readonly TextBox            _filterBox    = new();
    private readonly Panel              _rightPanel   = new();
    private readonly TabControl         _tabs         = new();
    private readonly TabPage            _tabChart     = new();
    private readonly TabPage            _tabGrid      = new();
    private readonly TabPage            _tabCompare   = new();
    private readonly PictureBox         _chartPicture = new();
    private readonly DataGridView       _grid         = new();
    private readonly RichTextBox        _compareBox   = new();
    private readonly StatusStrip        _status       = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly TrackBar           _runSlider    = new();
    private readonly Label              _lblRunCount  = new();
    private readonly NumericUpDown      _nudRuns      = new();

    public MainForm()
    {
        Text            = "TextAPI — Performance History Viewer";
        Size            = new Size(1100, 720);
        MinimumSize     = new Size(800, 500);
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = Color.FromArgb(245, 245, 242);
        Font            = new Font("Segoe UI", 9f);

        BuildToolbar();
        BuildSplit();
        BuildStatus();

        Controls.Add(_split);
        Controls.Add(_toolbar);
        Controls.Add(_status);

        Shown     += (_, _) => AutoLoadHistory();
        Resize    += (_, _) => RedrawChart();
        KeyPreview = true;
        KeyDown   += OnKeyDown;
    }

    // ── Toolbar ────────────────────────────────────────────────────────────

    private void BuildToolbar()
    {
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.BackColor = Color.FromArgb(238, 237, 254);
        _toolbar.Padding   = new Padding(4, 2, 4, 2);
        _toolbar.Height    = TopH;

        var btnOpen = new ToolStripButton("📂  Open history file") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        var btnRefresh = new ToolStripButton("↺  Refresh") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        var btnExport = new ToolStripButton("⬇  Export CSV") { DisplayStyle = ToolStripItemDisplayStyle.Text };

        btnOpen.Click    += (_, _) => OpenFile();
        btnRefresh.Click += (_, _) => Reload();
        btnExport.Click  += (_, _) => ExportCsv();

        var lblRuns = new ToolStripLabel("  Show last ");
        var nudHost = new ToolStripControlHost(BuildNudRuns());
        var lblRuns2 = new ToolStripLabel(" runs");

        _toolbar.Items.AddRange(new ToolStripItem[] {
            btnOpen, new ToolStripSeparator(), btnRefresh, new ToolStripSeparator(),
            new ToolStripSeparator(), btnExport, new ToolStripSeparator(),
            lblRuns, nudHost, lblRuns2
        });
    }

    private NumericUpDown BuildNudRuns()
    {
        _nudRuns.Minimum   = 1;
        _nudRuns.Maximum   = 50;
        _nudRuns.Value     = _lastRunCount;
        _nudRuns.Width     = 50;
        _nudRuns.Font      = Font;
        _nudRuns.ValueChanged += (_, _) => { _lastRunCount = (int)_nudRuns.Value; RefreshAll(); };
        return _nudRuns;
    }

    // ── Split / panels ─────────────────────────────────────────────────────

    private void BuildSplit()
    {
        _split.Dock             = DockStyle.Fill;
        _split.Padding          = new Padding(0, TopH, 0, StatusH);
        _split.SplitterDistance = SideW;
        _split.Panel1MinSize    = 180;
       
        BuildLeftPanel();
        BuildRightPanel();

        _split.Panel1.Controls.Add(_leftPanel);
        _split.Panel2.Controls.Add(_rightPanel);
    }

    private void BuildLeftPanel()
    {
        _leftPanel.Dock      = DockStyle.Fill;
        _leftPanel.BackColor = Color.FromArgb(238, 237, 254);
        _leftPanel.Padding   = new Padding(Pad);

        var lblBench = new Label { Text = "Benchmarks", Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Dock = DockStyle.Top, Height = 22, ForeColor = Color.FromArgb(60, 52, 137) };

        _lblFilter.Text = "Filter:"; _lblFilter.Dock = DockStyle.Top; _lblFilter.Height = 20;
        _filterBox.Dock = DockStyle.Top; _filterBox.Height = 24;
        _filterBox.TextChanged += (_, _) => PopulateBenchList();
        _filterBox.Font = new Font("Segoe UI", 9f);

        _benchList.Dock            = DockStyle.Fill;
        _benchList.BorderStyle     = BorderStyle.None;
        _benchList.BackColor       = Color.FromArgb(238, 237, 254);
        _benchList.Font            = new Font("Segoe UI", 8.5f);
        _benchList.ItemHeight      = 22;
        _benchList.SelectedIndexChanged += OnBenchSelected;

        _leftPanel.Controls.Add(_benchList);
        _leftPanel.Controls.Add(_filterBox);
        _leftPanel.Controls.Add(_lblFilter);
        _leftPanel.Controls.Add(lblBench);
    }

    private void BuildRightPanel()
    {
        _rightPanel.Dock    = DockStyle.Fill;
        _rightPanel.Padding = new Padding(Pad, Pad, Pad, Pad);

        _tabs.Dock = DockStyle.Fill;
        _tabs.Font = new Font("Segoe UI", 9f);

        // Chart tab
        _tabChart.Text      = "  Chart  ";
        _chartPicture.Dock  = DockStyle.Fill;
        _chartPicture.SizeMode = PictureBoxSizeMode.Zoom;
        _chartPicture.BackColor = Color.White;
        _chartPicture.Resize += (_, _) => RedrawChart();
        _tabChart.Controls.Add(_chartPicture);

        // Grid tab
        _tabGrid.Text = "  Data table  ";
        BuildGrid();
        _tabGrid.Controls.Add(_grid);

        // Compare tab
        _tabCompare.Text = "  Text comparison  ";
        _compareBox.Dock      = DockStyle.Fill;
        _compareBox.ReadOnly  = true;
        _compareBox.BackColor = Color.FromArgb(252, 252, 252);
        _compareBox.Font      = new Font("Consolas", 9f);
        _compareBox.WordWrap  = false;
        _tabCompare.Controls.Add(_compareBox);

        _tabs.TabPages.AddRange([_tabChart, _tabGrid, _tabCompare]);
        _tabs.SelectedIndexChanged += (_, _) => RefreshCurrentTab();

        _rightPanel.Controls.Add(_tabs);
    }

    private void BuildGrid()
    {
        _grid.Dock                  = DockStyle.Fill;
        _grid.ReadOnly              = true;
        _grid.AllowUserToAddRows    = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible     = false;
        _grid.SelectionMode         = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BorderStyle           = BorderStyle.None;
        _grid.BackgroundColor       = Color.White;
        _grid.GridColor             = Color.FromArgb(211, 209, 199);
        _grid.Font                  = new Font("Segoe UI", 8.5f);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(206, 203, 246);
        _grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(38, 33, 92);
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 237, 254);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(60, 52, 137);
        _grid.ColumnHeadersDefaultCellStyle.Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _grid.EnableHeadersVisualStyles = false;
        _grid.RowTemplate.Height        = 24;
    }

    private void BuildStatus()
    {
        _status.SizingGrip = false;
        _status.BackColor  = Color.FromArgb(238, 237, 254);
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _status.Items.Add(_statusLabel);
    }

    // ── File loading ───────────────────────────────────────────────────────

    private void AutoLoadHistory()
    {
        var path = HistoryLoader.FindHistoryFile(AppContext.BaseDirectory);
        if (path != null) LoadHistory(path);
        else SetStatus("No BenchmarkHistory.json found — use Open to locate it.");
    }

    private void OpenFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Open BenchmarkHistory.json",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "BenchmarkHistory.json"
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            LoadHistory(dlg.FileName);
    }

    private void LoadHistory(string path)
    {
        _historyPath = path;
        _runs        = HistoryLoader.Load(path);
        RefreshAll();
        SetStatus($"Loaded {_runs.Count} runs from {path}");
    }

    private void Reload()
    {
        if (_historyPath != null) LoadHistory(_historyPath);
    }

    private void RefreshAll()
    {
        PopulateBenchList();
        RefreshCurrentTab();
    }

    // ── Left panel ─────────────────────────────────────────────────────────

    private void PopulateBenchList()
    {
        var filter = _filterBox.Text.Trim().ToLower();
        var names  = HistoryLoader.GetAllBenchmarkNames(_runs);
        if (filter.Length > 0)
            names = names.Where(n => n.ToLower().Contains(filter)).ToList();

        _benchList.BeginUpdate();
        _benchList.Items.Clear();
        foreach (var n in names)
        {
            // Display: "Suite: Name [Label]" — parse from key
            var parts   = n.Split('|');
            string suite = parts[0]; string bname = parts[1]; string lbl = parts[2];
            string display = (suite.Length > 0 ? $"{suite}: " : "") + bname + (lbl.Length > 0 ? $" [{lbl}]" : "");
            _benchList.Items.Add(new BenchItem(n, display));
        }

        // Restore selection
        if (_selectedBenchmark != null)
        {
            int idx = -1;
            for (int i = 0; i < _benchList.Items.Count; i++)
                if (_benchList.Items[i] is BenchItem bi && bi.Key == _selectedBenchmark) { idx = i; break; }
            if (idx >= 0) _benchList.SelectedIndex = idx;
            else if (_benchList.Items.Count > 0) _benchList.SelectedIndex = 0;
        }
        else if (_benchList.Items.Count > 0) _benchList.SelectedIndex = 0;

        _benchList.EndUpdate();
    }

    private void OnBenchSelected(object? sender, EventArgs e)
    {
        _selectedBenchmark = (_benchList.SelectedItem as BenchItem)?.Key;
        RefreshCurrentTab();
    }

    // ── Tab refresh ────────────────────────────────────────────────────────

    private void RefreshCurrentTab()
    {
        if (_tabs.SelectedTab == _tabChart)   RedrawChart();
        else if (_tabs.SelectedTab == _tabGrid)    RefreshGrid();
        else if (_tabs.SelectedTab == _tabCompare) RefreshCompare();
    }

    // ── Chart tab ─────────────────────────────────────────────────────────

    private void RedrawChart()
    {
        if (_tabs.SelectedTab != _tabChart) return;
        var sz = _chartPicture.ClientSize;
        if (sz.Width < 10 || sz.Height < 10)
        {
            // Layout not complete yet — defer until the message pump settles
            BeginInvoke(RedrawChart);
            return;
        }
        var bmp = DrawChart(sz);
        var old = _chartPicture.Image;
        _chartPicture.Image = bmp;
        old?.Dispose();
    }

    private Bitmap DrawChart(Size size)
    {
        if (size.Width < 10 || size.Height < 10)
            return new Bitmap(1, 1);

        var bmp = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode   = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.White);

        if (_selectedBenchmark == null || _runs.Count == 0)
        {
            DrawCentredText(g, size, "Select a benchmark from the left panel", Color.Gray);
            return bmp;
        }

        var visRuns = _runs.TakeLast(_lastRunCount).ToList();
        var data    = visRuns.Select(r =>
        {
            var b = r.Results.FirstOrDefault(x => x.FullName == _selectedBenchmark);
            return (Run: r, Ms: b?.Ms ?? -1L);
        }).ToList();

        var validData = data.Where(d => d.Ms >= 0).ToList();
        if (validData.Count == 0)
        {
            DrawCentredText(g, size, $"No data for\n{_selectedBenchmark}", Color.Gray);
            return bmp;
        }

        // Layout
        const int padL = 70, padR = 20, padT = 50, padB = 60;
        int w = size.Width - padL - padR;
        int h = size.Height - padT - padB;

        long maxVal = validData.Max(d => d.Ms);
        if (maxVal == 0) maxVal = 1;

        // Title
        using var titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.FromArgb(60, 52, 137));
        g.DrawString(_selectedBenchmark, titleFont, titleBrush, padL, 12);

        // Y axis grid lines
        int gridLines = 5;
        using var gridPen = new Pen(Color.FromArgb(230, 229, 225));
        using var axisFont = new Font("Segoe UI", 7.5f);
        using var axisB    = new SolidBrush(Color.FromArgb(136, 135, 128));

        for (int i = 0; i <= gridLines; i++)
        {
            float y   = padT + h - (i * h / gridLines);
            long  val = i * maxVal / gridLines;
            g.DrawLine(gridPen, padL, y, padL + w, y);
            var label = val >= 1000 ? $"{val / 1000.0:0.#}s" : $"{val}ms";
            var sz    = g.MeasureString(label, axisFont);
            g.DrawString(label, axisFont, axisB, padL - sz.Width - 4, y - sz.Height / 2);
        }

        // Bars
        int barCount = data.Count;
        float barW   = Math.Min(60f, w / (float)barCount * 0.7f);
        float gap     = w / (float)barCount;

        var barColour     = Color.FromArgb(29, 158, 117);
        var barColourMiss = Color.FromArgb(211, 209, 199);
        var barColourBad  = Color.FromArgb(226, 75, 74);

        using var labelFont = new Font("Segoe UI", 7.5f);
        using var labelB    = new SolidBrush(Color.FromArgb(60, 60, 60));
        using var deltaGoodB = new SolidBrush(Color.FromArgb(29, 158, 117));
        using var deltaBadB  = new SolidBrush(Color.FromArgb(163, 45, 45));

        for (int i = 0; i < data.Count; i++)
        {
            var (run, ms) = data[i];
            float cx  = padL + gap * i + gap / 2f;
            float bx  = cx - barW / 2f;

            // X label
            var lbl  = run.RunId.Length >= 8 ? run.RunId[..8] : run.RunId;
            var lbl2 = run.Timestamp.Length >= 11 ? run.Timestamp[5..] : run.Timestamp;
            var lsz  = g.MeasureString(lbl, labelFont);
            g.DrawString(lbl, labelFont, labelB, cx - lsz.Width / 2f, padT + h + 6);
            var lsz2 = g.MeasureString(lbl2, labelFont);
            g.DrawString(lbl2, labelFont, axisB, cx - lsz2.Width / 2f, padT + h + 20);

            if (ms < 0)
            {
                // No data for this run — grey dash
                using var dashPen = new Pen(barColourMiss, 2);
                g.DrawLine(dashPen, cx - 10, padT + h, cx + 10, padT + h);
                continue;
            }

            float barH = (float)(ms * h / maxVal);
            float by   = padT + h - barH;

            // Colour: green if improving, red if >10% worse than previous non-null
            Color colour = barColour;
            if (i > 0)
            {
                var prev = data[..i].LastOrDefault(d => d.Ms >= 0);
                if (prev.Ms > 0)
                {
                    double delta = (ms - prev.Ms) * 100.0 / prev.Ms;
                    if (delta > 10) colour = barColourBad;
                }
            }

            using var barBrush = new SolidBrush(colour);
            g.FillRectangle(barBrush, bx, by, barW, barH);

            // Value label on top
            var vLabel = ms >= 1000 ? $"{ms / 1000.0:0.#}s" : $"{ms}ms";
            var vsz    = g.MeasureString(vLabel, labelFont);
            g.DrawString(vLabel, labelFont, labelB, cx - vsz.Width / 2f, Math.Max(padT - 2, by - vsz.Height - 2));

            // Delta label
            if (i > 0)
            {
                var prev = data[..i].LastOrDefault(d => d.Ms >= 0);
                if (prev.Ms > 0)
                {
                    double pct = (ms - prev.Ms) * 100.0 / prev.Ms;
                    var dLabel = $"{(pct > 0 ? "+" : "")}{pct:0}%";
                    var db     = pct > 5 ? deltaBadB : (pct < -5 ? deltaGoodB : axisB);
                    var dsz    = g.MeasureString(dLabel, labelFont);
                    g.DrawString(dLabel, labelFont, db, cx - dsz.Width / 2f, by - vsz.Height - 14);
                }
            }
        }

        // Axes
        using var axisPen = new Pen(Color.FromArgb(136, 135, 128));
        g.DrawLine(axisPen, padL, padT, padL, padT + h);
        g.DrawLine(axisPen, padL, padT + h, padL + w, padT + h);

        return bmp;
    }

    private static void DrawCentredText(Graphics g, Size size, string text, Color color)
    {
        using var f = new Font("Segoe UI", 10f);
        using var b = new SolidBrush(color);
        var sz = g.MeasureString(text, f);
        g.DrawString(text, f, b, (size.Width - sz.Width) / 2f, (size.Height - sz.Height) / 2f);
    }

    // ── Grid tab ──────────────────────────────────────────────────────────

    private void RefreshGrid()
    {
        _grid.Columns.Clear();
        _grid.Rows.Clear();

        var visRuns = _runs.TakeLast(_lastRunCount).ToList();
        if (visRuns.Count == 0) return;

        var names = HistoryLoader.GetAllBenchmarkNames(visRuns);

        // Columns: Benchmark, then one per run
        _grid.Columns.Add("bench", "Benchmark");
        _grid.Columns["bench"]!.FillWeight = 200;

        foreach (var run in visRuns)
        {
            var col = new DataGridViewTextBoxColumn
            {
                Name      = run.RunId,
                HeaderText = $"{run.RunId[..Math.Min(8, run.RunId.Length)]}\n{run.Timestamp[5..Math.Min(15, run.Timestamp.Length)]}",
                FillWeight = 80
            };
            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Columns.Add(col);
        }

        // Delta column
        var deltaCol = new DataGridViewTextBoxColumn
        {
            Name = "delta", HeaderText = "Δ last", FillWeight = 60
        };
        deltaCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.Columns.Add(deltaCol);

        // Rows
        foreach (var name in names)
        {
            var cells = new List<object?> { name };
            long? prevMs = null;
            foreach (var run in visRuns)
            {
                var b = run.Results.FirstOrDefault(r => $"{r.Suite}|{r.Name}|{r.Label}" == name);
                cells.Add(b != null ? $"{b.Ms}ms" : "-");
                if (b != null) prevMs = b.Ms;
            }
            // Delta: compare last two non-null
            string deltaStr = "-";
            var nonNull = visRuns
                .Select(r => r.Results.FirstOrDefault(b => b.FullName == name))
                .Where(b => b != null).ToList();
            if (nonNull.Count >= 2)
            {
                var d = HistoryLoader.Delta(nonNull[^2]!.Ms, nonNull[^1]!.Ms);
                if (d.HasValue)
                {
                    deltaStr = $"{(d.Value > 0 ? "+" : "")}{d.Value:0}%";
                }
            }
            cells.Add(deltaStr);

            int rowIdx = _grid.Rows.Add(cells.ToArray());

            // Colour the delta cell
            var deltaCell = _grid.Rows[rowIdx].Cells["delta"];
            if (deltaStr.StartsWith('+')) deltaCell.Style.ForeColor = Color.FromArgb(163, 45, 45);
            else if (deltaStr.StartsWith('-')) deltaCell.Style.ForeColor = Color.FromArgb(15, 110, 86);
        }
    }

    // ── Compare tab ───────────────────────────────────────────────────────

    private void RefreshCompare()
    {
        _compareBox.Clear();
        var visRuns = _runs.TakeLast(_lastRunCount).ToList();
        if (visRuns.Count == 0) { _compareBox.Text = "No data."; return; }

        var names = HistoryLoader.GetAllBenchmarkNames(visRuns);

        // Header
        var sb   = new System.Text.StringBuilder();
        var hdr  = "Benchmark".PadRight(46);
        foreach (var r in visRuns)
            hdr += $" {r.RunId[..Math.Min(8, r.RunId.Length)],10}";
        hdr += $"  {"Δ last",8}";
        sb.AppendLine(new string('─', hdr.Length));
        sb.AppendLine(hdr);
        sb.AppendLine(new string('─', hdr.Length));

        // Rows
        foreach (var name in names)
        {
            var row = name.PadRight(46);
            long prev = 0;
            long last = 0;
            foreach (var run in visRuns)
            {
                var b = run.Results.FirstOrDefault(r => $"{r.Suite}|{r.Name}|{r.Label}" == name);
                if (b != null) { row += $" {b.Ms + "ms",10}"; prev = last; last = b.Ms; }
                else row += $" {"-",10}";
            }
            var d = prev > 0 ? HistoryLoader.Delta(prev, last) : null;
            var dStr = d.HasValue ? $"  {(d.Value > 0 ? "+" : "")}{d.Value:0}%".PadLeft(9) : "         ";
            row += dStr;
            sb.AppendLine(row);
        }

        sb.AppendLine(new string('─', hdr.Length));
        sb.AppendLine();

        // Run metadata
        foreach (var r in visRuns)
            sb.AppendLine($"{r.RunId[..Math.Min(8,r.RunId.Length)]}  {r.Timestamp}  {r.Machine}  [{r.Suite}]");

        _compareBox.Text = sb.ToString();

        // Colour delta values
        ColourDeltas();
    }

    private void ColourDeltas()
    {
        // Colour +N% red, -N% green in the RichTextBox
        var text = _compareBox.Text;
        int pos  = 0;
        while (pos < text.Length)
        {
            int plus  = text.IndexOf('+', pos);
            int minus = text.IndexOf('-', pos);
            int next  = (plus < 0 && minus < 0) ? -1
                       : (plus < 0) ? minus
                       : (minus < 0) ? plus
                       : Math.Min(plus, minus);
            if (next < 0) break;

            // Check if this is a delta % (followed by digits and %)
            int end = next + 1;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.')) end++;
            if (end < text.Length && text[end] == '%')
            {
                _compareBox.Select(next, end - next + 1);
                _compareBox.SelectionColor = text[next] == '+'
                    ? Color.FromArgb(163, 45, 45)
                    : Color.FromArgb(15, 110, 86);
            }
            pos = end + 1;
        }
        _compareBox.SelectionStart  = 0;
        _compareBox.SelectionLength = 0;
    }

    // ── Export ────────────────────────────────────────────────────────────

    private void ExportCsv()
    {
        using var dlg = new SaveFileDialog
        {
            Title    = "Export CSV",
            Filter   = "CSV files (*.csv)|*.csv",
            FileName = "benchmark_history.csv"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var visRuns = _runs.TakeLast(_lastRunCount).ToList();
        var names   = HistoryLoader.GetAllBenchmarkNames(visRuns);
        var sb      = new System.Text.StringBuilder();

        // Header
        sb.Append("Benchmark");
        foreach (var r in visRuns) sb.Append($",{r.RunId[..8]} {r.Timestamp}");
        sb.AppendLine(",Delta%");

        foreach (var name in names)
        {
            sb.Append($"\"{name}\"");
            long prev = 0, last = 0;
            foreach (var run in visRuns)
            {
                var b = run.Results.FirstOrDefault(r => $"{r.Suite}|{r.Name}|{r.Label}" == name);
                if (b != null) { sb.Append($",{b.Ms}"); prev = last; last = b.Ms; }
                else sb.Append(",");
            }
            var d = prev > 0 ? HistoryLoader.Delta(prev, last) : null;
            sb.AppendLine(d.HasValue ? $",{d.Value:0.0}" : ",");
        }

        File.WriteAllText(dlg.FileName, sb.ToString());
        SetStatus($"Exported to {dlg.FileName}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void SetStatus(string msg)
        => _statusLabel.Text = $"  {msg}";

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5) Reload();
    }
}
