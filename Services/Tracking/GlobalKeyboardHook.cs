using System.Runtime.InteropServices;
using System.Text;

namespace ScreenRecorderApp.Services.Tracking;

/// <summary>
/// Installs a system-wide WH_KEYBOARD_LL hook on a dedicated thread (low-level keyboard hooks only
/// receive callbacks on the thread that installed them, and only while that thread is pumping messages)
/// and raises <see cref="KeyPressed"/> with a friendly display string for every key-down, combining live
/// modifier state (Ctrl/Alt/Shift/Win) into strings like "Ctrl + C".
/// </summary>
public sealed class GlobalKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out] StringBuilder pwszBuff, int cchBuff, uint wFlags);

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);

    private const uint WM_QUIT = 0x0012;

    private Thread? _thread;
    private nint _hookHandle;
    private uint _threadId;
    // Kept alive for the lifetime of the hook: SetWindowsHookEx only stores a raw function pointer, so if
    // this delegate were garbage-collected the CLR could free the native thunk out from under Windows.
    private LowLevelKeyboardProc? _proc;

    public event Action<string>? KeyPressed;

    public void Start()
    {
        if (_thread is not null) return;

        _thread = new Thread(RunMessageLoop) { IsBackground = true, Name = "GlobalKeyboardHook" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void RunMessageLoop()
    {
        _threadId = (uint)Environment.CurrentManagedThreadId;
        _proc = HookCallback;

        using var curModule = System.Diagnostics.Process.GetCurrentProcess().MainModule;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule?.ModuleName), 0);

        // A message pump is required for the hook procedure above to ever be invoked.
        while (GetMessage(out var msg, nint.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hookHandle != nint.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = nint.Zero;
        }
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            try
            {
                int vkCode = Marshal.ReadInt32(lParam);
                var display = DescribeKey(vkCode);
                if (display is not null) KeyPressed?.Invoke(display);
            }
            catch
            {
                // Best effort: never let overlay text generation break the hook chain.
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool IsDown(int vk) => (GetKeyState(vk) & 0x8000) != 0;

    private static string? DescribeKey(int vkCode)
    {
        // Modifier keys are folded into the combo string for the *other* key instead of being shown on
        // their own, so a plain Ctrl tap doesn't flash an empty "Ctrl + " combo.
        if (vkCode is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LWIN or VK_RWIN
            or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5) return null;

        var keyName = KeyName(vkCode);
        if (keyName is null) return null;

        var parts = new List<string>(4);
        if (IsDown(VK_CONTROL)) parts.Add("Ctrl");
        if (IsDown(VK_MENU)) parts.Add("Alt");
        if (IsDown(VK_LWIN) || IsDown(VK_RWIN)) parts.Add("Win");
        if (IsDown(VK_SHIFT) && keyName.Length > 1) parts.Add("Shift"); // single chars already reflect shift via ToUnicode

        parts.Add(keyName);
        return string.Join(" + ", parts);
    }

    private static string? KeyName(int vkCode)
    {
        switch (vkCode)
        {
            case 0x08: return "Backspace";
            case 0x09: return "Tab";
            case 0x0D: return "Enter";
            case 0x1B: return "Esc";
            case 0x20: return "Space";
            case 0x21: return "Page Up";
            case 0x22: return "Page Down";
            case 0x23: return "End";
            case 0x24: return "Home";
            case 0x25: return "←";
            case 0x26: return "↑";
            case 0x27: return "→";
            case 0x28: return "↓";
            case 0x2E: return "Delete";
            case >= 0x70 and <= 0x87: return $"F{vkCode - 0x70 + 1}";
        }

        // Letters/digits/punctuation: translate through the real keyboard layout (respecting Shift/CapsLock)
        // so what's shown matches what was actually typed, not the raw unshifted key.
        var keyboardState = new byte[256];
        if (!GetKeyboardState(keyboardState)) return null;

        var scanCode = MapVirtualKey((uint)vkCode, 0);
        var sb = new StringBuilder(8);
        int result = ToUnicode((uint)vkCode, scanCode, keyboardState, sb, sb.Capacity, 0);
        if (result > 0)
        {
            var text = sb.ToString(0, result);
            return text.Length == 1 && char.IsControl(text[0]) ? null : text.ToUpperInvariant();
        }

        return null;
    }

    public void Stop()
    {
        if (_thread is null) return;
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, nint.Zero, nint.Zero);
        _thread.Join(1000);
        _thread = null;
        _proc = null;
    }

    public void Dispose() => Stop();
}
