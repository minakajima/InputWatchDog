using System.Text;
using InputWatchDog.Models;

namespace InputWatchDog.Utils;

/// <summary>
/// 手動マーキング（「今、入力が効かなかった」の申告）時に、
/// 前後のイベントを突き合わせて疑わしい原因カテゴリを機械的に分類する。
///
/// ツール単体では「入力が無かったこと」自体は検知できないため、
/// あくまで人間が気づいた瞬間を起点に、周辺の状況証拠を整理する補助機能。
/// </summary>
internal static class MarkAnalyzer
{
    private static readonly TimeSpan BeforeWindow = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan AfterWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// マーキング時刻を受け取り、しばらく待ってから前後のイベントを集約し、
    /// レポートファイルを1件生成する。
    /// </summary>
    public static async Task CreateReportAsync(DateTime markTime)
    {
        // Afterウィンドウ分のイベントが記録されるまで待機
        await Task.Delay(AfterWindow + TimeSpan.FromSeconds(1));

        List<InputEvent> events = Logger.GetEventsBetween(markTime - BeforeWindow, markTime + AfterWindow);
        List<string> suspects = Classify(events);

        string path = Path.Combine(Logger.MarkReportDirectory, $"mark_{markTime:yyyyMMdd_HHmmss}.txt");
        var sb = new StringBuilder();
        sb.AppendLine($"# マーキングレポート {markTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("## 疑わしい原因カテゴリ（自動分類・目安）");
        if (suspects.Count == 0)
        {
            sb.AppendLine("- 該当パターンなし（手動でログを確認してください）");
        }
        else
        {
            foreach (string s in suspects)
            {
                sb.AppendLine($"- {s}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"## 前後イベント一覧 (マーク時刻の-{BeforeWindow.TotalSeconds:F0}秒 〜 +{AfterWindow.TotalSeconds:F0}秒)");
        sb.AppendLine(InputEvent.CsvHeader);
        foreach (InputEvent ev in events)
        {
            sb.AppendLine(ev.ToCsvLine());
        }

        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);

        Logger.Log(new InputEvent(DateTime.Now, EventSource.Marking, "ReportCreated", path));
    }

    /// <summary>
    /// イベント一覧から疑わしい原因カテゴリを判定する。
    /// テストから直接検証できるよう internal で公開している。
    /// </summary>
    internal static List<string> Classify(List<InputEvent> events)
    {
        var suspects = new List<string>();

        bool hasHookEvent = events.Any(e => e.Source is EventSource.HookMouse or EventSource.HookKeyboard);
        bool hasRawInputEvent = events.Any(e => e.Source is EventSource.RawInputMouse or EventSource.RawInputKeyboard);
        bool hasForegroundHang = events.Any(e => e.Type == "ForegroundHang");
        bool hasHighCpu = events.Any(e => e.Type == "HighCpuLoad");
        bool hasDeviceChange = events.Any(e => e.Source == EventSource.Device);
        bool hasPowerEvent = events.Any(e => e.Source == EventSource.Power);

        if (!hasHookEvent && !hasRawInputEvent)
        {
            suspects.Add("デバイス/ドライバ疑い：マーク前後で入力イベント自体がOSに到達していない（電池切れ・無線干渉・USBセレクティブサスペンド等を確認）");
        }
        else if (hasHookEvent != hasRawInputEvent)
        {
            suspects.Add("他プロセスのフック介入疑い：フックとRawInputで検出結果に差異あり（IME・セキュリティソフト・リモート操作ツール等を確認）");
        }

        if (hasForegroundHang)
        {
            suspects.Add("アプリケーションハング疑い：フォアグラウンドウィンドウが応答なし状態を記録");
        }

        if (hasDeviceChange)
        {
            suspects.Add("USB接続不良疑い：デバイスの着脱イベントを記録");
        }

        if (hasHighCpu)
        {
            suspects.Add("システム高負荷疑い：CPU使用率が90%以上を記録（メッセージポンプの遅延要因）");
        }

        if (hasPowerEvent)
        {
            suspects.Add("省電力/サスペンド疑い：電源状態の遷移イベントを記録");
        }

        return suspects;
    }
}
