using System.Runtime.InteropServices;

namespace Canopy.Windows.Interop;

/// <summary>
/// Native Windows API methods for advanced window manipulation
/// </summary>
internal static partial class NativeMethods
{
    // Window styles
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    // Hotkey modifiers
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // Window messages
    public const int WM_HOTKEY = 0x0312;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int WM_GETMINMAXINFO = 0x0024;
    public const int HT_CAPTION = 0x2;

    // System metrics
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    // Subclassing
    public const int GWLP_WNDPROC = -4;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [LibraryImport("user32.dll")]
    public static partial int GetWindowLongW(IntPtr hwnd, int index);

    [LibraryImport("user32.dll")]
    public static partial int SetWindowLongW(IntPtr hwnd, int index, int newStyle);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newProc);

    [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
    public static partial IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int nIndex);

    // Show window commands
    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;

    /// <summary>
    /// Gets primary screen dimensions
    /// </summary>
    public static (int Width, int Height) GetPrimaryScreenSize()
    {
        return (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
    }

    /// <summary>
    /// Makes window click-through
    /// </summary>
    public static void MakeClickThrough(IntPtr hwnd)
    {
        var extendedStyle = GetWindowLongW(hwnd, GWL_EXSTYLE);
        SetWindowLongW(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
    }

    /// <summary>
    /// Removes click-through from window
    /// </summary>
    public static void RemoveClickThrough(IntPtr hwnd)
    {
        var extendedStyle = GetWindowLongW(hwnd, GWL_EXSTYLE);
        SetWindowLongW(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
    }

    /// <summary>
    /// Starts window drag operation
    /// </summary>
    public static void StartWindowDrag(IntPtr hwnd)
    {
        ReleaseCapture();
        SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
    }
}

/// <summary>
/// Helper to enforce minimum window size via Win32 subclassing
/// </summary>
internal class MinimumSizeEnforcer : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly int _minWidth;
    private readonly int _minHeight;
    private readonly IntPtr _oldWndProc;
    private readonly WndProc _newWndProc;
    private bool _disposed;

    // Delegate for window procedure
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public MinimumSizeEnforcer(IntPtr hwnd, int minWidth, int minHeight)
    {
        _hwnd = hwnd;
        _minWidth = minWidth;
        _minHeight = minHeight;

        // Keep delegate alive
        _newWndProc = WndProcHandler;

        // Subclass the window
        _oldWndProc = NativeMethods.SetWindowLongPtr(
            _hwnd,
            NativeMethods.GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_newWndProc));
    }

    private IntPtr WndProcHandler(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_GETMINMAXINFO)
        {
            var mmi = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
            mmi.ptMinTrackSize.x = _minWidth;
            mmi.ptMinTrackSize.y = _minHeight;
            Marshal.StructureToPtr(mmi, lParam, false);
            return IntPtr.Zero;
        }

        return NativeMethods.CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // Restore original window procedure
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWLP_WNDPROC, _oldWndProc);
            _disposed = true;
        }
    }
}