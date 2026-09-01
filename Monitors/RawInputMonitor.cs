using System.Runtime.InteropServices;
using InputWatchDog.Models;
using InputWatchDog.Native;
using InputWatchDog.Utils;

namespace InputWatchDog.Monitors;

/// <summary>
/// Raw Input API 経由でマウス・キーボードの入力を記録する。
///
/// フック(HookMonitor)とは別経路でOSからイベントを受け取るため、
/// 「フックには来ているがRawInputには来ていない」等の差異を見ることで、
/// 他プロセスによるフック介入（イベントの消費・改変）を疑う手がかりになる。
/// </summary>
internal sealed class RawInputMonitor
{
    private readonly nint _targetWindowHandle;

    public RawInputMonitor(nint targetWindowHandle)
    {
        _targetWindowHandle = targetWindowHandle;
    }

    public void Register()
    {
        var devices = new NativeMethods.RAWINPUTDEVICE[]
        {
            new()
            {
                usUsagePage = 0x01,
                usUsage = 0x02, // Mouse
                dwFlags = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget = _targetWindowHandle,
            },
            new()
            {
                usUsagePage = 0x01,
                usUsage = 0x06, // Keyboard
                dwFlags = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget = _targetWindowHandle,
            },
        };

        bool ok = NativeMethods.RegisterRawInputDevices(devices, (uint)devices.Length,
            (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());

        if (!ok)
        {
            throw new InvalidOperationException("Raw Input デバイスの登録に失敗しました。");
        }
    }

    /// <summary>
    /// WM_INPUT メッセージを受け取った際にフォームの WndProc から呼び出す。
    /// </summary>
    public void ProcessRawInput(nint lParam)
    {
        uint size = 0;
        NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, nint.Zero, ref size,
            (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>());

        if (size == 0)
        {
            return;
        }

        nint buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            uint headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
            uint read = NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, buffer, ref size, headerSize);
            if (read != size)
            {
                return;
            }

            var header = Marshal.PtrToStructure<NativeMethods.RAWINPUTHEADER>(buffer);
            nint dataPtr = nint.Add(buffer, (int)headerSize);

            if (header.dwType == NativeMethods.RIM_TYPEMOUSE)
            {
                var mouse = Marshal.PtrToStructure<NativeMethods.RAWMOUSE>(dataPtr);
                if (mouse.usButtonFlags != 0)
                {
                    Logger.Log(new InputEvent(DateTime.Now, EventSource.RawInputMouse, "ButtonEvent",
                        $"ButtonFlags=0x{mouse.usButtonFlags:X4} dX={mouse.lLastX} dY={mouse.lLastY}"));
                }
            }
            else if (header.dwType == NativeMethods.RIM_TYPEKEYBOARD)
            {
                var kb = Marshal.PtrToStructure<NativeMethods.RAWKEYBOARD>(dataPtr);
                // Flags の bit0 が立っていればキーアップ
                string type = (kb.Flags & 0x01) == 0x01 ? "KeyUp" : "KeyDown";
                Logger.Log(new InputEvent(DateTime.Now, EventSource.RawInputKeyboard, type, $"VKey=0x{kb.VKey:X2}"));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
