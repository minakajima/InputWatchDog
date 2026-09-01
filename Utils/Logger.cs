using System.Collections.Concurrent;
using InputWatchDog.Models;

namespace InputWatchDog.Utils;

/// <summary>
/// イベントロガー。
///
/// 低レベルフックのコールバック内で直接ファイルI/Oを行うと、
/// OSの規定時間(既定300ms程度)内に処理が戻らずフックが強制解除されるおそれがあるため、
/// コールバックからは Log() でキューに積むだけにし、
/// 実際のファイル書き込みはバックグラウンドスレッドで行う。
///
/// また、手動マーキング時に「発生前後のイベント」を取り出せるように、
/// 直近一定時間分のイベントをメモリ上のリングバッファにも保持する。
/// </summary>
internal static class Logger
{
    private static readonly BlockingCollection<InputEvent> _writeQueue = new();
    private static readonly ConcurrentQueue<InputEvent> _ringBuffer = new();
    private static readonly TimeSpan _ringBufferRetention = TimeSpan.FromSeconds(60);

    private static string _logDirectory = string.Empty;
    private static string? _currentLogDate;
    private static StreamWriter? _writer;
    private static Thread? _writerThread;

    public static string LogDirectory => _logDirectory;
    public static string MarkReportDirectory { get; private set; } = string.Empty;

    public static void Initialize(string baseDirectory)
    {
        _logDirectory = Path.Combine(baseDirectory, "Logs");
        MarkReportDirectory = Path.Combine(baseDirectory, "MarkReports");
        Directory.CreateDirectory(_logDirectory);
        Directory.CreateDirectory(MarkReportDirectory);

        _writerThread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "InputWatchDog-LogWriter",
        };
        _writerThread.Start();
    }

    /// <summary>
    /// イベントを記録する。呼び出しは軽量（キューへの追加のみ）で、
    /// フックコールバックやRawInput処理から直接呼んでよい。
    /// </summary>
    public static void Log(InputEvent ev)
    {
        _ringBuffer.Enqueue(ev);
        TrimRingBuffer();
        _writeQueue.Add(ev);
    }

    /// <summary>
    /// リングバッファから、指定範囲の時刻のイベントを取得する。
    /// マーキング時のレポート生成に使う。
    /// </summary>
    public static List<InputEvent> GetEventsBetween(DateTime from, DateTime to)
    {
        return _ringBuffer
            .Where(e => e.Timestamp >= from && e.Timestamp <= to)
            .OrderBy(e => e.Timestamp)
            .ToList();
    }

    private static void TrimRingBuffer()
    {
        DateTime threshold = DateTime.Now - _ringBufferRetention;
        while (_ringBuffer.TryPeek(out InputEvent? oldest) && oldest.Timestamp < threshold)
        {
            _ringBuffer.TryDequeue(out _);
        }
    }

    private static void WriterLoop()
    {
        foreach (InputEvent ev in _writeQueue.GetConsumingEnumerable())
        {
            try
            {
                EnsureWriterForDate(ev.Timestamp);
                _writer?.WriteLine(ev.ToCsvLine());
                _writer?.Flush();
            }
            catch
            {
                // ログ書き込み失敗は監視継続を優先し、握りつぶす。
                // （ディスク一時エラー等でツール自体を落とさないため）
            }
        }
    }

    private static void EnsureWriterForDate(DateTime timestamp)
    {
        string dateKey = timestamp.ToString("yyyyMMdd");
        if (dateKey == _currentLogDate && _writer is not null)
        {
            return;
        }

        _writer?.Dispose();
        _currentLogDate = dateKey;
        string path = Path.Combine(_logDirectory, $"input_{dateKey}.csv");
        bool isNewFile = !File.Exists(path);

        _writer = new StreamWriter(path, append: true, System.Text.Encoding.UTF8);
        if (isNewFile)
        {
            _writer.WriteLine(InputEvent.CsvHeader);
        }
    }

    public static void Shutdown()
    {
        _writeQueue.CompleteAdding();
        _writerThread?.Join(TimeSpan.FromSeconds(2));
        _writer?.Flush();
        _writer?.Dispose();
    }
}
