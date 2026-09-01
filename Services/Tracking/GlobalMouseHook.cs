using System.Runtime.InteropServices;

namespace ScreenRecorderApp.Services.Tracking;

/// <summary>
/// Installs a system-wide WH_MOUSE_LL hook on a dedicated thread (mirrors <see cref="GlobalKeyboardHook"/>'s
/// pattern exactly — low-level hooks only fire on the thread that installed them, and only while that
/// thread is pumping messages) and raises <see cref="Click"/> for button-down and wheel events. Used as an
/// activity signal for the smart zoom feature; DXGI's Desktop Duplication API reports cursor *position*
/// every frame already, but never button state, so clicks need a real hook.
/// </summary>
public sealed class GlobalMouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MOUSEWHEEL = 0x020A;

    private delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);

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
    // Kept alive for the lifetime of the hook — see GlobalKeyboardHook's identical field for why.
    private LowLevelMouseProc? _proc;

    public event Action? Click;

    public void Start()
    {
        if (_thread is not null) return;

        _thread = new Thread(RunMessageLoop) { IsBackground = true, Name = "GlobalMouseHook" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void RunMessageLoop()
    {
        _threadId = (uint)Environment.CurrentManagedThreadId;
        _proc = HookCallback;

        using var curModule = System.Diagnostics.Process.GetCurrentProcess().MainModule;
        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule?.ModuleName), 0);

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
            var message = (int)wParam;
            if (message is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_MOUSEWHEEL)
            {
                try { Click?.Invoke(); }
                catch { /* best effort: never let a subscriber exception break the hook chain */ }
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
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
