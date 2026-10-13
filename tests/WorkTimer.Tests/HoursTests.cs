using WorkTimer;
using Xunit;

public class HoursTests
{
    static long Ms(int y, int mo, int d, int h = 9, int mi = 0) =>
        new DateTimeOffset(new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Local)).ToUnixTimeMilliseconds();

    const long Min = 60 * 1000;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(449, 0)]      // 7分29秒 -> 切り捨て
    [InlineData(450, 0.25)]   // 7分30秒 -> 切り上げ
    [InlineData(900, 0.25)]
    [InlineData(1349, 0.25)]  // 22分29秒
    [InlineData(1350, 0.5)]   // 22分30秒
    [InlineData(3600, 1)]
    public void RoundQuarter(int sec, double expected) => Assert.Equal(expected, Hours.RoundQuarter(sec));

    [Fact]
    public void SummarizeByDate_日付と作業名の合計秒を丸める()
    {
        var t = Ms(2026, 10, 13);
        var r = Hours.SummarizeByDate([
            new(1, "A", t, t + 5 * Min),
            new(2, "A", t + 60 * Min, t + 70 * Min),   // 合計15分 -> 0.25
            new(3, "B", t + 120 * Min, t + 140 * Min), // 20分 -> 0.25
            new(4, "C", t + 150 * Min, t + 155 * Min), // 5分 -> 0
        ]);
        var day = Assert.Single(r);
        Assert.Equal("2026-10-13", day.Date);
        Assert.Equal(0.25, day.Items.Single(i => i.Task == "A").Hours);
        Assert.Equal(0.25, day.Items.Single(i => i.Task == "B").Hours);
        Assert.Equal(0, day.Items.Single(i => i.Task == "C").Hours);
        Assert.Equal(0.5, day.Total);
    }

    [Fact]
    public void ToCsv_指定月だけを古い順に出力し特殊文字をエスケープする()
    {
        var csv = Hours.ToCsv([
            new(1, "A,\"x\"", Ms(2026, 10, 13), Ms(2026, 10, 13) + 30 * Min),
            new(2, "B", Ms(2026, 10, 2), Ms(2026, 10, 2) + 60 * Min),
            new(3, "Z", Ms(2026, 9, 1), Ms(2026, 9, 1) + 60 * Min),
        ], "2026-10");
        Assert.Equal("日付,作業名,工数(h)\r\n2026-10-02,B,1.00\r\n2026-10-13,\"A,\"\"x\"\"\",0.50\r\n", csv);
    }

    [Fact]
    public void MonthsOf_新しい順()
    {
        var m = Hours.MonthsOf([new(1, "A", Ms(2026, 9, 1), Ms(2026, 9, 1)), new(2, "A", Ms(2026, 10, 1), Ms(2026, 10, 1))]);
        Assert.Equal(["2026-10", "2026-09"], m);
    }
}
