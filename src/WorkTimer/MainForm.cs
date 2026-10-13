using System.Globalization;
using System.Text;

namespace WorkTimer;

public class MainForm : Form
{
    static readonly Color Blue = Color.FromArgb(0x2b, 0x7d, 0xe9);
    static readonly Color Red = Color.FromArgb(0xd9, 0x53, 0x4f);
    static readonly Color Gray = Color.FromArgb(0x66, 0x66, 0x66);

    readonly Label taskLabel = new() { Text = "作業名", AutoSize = true };
    readonly TextBox taskBox = new() { Dock = DockStyle.Top, PlaceholderText = "案件・チケットなど" };
    readonly Label errorLabel = new() { Text = "作業名を入力してください", ForeColor = Red, AutoSize = true, Visible = false };
    readonly Label clockLabel = new() { Text = "00:00:00", Font = new Font("Segoe UI", 36), AutoSize = true };
    readonly Button toggleButton = new() { Text = "開始", AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, Cursor = Cursors.Hand };
    readonly ComboBox monthBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    readonly Button exportButton = new() { Text = "CSV出力", AutoSize = true };
    readonly Button pdfButton = new() { Text = "報告書PDF", AutoSize = true };
    readonly Panel historyPanel = new() { Dock = DockStyle.Fill, AutoScroll = true };
    readonly System.Windows.Forms.Timer tick = new() { Interval = 1000 };

    Data data = Store.Load();
    bool rendering;

