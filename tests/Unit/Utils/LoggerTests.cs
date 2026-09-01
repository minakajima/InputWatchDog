using InputWatchDog.Models;
using InputWatchDog.Utils;

namespace InputWatchDog.Tests.Unit.Utils;

/// <summary>
/// Logger は static クラスでプロセス全体の状態を共有するため、
/// このテストクラス内でのみ Initialize し、フィクスチャで1回だけ初期化する。
/// 他のテストクラスから Logger を触らないこと（状態共有のため）。
/// </summary>
public class LoggerTestFixture : IDisposable
{
    public string TempDirectory { get; }

    public LoggerTestFixture()
    {
        TempDirectory = Path.Combine(Path.GetTempPath(), "InputWatchDogTests_" + Guid.NewGuid());
        Logger.Initialize(TempDirectory);
    }

    public void Dispose()
    {
        Logger.Shutdown();
        if (Directory.Exists(TempDirectory))
        {
            Directory.Delete(TempDirectory, recursive: true);
        }
    }
}

[Collection(nameof(LoggerTestCollection))]
public class LoggerTests : IClassFixture<LoggerTestFixture>
{
    private readonly LoggerTestFixture _fixture;

    public LoggerTests(LoggerTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Log_した直後にGetEventsBetweenで取得できる()
    {
        DateTime now = DateTime.Now;
        var ev = new InputEvent(now, EventSource.Marking, "UserMarked", "テスト用イベント");

        Logger.Log(ev);
        List<InputEvent> result = Logger.GetEventsBetween(now.AddSeconds(-1), now.AddSeconds(1));

        Assert.Contains(result, e => ReferenceEquals(e, ev));
    }

    [Fact]
    public void GetEventsBetween_範囲外のイベントは含まれない()
    {
        DateTime baseTime = DateTime.Now;
        var inRange = new InputEvent(baseTime, EventSource.Marking, "InRange", "in");
        var outOfRange = new InputEvent(baseTime.AddSeconds(30), EventSource.Marking, "OutOfRange", "out");

        Logger.Log(inRange);
        Logger.Log(outOfRange);

        List<InputEvent> result = Logger.GetEventsBetween(baseTime.AddSeconds(-1), baseTime.AddSeconds(1));

        Assert.Contains(result, e => ReferenceEquals(e, inRange));
        Assert.DoesNotContain(result, e => ReferenceEquals(e, outOfRange));
    }

    [Fact]
    public void 保持期間より古いイベントはリングバッファから追い出される()
    {
        // リングバッファの保持期間(60秒)より明らかに古いイベントを記録する。
        DateTime oldTimestamp = DateTime.Now.AddMinutes(-5);
        var oldEvent = new InputEvent(oldTimestamp, EventSource.Marking, "TooOld", "old");
        Logger.Log(oldEvent);

        // Trim は Log() 呼び出し時に走るため、もう1件記録してトリムを発生させる。
        Logger.Log(new InputEvent(DateTime.Now, EventSource.Marking, "Trigger", "trigger"));

        List<InputEvent> result = Logger.GetEventsBetween(oldTimestamp.AddSeconds(-1), oldTimestamp.AddSeconds(1));

        Assert.DoesNotContain(result, e => ReferenceEquals(e, oldEvent));
    }

    [Fact]
    public async Task Log_した内容はCSVファイルへ非同期で書き込まれる()
    {
        var ev = new InputEvent(DateTime.Now, EventSource.HookKeyboard, "KeyDown", "VKey=0x99");
        Logger.Log(ev);

        string expectedPath = Path.Combine(_fixture.TempDirectory, "Logs", $"input_{DateTime.Now:yyyyMMdd}.csv");

        // バックグラウンド書き込みスレッドが処理するまで最大2秒ポーリングする。
        // 書き込み中はファイルが排他ロックされる瞬間があるため、IOExceptionは無視してリトライする。
        string? content = null;
        for (int i = 0; i < 20; i++)
        {
            try
            {
                if (File.Exists(expectedPath))
                {
                    using var stream = new FileStream(expectedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);
                    content = await reader.ReadToEndAsync();
                    if (content.Contains("VKey=0x99"))
                    {
                        break;
                    }
                }
            }
            catch (IOException)
            {
                // 書き込み中の排他競合。次のポーリングで再試行する。
            }

            await Task.Delay(100);
        }

        Assert.NotNull(content);
        Assert.Contains(InputEvent.CsvHeader, content);
        Assert.Contains("VKey=0x99", content);
    }
}

[CollectionDefinition(nameof(LoggerTestCollection), DisableParallelization = true)]
public class LoggerTestCollection
{
}
