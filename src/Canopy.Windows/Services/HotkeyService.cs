using System.Runtime.InteropServices;
using Canopy.Core.Input;
using Canopy.Windows.Interop;
using Microsoft.UI.Xaml;
using Windows.System;
using WinRT.Interop;

namespace Canopy.Windows.Services;

/// <summary>
/// Global hotkey registration and handling for WinUI 3
/// </summary>
public class HotkeyService : IHotkeyService
{
    private readonly Dictionary<int, HotkeyRegistration> _registeredHotkeys = new();
    private IntPtr _hwnd;
    private int _nextHotkeyId = 1;
    private bool _isDisposed;
    private bool _isInitialized;

    // Subclassing fields
    private IntPtr _prevWndProc = IntPtr.Zero;
    private WndProc? _wndProcDelegate;

    public event EventHandler<HotkeyEventArgs>? HotkeyPressed;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Initializes the hotkey service with a window handle
    /// Subclasses the window to intercept WM_HOTKEY and forward it to this service.
    /// </summary>
    public void Initialize(Window window)
    {
        if (_isInitialized) return;
        
        _hwnd = WindowNative.GetWindowHandle(window);
        if (_hwnd == IntPtr.Zero) return;

        // Keep delegate alive in a field to prevent GC
        _wndProcDelegate = WndProcHandler;

        // Install subclass; store previous proc so we can restore later
        var newProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        _prevWndProc = NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWLP_WNDPROC, newProcPtr);
        _isInitialized = true;
    }

    private IntPtr WndProcHandler(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            HandleHotkeyMessage(id);
            return IntPtr.Zero; // handled
        }

        // Call original WndProc for other messages
        return NativeMethods.CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Registers a global hotkey from a shortcut string (e.g., "Ctrl+Alt+O")
    /// </summary>
    public int RegisterHotkey(string shortcut, string name)
    {
        if (!TryParseShortcut(shortcut, out var key, out var modifiers))
            return -1;

        return RegisterHotkey(key, modifiers, name, shortcut);
    }

    /// <summary>
    /// Registers a global hotkey using VirtualKey
    /// </summary>
    public int RegisterHotkey(VirtualKey key, WindowsHotkeyModifiers modifiers, string name)
    {
        return RegisterHotkey((uint)key, modifiers, name, "");
    }

    /// <summary>
    /// Registers a global hotkey using VirtualKeys constants
    /// </summary>
    public int RegisterHotkey(VirtualKeys key, WindowsHotkeyModifiers modifiers, string name)
    {
        return RegisterHotkey((uint)key, modifiers, name, "");
    }

    /// <summary>
    /// Registers a global hotkey
    /// </summary>
    private int RegisterHotkey(uint key, WindowsHotkeyModifiers modifiers, string name, string shortcut)
    {
        if (_hwnd == IntPtr.Zero) return -1;

        var id = _nextHotkeyId++;
        var mods = (uint)modifiers | NativeMethods.MOD_NOREPEAT;

        if (NativeMethods.RegisterHotKey(_hwnd, id, mods, key))
        {
            _registeredHotkeys[id] = new HotkeyRegistration
            {
                Id = id,
                Key = key,
                Modifiers = modifiers,
                Name = name,
                Shortcut = shortcut
            };
            return id;
        }

        return -1;
    }

    /// <summary>
    /// Unregisters a hotkey by ID
    /// </summary>
    public bool UnregisterHotkey(int id)
    {
        if (_hwnd == IntPtr.Zero) return false;

        if (_registeredHotkeys.ContainsKey(id))
        {
            NativeMethods.UnregisterHotKey(_hwnd, id);
            _registeredHotkeys.Remove(id);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Unregisters all hotkeys
    /// </summary>
    public void UnregisterAll()
    {
        if (_hwnd == IntPtr.Zero) return;

        foreach (var id in _registeredHotkeys.Keys.ToList())
        {
            NativeMethods.UnregisterHotKey(_hwnd, id);
        }
        _registeredHotkeys.Clear();
    }

    /// <summary>
    /// Unregisters all hotkeys (alias for UnregisterAll)
    /// </summary>
    public void UnregisterAllHotkeys() => UnregisterAll();

    /// <summary>
    /// Handles WM_HOTKEY messages
    /// </summary>
    private void HandleHotkeyMessage(int hotkeyId)
    {
        if (_registeredHotkeys.TryGetValue(hotkeyId, out var registration))
        {
            HotkeyPressed?.Invoke(this, new HotkeyEventArgs
            {
                HotkeyId = hotkeyId,
                Name = registration.Name,
                Shortcut = registration.Shortcut
            });
        }
    }

    /// <summary>
    /// Parses a shortcut string like "Ctrl+Alt+O" into key and modifiers
    /// </summary>
    public static bool TryParseShortcut(string shortcut, out uint key, out WindowsHotkeyModifiers modifiers)
    {
        key = 0;
        modifiers = 0;

        if (string.IsNullOrEmpty(shortcut))
            return false;

        var parts = shortcut.Split('+');
        
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            switch (trimmed.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= WindowsHotkeyModifiers.Control;
                    break;
                case "ALT":
                    modifiers |= WindowsHotkeyModifiers.Alt;
                    break;
                case "SHIFT":
                    modifiers |= WindowsHotkeyModifiers.Shift;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= WindowsHotkeyModifiers.Win;
                    break;
                default:
                    // Try to parse as a VirtualKeys enum
                    if (Enum.TryParse<VirtualKeys>("VK_" + trimmed.ToUpperInvariant(), out var vk))
                    {
                        key = (uint)vk;
                    }
                    else if (trimmed.Length == 1 && char.IsLetterOrDigit(trimmed[0]))
                    {
                        // Single character key (A-Z maps to 0x41-0x5A)
                        key = (uint)(0x41 + (char.ToUpperInvariant(trimmed[0]) - 'A'));
                    }
                    break;
            }
        }

        return key != 0 && modifiers != 0;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        UnregisterAll();

        // restore original wndproc
        if (_prevWndProc != IntPtr.Zero && _hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWLP_WNDPROC, _prevWndProc);
            _prevWndProc = IntPtr.Zero;
        }
    }

    private class HotkeyRegistration
    {
        public int Id { get; init; }
        public uint Key { get; init; }
        public WindowsHotkeyModifiers Modifiers { get; init; }
        public string Name { get; init; } = "";
        public string Shortcut { get; init; } = "";
    }
}

