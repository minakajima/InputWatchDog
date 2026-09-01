using System.Text;
using InputWatchDog.Models;
using InputWatchDog.Native;
using InputWatchDog.Utils;

namespace InputWatchDog.Monitors;

/// <summary>
/// CPU使用率・メモリ状況・フォアグラウンドウィンドウの応答性を定期的にサンプリングする。
///
/// 「入力イベントは来ているのに反映されない」ケースは、
/// アプリ側のUIスレッドがハングしている可能性が高いため、
/// SendMessageTimeout(SMTO_ABORTIFHUNG) でフォアグラウンドウィンドウの応答性を確認する。
/// </summary>
internal sealed class SystemHealthMonitor
{
    private NativeMethods.FILETIME_TIMES _prevIdle;
    private NativeMethods.FILETIME_TIMES _prevKernel;
    private NativeMethods.FILETIME_TIMES _prevUser;
    private bool _hasPrevSample;

    public void Sample()
    {
        SampleCpuAndMemory();
        SampleForegroundWindowResponsiveness();
    }

    private void SampleCpuAndMemory()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return;
        }

        if (_hasPrevSample)
        {
            ulong idleDiff = ToUInt64(idle) - ToUInt64(_prevIdle);
            ulong kernelDiff = ToUInt64(kernel) - ToUInt64(_prevKernel);
            ulong userDiff = ToUInt64(user) - ToUInt64(_prevUser);
            ulong totalDiff = kernelDiff + userDiff; // kernelTime にidleが含まれる仕様のため busy = total - idle
            double cpuUsagePercent = totalDiff == 0 ? 0 : (1.0 - (double)idleDiff / totalDiff) * 100.0;

            var mem = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
            NativeMethods.GlobalMemoryStatusEx(ref mem);

            Logger.Log(new InputEvent(DateTime.Now, EventSource.Health, "SystemLoad",
                $"CPU={cpuUsagePercent:F1}% MemoryLoad={mem.dwMemoryLoad}% AvailPhysMB={mem.ullAvailPhys / 1024 / 1024}"));

            // 高負荷時は入力メッセージポンプの遅延要因になり得るため、しきい値超過を別途マーキング
            if (cpuUsagePercent >= 90.0)
            {
                Logger.Log(new InputEvent(DateTime.Now, EventSource.Health, "HighCpuLoad", $"CPU={cpuUsagePercent:F1}%"));
            }
        }

        _prevIdle = idle;
        _prevKernel = kernel;
        _prevUser = user;
        _hasPrevSample = true;
    }

    private static ulong ToUInt64(NativeMethods.FILETIME_TIMES t)
        => ((ulong)t.dwHighDateTime << 32) | t.dwLowDateTime;

    private static void SampleForegroundWindowResponsiveness()
    {
        nint hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0)
        {
            return;
        }

        var sb = new StringBuilder(256);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        string title = sb.ToString();

        // WM_NULL(0) を200ms以内に応答できるか確認する。応答不可＝ウィンドウハング。
        nint result = NativeMethods.SendMessageTimeout(hwnd, 0, 0, 0,
            NativeMethods.SMTO_ABORTIFHUNG, 200, out _);

        if (result == 0)
        {
            Logger.Log(new InputEvent(DateTime.Now, EventSource.Health, "ForegroundHang", $"Title=\"{title}\""));
        }
    }
}