    public MainForm()
    {
        Text = "工数タイマー";
        ClientSize = new Size(560, 720);
        BackColor = Color.FromArgb(0xf5, 0xf6, 0xf8);

        var timerPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = Color.White,
            Padding = new Padding(16), Margin = new Padding(0),
        };
        timerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var c in new Control[] { taskLabel, taskBox, errorLabel, clockLabel, toggleButton })
        {
            c.Anchor = c == taskBox ? AnchorStyles.Left | AnchorStyles.Right
                : c == taskLabel ? AnchorStyles.Left
                : AnchorStyles.None;
            c.Margin = new Padding(0, 6, 0, 6);
            timerPanel.Controls.Add(c);
        }
        toggleButton.Padding = new Padding(24, 4, 24, 4);
        toggleButton.FlatAppearance.BorderSize = 0;

        var exportPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 12, 16, 0) };
        exportPanel.Controls.AddRange([monthBox, exportButton, pdfButton]);

        historyPanel.Padding = new Padding(16, 0, 16, 16);
        Controls.Add(historyPanel);
        Controls.Add(exportPanel);
        Controls.Add(timerPanel);

        var suggest = new AutoCompleteStringCollection();
        taskBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        taskBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
        taskBox.AutoCompleteCustomSource = suggest;

        taskBox.TextChanged += (_, _) => { if (taskBox.Text.Trim().Length > 0) errorLabel.Visible = false; };
        toggleButton.Click += (_, _) => Toggle();
        monthBox.SelectedIndexChanged += (_, _) => { if (!rendering) RenderHistory(); };
        exportButton.Click += (_, _) => Export();
        pdfButton.Click += (_, _) => ExportPdf();
        tick.Tick += (_, _) => RenderClock();

        RenderTimer();
        RenderHistory();
    }

    void Persist() => Store.Save(data);

    static string Pad(long n) => n.ToString("00", CultureInfo.InvariantCulture);

    void RenderClock()
    {
        var sec = data.Running is { } r ? Math.Max(0, (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - r.Start) / 1000) : 0;
        clockLabel.Text = $"{Pad(sec / 3600)}:{Pad(sec / 60 % 60)}:{Pad(sec % 60)}";
    }

    void RenderTimer()
    {
        var running = data.Running != null;
        toggleButton.Text = running ? "停止" : "開始";
        toggleButton.BackColor = running ? Red : Blue;
        taskBox.Enabled = !running;
        if (running) taskBox.Text = data.Running!.Task;
        tick.Enabled = running;
        RenderClock();
    }

    void Toggle()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (data.Running is { } r)
        {
            data.Sessions.Add(new SessionDto { Id = now, Task = r.Task, Start = r.Start, End = now });
            data.Running = null;
        }
        else
        {
            var task = taskBox.Text.Trim();
            errorLabel.Visible = task.Length == 0;
            if (task.Length == 0) { taskBox.Focus(); return; }
            data.Running = new Running { Task = task, Start = now };
        }
        Persist();
        RenderTimer();
        RenderHistory();
    }

    void Export()
    {
        if (monthBox.SelectedItem is not MonthItem m) return;
        using var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"work-timer-{m.Key}.csv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        // Excelで文字化けしないようBOM付きUTF-8
        File.WriteAllText(dlg.FileName, Hours.ToCsv(data.ToSessions(), m.Key), new UTF8Encoding(true));
    }

    void ExportPdf()
    {
        if (monthBox.SelectedItem is not MonthItem m) return;
        using var dlg = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = $"作業報告書-{m.Key}.pdf" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var days = Hours.SummarizeByDate(data.ToSessions()).Where(d => d.Date.StartsWith(m.Key, StringComparison.Ordinal)).ToList();
        try { ReportPrinter.SavePdf(dlg.FileName, m.Key, days); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            MessageBox.Show(this, $"PDFを出力できませんでした。\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    record MonthItem(string Key)
    {
        public override string ToString() => $"{Key[..4]}年{int.Parse(Key[5..])}月";
    }

    void RenderHistory()
    {
        var sessions = data.ToSessions().ToList();

        taskBox.AutoCompleteCustomSource.Clear();
        taskBox.AutoCompleteCustomSource.AddRange(sessions.Select(s => s.Task).Distinct().ToArray());

        // 当月は記録がなくても選択肢に含め、初期表示にする
        var current = Hours.DateKey(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())[..7];
        var months = Hours.MonthsOf(sessions).Append(current).Distinct().OrderByDescending(x => x, StringComparer.Ordinal).ToList();
        var selected = (monthBox.SelectedItem as MonthItem)?.Key;
        if (selected == null || !months.Contains(selected)) selected = current;
        rendering = true;
        monthBox.Items.Clear();
        monthBox.Items.AddRange(months.Select(x => (object)new MonthItem(x)).ToArray());
        monthBox.SelectedIndex = months.IndexOf(selected);
        rendering = false;

        var days = Hours.SummarizeByDate(sessions).Where(d => d.Date.StartsWith(selected, StringComparison.Ordinal)).ToList();
        exportButton.Enabled = pdfButton.Enabled = days.Count > 0;

        historyPanel.SuspendLayout();
        foreach (Control c in historyPanel.Controls) c.Dispose();
        historyPanel.Controls.Clear();

        var list = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = Color.Transparent,
        };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var day in days)
        {
            var box = BuildDay(day, sessions.Where(s => Hours.DateKey(s.Start) == day.Date).OrderBy(s => s.Start));
            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(0, 12, 0, 0);
            list.Controls.Add(box);
        }
        historyPanel.Controls.Add(list);
        historyPanel.ResumeLayout();
    }

    TableLayoutPanel BuildDay(DaySummary day, IEnumerable<Session> daySessions)
    {
        // 列: 時刻(自動) / 作業名(伸縮) / 工数・削除(自動)。日付と時間の間をあけて見やすくする
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, BackColor = Color.White, Padding = new Padding(12, 8, 12, 8) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Label L(string text, ContentAlignment align = ContentAlignment.MiddleLeft, Font? font = null, Color? color = null) =>
            new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, TextAlign = align, Font = font ?? Font, ForeColor = color ?? ForeColor, Margin = new Padding(0, 3, 0, 3) };

        var bold = new Font(Font, FontStyle.Bold);
        var small = new Font(Font.FontFamily, 9);
        string H(double h) => $"{h.ToString("0.00", CultureInfo.InvariantCulture)} h";

        var head = L(day.Date, font: bold);
        t.Controls.Add(head, 0, 0);
        t.SetColumnSpan(head, 2);
        t.Controls.Add(L(H(day.Total), ContentAlignment.MiddleRight, bold), 2, 0);

        var row = 1;
        foreach (var item in day.Items)
        {
            var name = L(item.Task);
            t.Controls.Add(name, 0, row);
            t.SetColumnSpan(name, 2);
            t.Controls.Add(L(H(item.Hours), ContentAlignment.MiddleRight), 2, row++);
        }

        foreach (var s in daySessions)
        {
            var time = L($"{Hours.ToLocal(s.Start):HH:mm}-{Hours.ToLocal(s.End):HH:mm}", font: small, color: Gray);
            time.Margin = new Padding(0, 3, 24, 3);
            var del = new Button
            {
                Text = "削除", AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Red, Cursor = Cursors.Hand,
                Font = small, Margin = new Padding(0), Anchor = AnchorStyles.Right,
            };
            del.FlatAppearance.BorderSize = 0;
            var id = s.Id;
            del.Click += (_, _) =>
            {
                data.Sessions.RemoveAll(x => x.Id == id);
                Persist();
                RenderHistory();
            };
            t.Controls.Add(time, 0, row);
            t.Controls.Add(L(s.Task, font: small, color: Gray), 1, row);
            t.Controls.Add(del, 2, row++);
        }
        return t;
    }
}
