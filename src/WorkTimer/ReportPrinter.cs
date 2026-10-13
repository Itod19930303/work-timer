using System.Drawing.Printing;
using System.Globalization;

namespace WorkTimer;

// 月次の作業報告書(案件別合計・日別内訳)をPDFに出力する。
// Windows標準の「Microsoft Print to PDF」を使うため、追加のライブラリは不要
public static class ReportPrinter
{
    const string PdfPrinter = "Microsoft Print to PDF";

    record Row(string Left, string Right, bool Bold = false, int Indent = 0, float Size = 10, float Before = 0, bool Rule = false, bool KeepNext = false);

    static string H(double h) => $"{h.ToString("0.00", CultureInfo.InvariantCulture)} h";

    static List<Row> BuildRows(string month, IReadOnlyList<DaySummary> daysDesc, DateTime now)
    {
        var days = daysDesc.OrderBy(d => d.Date, StringComparer.Ordinal).ToList();
        var rows = new List<Row>
        {
            new($"作業報告書 {month[..4]}年{int.Parse(month[5..])}月", "", true, Size: 20),
            new($"作成日: {now:yyyy年M月d日}", "", Size: 9),
            new("総工数", H(days.Sum(d => d.Total)), true, Size: 12, Before: 14, Rule: true),
            new("稼働日数", $"{days.Count} 日", Size: 11),
            new("案件別合計", "", true, Size: 13, Before: 18, Rule: true, KeepNext: true),
        };
        rows.AddRange(Hours.TaskTotals(days).Select(t => new Row(t.Task, H(t.Hours))));
        rows.Add(new("日別内訳", "", true, Size: 13, Before: 18, Rule: true, KeepNext: true));
        foreach (var day in days)
        {
            rows.Add(new(day.Date, H(day.Total), true, Before: 8, KeepNext: true));
            rows.AddRange(day.Items.Select(i => new Row(i.Task, H(i.Hours), Indent: 24)));
        }
        return rows;
    }

    // 指定ファイルにPDFを書き出す。PDF用プリンタが使えない場合は InvalidOperationException
    public static void SavePdf(string path, string month, IReadOnlyList<DaySummary> days)
    {
        var rows = BuildRows(month, days, DateTime.Now);
        using var doc = new PrintDocument();
        doc.PrinterSettings.PrinterName = PdfPrinter;
        if (!doc.PrinterSettings.IsValid)
            throw new InvalidOperationException($"「{PdfPrinter}」が見つかりません。Windowsの機能で有効にしてください。");
        doc.PrinterSettings.PrintToFile = true;
        doc.PrinterSettings.PrintFileName = path;
        doc.PrintController = new StandardPrintController();
        doc.DocumentName = $"作業報告書 {month}";
        doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        var a4 = doc.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(p => p.Kind == PaperKind.A4);
        if (a4 != null) doc.DefaultPageSettings.PaperSize = a4;

        var next = 0;
        var page = 0;
        doc.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Point;
            var w = e.PageBounds.Width * 72f / 100;
            var h = e.PageBounds.Height * 72f / 100;
            const float margin = 50;
            var bottom = h - margin - 14;
            var y = margin;
            page++;

            var right = new StringFormat { Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            var left = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            const float rightW = 90;

            while (next < rows.Count)
            {
                var r = rows[next];
                var lineH = r.Size * 1.7f;
                var before = y == margin ? 0 : r.Before;
                // 見出し行は次の行と同じページに置く
                var need = lineH + (r.KeepNext && next + 1 < rows.Count ? rows[next + 1].Size * 1.7f : 0);
                if (y + before + need > bottom) break; // 次ページへ

                y += before;
                using var font = new Font("Yu Gothic UI", r.Size, r.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
                if (r.Rule) g.DrawLine(Pens.Gray, margin, y + lineH - 1, w - margin, y + lineH - 1);
                var x = margin + r.Indent;
                g.DrawString(r.Left, font, Brushes.Black, new RectangleF(x, y, w - margin - rightW - x, lineH), left);
                if (r.Right.Length > 0)
                    g.DrawString(r.Right, font, Brushes.Black, new RectangleF(w - margin - rightW, y, rightW, lineH), right);
                y += lineH;
                next++;
            }

            using var small = new Font("Yu Gothic UI", 8, GraphicsUnit.Point);
            g.DrawString($"- {page} -", small, Brushes.Gray,
                new RectangleF(margin, h - margin, w - margin * 2, 14), new StringFormat { Alignment = StringAlignment.Center });
            e.HasMorePages = next < rows.Count;
        };
        doc.Print();
    }
}
