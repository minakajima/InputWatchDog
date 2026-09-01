namespace InputWatchDog.Models;

/// <summary>
/// イベントの発生元（どの監視系統で捕捉したか）。
/// フックとRawInputの両方に記録があるかどうかで、
/// 「デバイス/ドライバ起因」か「他プロセスのフック介入」かを切り分けるために使う。
/// </summary>
internal enum EventSource
{
    HookMouse,
    HookKeyboard,
    RawInputMouse,
    RawInputKeyboard,
    Health,
    Device,
    Power,
    Marking,
}

/// <summary>
/// 記録する1件のイベント。
/// 入力イベント・システム状態・デバイス着脱・電源イベントすべてを共通形式で扱う。
/// </summary>
internal sealed record InputEvent(DateTime Timestamp, EventSource Source, string Type, string Detail)
{
    public string ToCsvLine()
    {
        string safeDetail = Detail.Replace("\"", "\"\"");
        return $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff},{Source},{Type},\"{safeDetail}\"";
    }

    public static string CsvHeader => "Timestamp,Source,Type,Detail";
}
