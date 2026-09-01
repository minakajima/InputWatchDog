using InputWatchDog.Models;
using InputWatchDog.Monitors;
using InputWatchDog.Native;
using InputWatchDog.Utils;
using Microsoft.Win32;

namespace InputWatchDog.UI;

/// <summary>
/// 常駐監視のメインウィンドウ（非表示）。
/// トレイアイコンから終了・フォルダを開く・手動マーキングを操作できる。
/// </summary>
public sealed class MainForm : Form
{
    private const int MarkHotkeyId = 1;

    private readonly NotifyIcon _notifyIcon;
    private readonly HookMonitor _hookMonitor = new();
    private readonly RawInputMonitor _rawInputMonitor;
    private readonly SystemHealthMonitor _healthMonitor = new();
    private readonly System.Windows.Forms.Timer _healthTimer = new() { Interval = 2000 };

    public MainForm()
    {
        // 監視専用の非表示ウィンドウのため、画面には出さない。
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Opacity = 0;
        Load += (_, _) => Hide();

        _rawInputMonitor = new RawInputMonitor(Handle);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "InputWatchDog - 入力監視中",
            Visible = true,
            ContextMenuStrip = BuildContextMenu(),
        };
        _notifyIcon.DoubleClick += (_, _) => OpenLogFolder();

        _healthTimer.Tick += (_, _) => _healthMonitor.Sample();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("今すぐマーキング (Ctrl+Alt+M)", null, (_, _) => TriggerMark());
        menu.Items.Add("ログフォルダを開く", null, (_, _) => OpenLogFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => Close());
        return menu;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        string baseDir = Path.Combine(AppContext.BaseDirectory, "Data");
        Logger.Initialize(baseDir);

        try
        {
            _hookMonitor.Start();
            _rawInputMonitor.Register();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "InputWatchDog", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        NativeMethods.RegisterHotKey(Handle, MarkHotkeyId, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, (uint)Keys.M);
        _healthTimer.Start();
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case NativeMethods.WM_INPUT:
                _rawInputMonitor.ProcessRawInput(m.LParam);
                break;

            case NativeMethods.WM_DEVICECHANGE:
                HandleDeviceChange(m.WParam.ToInt32());
                break;

            case NativeMethods.WM_HOTKEY:
                if (m.WParam.ToInt32() == MarkHotkeyId)
                {
                    TriggerMark();
                }
                break;
        }

        base.WndProc(ref m);
    }

    private static void HandleDeviceChange(int eventType)
    {
        string type = eventType switch
        {
            NativeMethods.DBT_DEVICEARRIVAL => "DeviceArrival",
            NativeMethods.DBT_DEVICEREMOVECOMPLETE => "DeviceRemoved",
            _ => $"Other(0x{eventType:X4})",
        };

        // 種別が汎用イベントの場合はノイズになるため、着脱のみ記録する。
        if (eventType is NativeMethods.DBT_DEVICEARRIVAL or NativeMethods.DBT_DEVICEREMOVECOMPLETE)
        {
            Logger.Log(new InputEvent(DateTime.Now, EventSource.Device, type, "USBデバイスの着脱を検出"));
        }
    }

    private static void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        Logger.Log(new InputEvent(DateTime.Now, EventSource.Power, e.Mode.ToString(), "電源状態の遷移を検出"));
    }

    private void TriggerMark()
    {
        DateTime markTime = DateTime.Now;
        Logger.Log(new InputEvent(markTime, EventSource.Marking, "UserMarked", "ユーザーが手動でマーキングしました"));
        _notifyIcon.ShowBalloonTip(1500, "InputWatchDog", "マーキングしました。数秒後にレポートを作成します。", ToolTipIcon.Info);

        _ = MarkAnalyzer.CreateReportAsync(markTime).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                return;
            }

            BeginInvoke(() => _notifyIcon.ShowBalloonTip(2000, "InputWatchDog", "マーキングレポートを作成しました。", ToolTipIcon.Info));
        });
    }

    private void OpenLogFolder()
    {
        if (Directory.Exists(Logger.LogDirectory))
        {
            System.Diagnostics.Process.Start("explorer.exe", Path.GetDirectoryName(Logger.LogDirectory)!);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        NativeMethods.UnregisterHotKey(Handle, MarkHotkeyId);
        _healthTimer.Stop();
        _hookMonitor.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Logger.Shutdown();
        base.OnFormClosing(e);
    }
}
