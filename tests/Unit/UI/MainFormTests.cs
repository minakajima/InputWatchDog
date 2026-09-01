using InputWatchDog.Tests.Unit.Utils;
using InputWatchDog.UI;
using Xunit;

namespace InputWatchDog.Tests.Unit.UI;

/// <summary>
/// MainForm のライフサイクル回帰テスト。
///
/// 過去に「コンストラクタで Handle プロパティへアクセスしたことで、
/// フィールド代入前に OnHandleCreated が同期的に呼ばれ NullReferenceException が
/// 発生する」不具合があったため、実際にフォームを生成しウィンドウハンドルを
/// 確定させるところまでを再現して防止する。
///
/// 注意（このテストの性質について）:
/// - WinForms のフォームは通常STAスレッド上での利用が前提のため、[StaFact] でSTAスレッド実行する。
/// - Handle 確定に伴い、実際に WH_MOUSE_LL / WH_KEYBOARD_LL のグローバルフックと
///   グローバルホットキー(Ctrl+Alt+M)が一時的に実際のOSへ登録される（純粋なユニットテストではなく
///   軽量な統合テストに近い）。フックはイベントを素通りさせるだけで実害はないが、
///   テスト終了時に Close() で確実に解除する。
/// - Logger は静的クラスで状態を共有するため、Logger を直接操作する LoggerTests と
///   同じ Collection（非並列実行）に所属させている。
/// </summary>
[Collection(nameof(LoggerTestCollection))]
public class MainFormTests
{
    [StaFact]
    public void MainForm生成後にHandleへアクセスしても例外が発生しない()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "InputWatchDogTests_MainForm_" + Guid.NewGuid());

        try
        {
            using var form = new MainForm(tempDir);

            // Handle プロパティへのアクセスでウィンドウハンドルが生成され、
            // OnHandleCreated が同期的に呼び出される。
            // 過去の不具合ではここで NullReferenceException が発生していた。
            Exception? ex = Record.Exception(() =>
            {
                nint handle = form.Handle;
                Assert.NotEqual(nint.Zero, handle);
            });

            Assert.Null(ex);

            // Close() で OnFormClosing を経由し、フック・ホットキー・Loggerの後始末を行う。
            form.Close();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
