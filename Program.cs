using System.Diagnostics.CodeAnalysis;
using InputWatchDog.UI;

namespace InputWatchDog;

[ExcludeFromCodeCoverage]
internal static class Program
{
    /// <summary>
    /// アプリケーションのエントリポイント。
    /// トレイ常駐で入力監視を行うため、通常のウィンドウは表示しない。
    /// </summary>
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
