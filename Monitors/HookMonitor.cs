using InputWatchDog.Models;
using InputWatchDog.Native;
using InputWatchDog.Utils;

namespace InputWatchDog.Monitors;

/// <summary>
/// WH_MOUSE_LL / WH_KEYBOARD_LL によるグローバルフックで、
/// OSに実際に届いたマウス・キーボードイベントを記録する。
///
/// 注意: フックプロシージャは既定で約300ms以内に処理を戻さないと
/// OSによって自動的にフックが解除されてしまうため、ここでは
/// Logger.Log() でキューに積むだけの軽量処理に留めている。
/// </summary>
internal sealed class HookMonitor : IDisposable
{
    // デリゲートはGCされないようフィールドに保持し続ける必要がある。
    private readonly NativeMethods.LowLevelHookProc _keyboardProc;
    private readonly NativeMethods.LowLevelHookProc _mouseProc;
    private nint _keyboardHookHandle;
    private nint _mouseHookHandle;

    public HookMonitor()
    {
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
    }

    public void Start()
    {
        nint hModule = NativeMethods.GetModuleHandle(null);
        _keyboardHookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, hModule, 0);
        _mouseHookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, hModule, 0);

        if (_keyboardHookHandle == 0 || _mouseHookHandle == 0)
        {
            throw new InvalidOperationException("低レベルフックの設定に失敗しました。管理者権限で実行しているか確認してください。");
        }
    }

    private nint KeyboardHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            string type = wParam.ToInt32() switch
            {
                NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN => "KeyDown",
                NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP => "KeyUp",
                _ => "Unknown",
            };
            Logger.Log(new InputEvent(DateTime.Now, EventSource.HookKeyboard, type, $"VKey=0x{data.vkCode:X2} Scan=0x{data.scanCode:X2}"));
        }

        return NativeMethods.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private nint MouseHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            string? type = wParam.ToInt32() switch
            {
                NativeMethods.WM_LBUTTONDOWN => "LButtonDown",
                NativeMethods.WM_LBUTTONUP => "LButtonUp",
                NativeMethods.WM_RBUTTONDOWN => "RButtonDown",
                NativeMethods.WM_RBUTTONUP => "RButtonUp",
                NativeMethods.WM_MBUTTONDOWN => "MButtonDown",
                NativeMethods.WM_MBUTTONUP => "MButtonUp",
                NativeMethods.WM_MOUSEWHEEL => "Wheel",
                NativeMethods.WM_MOUSEMOVE => null, // 移動はログが膨大になるため記録しない
                _ => "Unknown",
            };

            if (type is not null)
            {
                Logger.Log(new InputEvent(DateTime.Now, EventSource.HookMouse, type, $"Pos=({data.pt.x},{data.pt.y})"));
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_keyboardHookHandle != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = 0;
        }

        if (_mouseHookHandle != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = 0;
        }
    }
}
