using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DXDesk.Core;

/// <summary>
/// Viewer-dən gələn input mesajlarını host maşınının aktiv sessiyasına
/// <c>SendInput</c> ilə yeridir.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class InputInjector
{
    /// <summary>Bir input mesajını (bax <see cref="InputMessage"/>) tətbiq edir.</summary>
    public void Inject(ReadOnlySpan<byte> msg)
    {
        if (msg.Length < 1) return;
        var type = (InputType)msg[0];

        switch (type)
        {
            case InputType.MouseMove when msg.Length >= 5:
                var nx = (ushort)((msg[1] << 8) | msg[2]);
                var ny = (ushort)((msg[3] << 8) | msg[4]);
                SendMouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE, nx, ny, 0);
                break;

            case InputType.MouseDown when msg.Length >= 2:
                SendMouse(ButtonFlag((MouseButtonKind)msg[1], down: true), 0, 0, 0);
                break;

            case InputType.MouseUp when msg.Length >= 2:
                SendMouse(ButtonFlag((MouseButtonKind)msg[1], down: false), 0, 0, 0);
                break;

            case InputType.MouseWheel when msg.Length >= 3:
                var delta = (short)((msg[1] << 8) | msg[2]);
                SendMouse(MOUSEEVENTF_WHEEL, 0, 0, delta);
                break;

            case InputType.KeyDown when msg.Length >= 3:
                SendKey((ushort)((msg[1] << 8) | msg[2]), down: true);
                break;

            case InputType.KeyUp when msg.Length >= 3:
                SendKey((ushort)((msg[1] << 8) | msg[2]), down: false);
                break;
        }
    }

    private static uint ButtonFlag(MouseButtonKind button, bool down) => button switch
    {
        MouseButtonKind.Right => down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
        MouseButtonKind.Middle => down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
        _ => down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
    };

    private static void SendMouse(uint flags, ushort nx, ushort ny, int wheelDelta)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = nx,
                    dy = ny,
                    mouseData = (uint)wheelDelta,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void SendKey(ushort vk, bool down)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = down ? 0u : KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    // --- P/Invoke ---
    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public int type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx; public int dy; public uint mouseData;
        public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk; public ushort wScan; public uint dwFlags;
        public uint time; public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
