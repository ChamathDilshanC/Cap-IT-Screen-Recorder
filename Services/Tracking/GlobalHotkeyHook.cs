using System.Runtime.InteropServices;

namespace ScreenRecorderApp.Services.Tracking;

/// <summary>
/// Dedicated system-wide WH_KEYBOARD_LL hook for Phase 6's annotation hotkeys (Ctrl+Shift+D to toggle
/// drawing mode, Esc to clear). Deliberately a separate instance from <see cref="GlobalKeyboardHook"/>
/// rather than sharing it: that hook is scoped to VideoCaptureService's capture session and only exists
/// at all when zoom or the keystroke overlay is enabled, whereas this one must exist whenever the
/// annotation overlay is armed, independent of those other features. Mirrors
/// GlobalKeyboardHook/GlobalMouseHook's P/Invoke and message-pump pattern exactly — low-level hooks only
/// fire on the thread that installed them, and only while that thread is pumping messages — rather than
/// sharing a base class, the same tradeoff those two already make for each other.
///
/// Scoped to the lifetime of an armed annotation overlay (started when a recording with Annotations
/// enabled begins, stopped when it ends) rather than installed for the whole app session: a system-wide
/// low-level keyboard hook running at all times is exactly the kind of behavior some antivirus/EDR
/// software flags, so it's only ever live while this opt-in feature is actually in use.
/// </summary>
public sealed class GlobalHotkeyHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const int VK_D = 0x44;
    private const int VK_ESCAPE = 0x1B;

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

    // De-dupes the low-level hook's key-repeat: a key held down fires WM_KEYDOWN repeatedly, but a
    // hotkey toggle should fire exactly once per physical press.
    private bool _dKeyLatched;
    private bool _escLatched;

    /// <summary>Ctrl+Shift+D was pressed. Fires on the hook's own background thread — subscribers touching UI/WinUI state must marshal back via DispatcherQueue.</summary>
    public event Action? ToggleDrawingModeRequested;

    /// <summary>Esc was pressed. Same threading caveat as <see cref="ToggleDrawingModeRequested"/>.</summary>
    public event Action? ClearRequested;

    public void Start()
    {
        if (_thread is not null) return;

        _thread = new Thread(RunMessageLoop) { IsBackground = true, Name = "GlobalHotkeyHook" };
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
        if (nCode >= 0)
        {
            try
            {
                int vkCode = Marshal.ReadInt32(lParam);

                if (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN)
                {
                    if (vkCode == VK_D && IsDown(VK_CONTROL) && IsDown(VK_SHIFT) && !_dKeyLatched)
                    {
                        _dKeyLatched = true;
                        ToggleDrawingModeRequested?.Invoke();
                    }
                    else if (vkCode == VK_ESCAPE && !_escLatched)
                    {
                        _escLatched = true;
                        ClearRequested?.Invoke();
                    }
                }
                else if (wParam == WM_KEYUP || wParam == WM_SYSKEYUP)
                {
                    if (vkCode == VK_D) _dKeyLatched = false;
                    else if (vkCode == VK_ESCAPE) _escLatched = false;
                }
            }
            catch
            {
                // Best effort: never let a subscriber's exception break the hook chain.
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool IsDown(int vk) => (GetKeyState(vk) & 0x8000) != 0;

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
