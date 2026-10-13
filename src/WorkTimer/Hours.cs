using System.Globalization;
using System.Text;

namespace WorkTimer;

public record Session(long Id, string Task, long Start, long End);

public record DayItem(string Task, int Sec, double Hours);

public record DaySummary(string Date, List<DayItem> Items, double Total);

// 工数計算の純粋関数
public static class Hours
{
    const int QuarterSec = 15 * 60;

    // 秒 -> 0.25h単位(四捨五入)の時間(h)。7分30秒以上で切り上げ、未満で切り捨て
    public static double RoundQuarter(double sec) =>
        Math.Round(sec / QuarterSec, MidpointRounding.AwayFromZero) * QuarterSec / 3600;

    public static DateTime ToLocal(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

    // ローカル日付 YYYY-MM-DD
    public static string DateKey(long ms) => ToLocal(ms).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // 日付(新しい順) -> 作業名別の合計秒を丸めた工数と日合計
    public static List<DaySummary> SummarizeByDate(IEnumerable<Session> sessions)
    {
        var byDate = new Dictionary<string, Dictionary<string, int>>();
        foreach (var s in sessions)
        {
            var date = DateKey(s.Start);
            var sec = Math.Max(0, (int)Math.Round((s.End - s.Start) / 1000.0, MidpointRounding.AwayFromZero));
            if (!byDate.TryGetValue(date, out var tasks)) byDate[date] = tasks = new();
            tasks[s.Task] = tasks.GetValueOrDefault(s.Task) + sec;
        }
        return byDate
            .OrderByDescending(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv =>
            {
                var items = kv.Value.Select(t => new DayItem(t.Key, t.Value, RoundQuarter(t.Value))).ToList();
                return new DaySummary(kv.Key, items, items.Sum(i => i.Hours));
            })
            .ToList();
    }

    // 記録のある年月(YYYY-MM)を新しい順に
    public static List<string> MonthsOf(IEnumerable<Session> sessions) =>
        sessions.Select(s => DateKey(s.Start)[..7]).Distinct().OrderByDescending(m => m, StringComparer.Ordinal).ToList();

    static string CsvField(string v) =>
        v.IndexOfAny(['"', ',', '\r', '\n']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;

    // 指定年月(YYYY-MM)の日付×作業名の工数(0.25h単位)をCSV文字列にする(日付は古い順)。BOMは書き込み側で付ける
    public static string ToCsv(IEnumerable<Session> sessions, string month)
    {
        var sb = new StringBuilder("日付,作業名,工数(h)\r\n");
        foreach (var day in SummarizeByDate(sessions).Where(d => d.Date.StartsWith(month, StringComparison.Ordinal)).Reverse())
            foreach (var item in day.Items)
                sb.Append($"{day.Date},{CsvField(item.Task)},{item.Hours.ToString("0.00", CultureInfo.InvariantCulture)}\r\n");
        return sb.ToString();
    }
}
