using InputWatchDog.Models;
using InputWatchDog.Utils;

namespace InputWatchDog.Tests.Unit.Utils;

/// <summary>
/// MarkAnalyzer.Classify の分類ロジックを検証する。
/// README記載の「原因分類の目安」表と対応させたテストケースにしている。
/// </summary>
public class MarkAnalyzerTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 10, 0, 0);

    private static InputEvent MakeEvent(EventSource source, string type, string detail = "detail")
        => new(Now, source, type, detail);

    [Fact]
    public void フックもRawInputも無い場合はデバイスドライバ疑いになる()
    {
        var events = new List<InputEvent>
        {
            MakeEvent(EventSource.Health, "SystemLoad"),
        };

        List<string> result = MarkAnalyzer.Classify(events);

        Assert.Contains(result, s => s.Contains("デバイス/ドライバ疑い"));
    }

    [Fact]
    public void フックのみ存在しRawInputが無い場合はフック介入疑いになる()
    {
        var events = new List<InputEvent>
        {
            MakeEvent(EventSource.HookKeyboard, "KeyDown"),
        };

        List<string> result = MarkAnalyzer.Classify(events);

        Assert.Contains(result, s => s.Contains("他プロセスのフック介入疑い"));
        Assert.DoesNotContain(result, s => s.Contains("デバイス/ドライバ疑い"));
    }

    [Fact]
    public void RawInputのみ存在しフックが無い場合はフック介入疑いになる()
    {
        var events = new List<InputEvent>
        {
            MakeEvent(EventSource.RawInputMouse, "ButtonEvent"),
        };

        List<string> result = MarkAnalyzer.Classify(events);

        Assert.Contains(result, s => s.Contains("他プロセスのフック介入疑い"));
    }

    [Fact]
    public void フックとRawInput両方存在する場合はデバイスやフック介入の疑いを出さない()
    {
        var events = new List<InputEvent>
        {
            MakeEvent(EventSource.HookKeyboard, "KeyDown"),
            MakeEvent(EventSource.RawInputKeyboard, "KeyDown"),
        };

        List<string> result = MarkAnalyzer.Classify(events);

        Assert.DoesNotContain(result, s => s.Contains("デバイス/ドライバ疑い"));
        Assert.DoesNotContain(result, s => s.Contains("他プロセスのフック介入疑い"));
    }

    [Fact]
    public void イベントが空の場合はデバイスドライバ疑いのみになる()
    {
        List<string> result = MarkAnalyzer.Classify(new List<InputEvent>());

        Assert.Single(result);
        Assert.Contains(result, s => s.Contains("デバイス/ドライバ疑い"));
    }

    [Fact]
    public void ForegroundHangイベントがあればアプリケーションハング疑いになる()
    {
        var events = new List<InputEvent>
        {
            MakeEvent(EventSource.HookKeyboard, "KeyDown"),
            MakeEvent(EventSource.RawInputKeyboard, "KeyDown"),
            MakeEvent(EventSource.Health, "ForegroundHang"),
        };

        List<string> result = MarkAnalyzer.Classify(events);

        Assert.Contains(result, s => s.Contains("アプリケーションハング疑い"));
    }

    [Fact]
    public void Deviceイベントがあれば_USB接続不良疑いになる()
    {
        var events = new List<InputEvent>
        {
            MakeEvent(EventSource.HookKeyboard, "KeyDown"),
            MakeEvent(EventSource.RawInputKeyboard, "KeyDown"),
            MakeEvent(EventSource.Device, "DeviceRemoved"),
        };

        List<string> result = MarkAnalyzer.Classify(events);

        Assert.Contains(result, s => s.Contains("USB接続不良疑い"));
    }

    [Fact]
    public void HighCpuLoadイベントがあればシステム高負荷疑いになる()
    {
        var events = new List<InputEvent>
        {
            MakeEvent(EventSource.HookKeyboard, "KeyDown"),
            MakeEvent(EventSource.RawInputKeyboard, "KeyDown"),
            MakeEvent(EventSource.Health, "HighCpuLoad"),
        };

        List<string> result = MarkAnalyzer.Classify(events);

        Assert.Contains(result, s => s.Contains("システム高負荷疑い"));
    }

    [Fact]
    public void Powerイベントがあれば省電力サスペンド疑いになる()
    {
        var events = new List<InputEvent>
        {
            MakeEvent(EventSource.HookKeyboard, "KeyDown"),
            MakeEvent(EventSource.RawInputKeyboard, "KeyDown"),
            MakeEvent(EventSource.Power, "Suspend"),
        };

        List<string> result = MarkAnalyzer.Classify(events);

        Assert.Contains(result, s => s.Contains("省電力/サスペンド疑い"));
    }

    [Fact]
    public void 複数の疑いが同時に成立する場合はすべて列挙される()
    {
        var events = new List<InputEvent>
        {
            // フックのみ = フック介入疑い
            MakeEvent(EventSource.HookKeyboard, "KeyDown"),
            MakeEvent(EventSource.Health, "ForegroundHang"),
            MakeEvent(EventSource.Device, "DeviceArrival"),
            MakeEvent(EventSource.Health, "HighCpuLoad"),
            MakeEvent(EventSource.Power, "Resume"),
        };

        List<string> result = MarkAnalyzer.Classify(events);

        Assert.Equal(5, result.Count);
    }
}