/// <summary>
/// Windows-specific hotkey modifiers (maps to Win32 MOD_* constants)
/// </summary>
[Flags]
public enum WindowsHotkeyModifiers : uint
{
    None = 0,
    Alt = NativeMethods.MOD_ALT,
    Control = NativeMethods.MOD_CONTROL,
    Shift = NativeMethods.MOD_SHIFT,
    Win = NativeMethods.MOD_WIN
}

/// <summary>
/// Well-known virtual key codes
/// </summary>
public enum VirtualKeys : uint
{
    VK_A = 0x41,
    VK_B = 0x42,
    VK_C = 0x43,
    VK_D = 0x44,
    VK_E = 0x45,
    VK_F = 0x46,
    VK_G = 0x47,
    VK_H = 0x48,
    VK_I = 0x49,
    VK_J = 0x4A,
    VK_K = 0x4B,
    VK_L = 0x4C,
    VK_M = 0x4D,
    VK_N = 0x4E,
    VK_O = 0x4F,
    VK_P = 0x50,
    VK_Q = 0x51,
    VK_R = 0x52,
    VK_S = 0x53,
    VK_T = 0x54,
    VK_U = 0x55,
    VK_V = 0x56,
    VK_W = 0x57,
    VK_X = 0x58,
    VK_Y = 0x59,
    VK_Z = 0x5A,
    VK_0 = 0x30,
    VK_1 = 0x31,
    VK_2 = 0x32,
    VK_3 = 0x33,
    VK_4 = 0x34,
    VK_5 = 0x35,
    VK_6 = 0x36,
    VK_7 = 0x37,
    VK_8 = 0x38,
    VK_9 = 0x39,
    VK_F1 = 0x70,
    VK_F2 = 0x71,
    VK_F3 = 0x72,
    VK_F4 = 0x73,
    VK_F5 = 0x74,
    VK_F6 = 0x75,
    VK_F7 = 0x76,
    VK_F8 = 0x77,
    VK_F9 = 0x78,
    VK_F10 = 0x79,
    VK_F11 = 0x7A,
    VK_F12 = 0x7B,
    VK_OEM_3 = 0xC0, // ` key
    VK_ESCAPE = 0x1B,
    VK_TAB = 0x09,
    VK_SPACE = 0x20,
}
