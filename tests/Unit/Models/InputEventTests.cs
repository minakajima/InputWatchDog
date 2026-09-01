using InputWatchDog.Models;

namespace InputWatchDog.Tests.Unit.Models;

public class InputEventTests
{
    [Fact]
    public void ToCsvLine_通常の値をカンマ区切りで出力する()
    {
        var timestamp = new DateTime(2026, 9, 1, 12, 34, 56, 789);
        var ev = new InputEvent(timestamp, EventSource.HookKeyboard, "KeyDown", "VKey=0x41");

        string line = ev.ToCsvLine();

        Assert.Equal("2026-09-01 12:34:56.789,HookKeyboard,KeyDown,\"VKey=0x41\"", line);
    }

    [Fact]
    public void ToCsvLine_Detail内のダブルクォートはCSVエスケープされる()
    {
        var ev = new InputEvent(DateTime.Now, EventSource.Health, "SystemLoad", "Title=\"メモ帳\"");

        string line = ev.ToCsvLine();

        Assert.Contains("\"Title=\"\"メモ帳\"\"\"", line);
    }

    [Fact]
    public void CsvHeader_想定した列名を返す()
    {
        Assert.Equal("Timestamp,Source,Type,Detail", InputEvent.CsvHeader);
    }
}
