using System.Diagnostics;
using System.Runtime.InteropServices;
using Canopy.Core.Input;
using Canopy.Core.Logging;

namespace Canopy.Linux.Services;

/// <summary>
/// Linux global hotkey service using X11 XGrab APIs
/// </summary>
public class LinuxHotkeyService : IHotkeyService
{
    private readonly ICanopyLogger _logger;
    private readonly Dictionary<int, HotkeyRegistration> _registeredHotkeys = new();
    private int _nextHotkeyId = 1;
    private bool _isDisposed;
    private Thread? _keyListenerThread;
    private CancellationTokenSource? _cancellationTokenSource;
    
    // X11 interop
    private IntPtr _display;
    private IntPtr _rootWindow;
    private bool _x11Available;

    public event EventHandler<HotkeyEventArgs>? HotkeyPressed;

    // X11 Key modifiers
    private const uint Mod1Mask = 1 << 3;  // Alt
    private const uint ControlMask = 1 << 2;  // Control
    private const uint ShiftMask = 1 << 0;  // Shift
    private const uint Mod4Mask = 1 << 6;  // Super/Windows

    // X11 Event types
    private const int KeyPress = 2;

    public LinuxHotkeyService()
    {
        _logger = CanopyLoggerFactory.CreateLogger<LinuxHotkeyService>();
        InitializeX11();
    }

    private void InitializeX11()
    {
        try
        {
            _display = XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero)
            {
                _logger.Warning("Failed to open X11 display - hotkeys will not work");
                _x11Available = false;
                return;
            }

            _rootWindow = XDefaultRootWindow(_display);
            _x11Available = true;
            _logger.Info("X11 hotkey service initialized");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to initialize X11", ex);
            _x11Available = false;
        }
    }

    public int RegisterHotkey(string shortcut, string name)
    {
        if (!_x11Available)
        {
            _logger.Warning($"Cannot register hotkey {shortcut} - X11 not available");
            return -1;
        }

        if (!TryParseShortcut(shortcut, out var keycode, out var modifiers))
        {
            _logger.Warning($"Failed to parse shortcut: {shortcut}");
            return -1;
        }

        return RegisterHotkeyInternal(keycode, modifiers, name, shortcut);
    }

    private int RegisterHotkeyInternal(uint keycode, uint modifiers, string name, string shortcut)
    {
        try
        {
            var id = _nextHotkeyId++;
            
            // Grab the key
            XGrabKey(_display, (int)keycode, modifiers, _rootWindow, true, 1, 1);
            
            // Also grab with NumLock and CapsLock combinations
            XGrabKey(_display, (int)keycode, modifiers | 2, _rootWindow, true, 1, 1); // NumLock
            XGrabKey(_display, (int)keycode, modifiers | 16, _rootWindow, true, 1, 1); // CapsLock
            XGrabKey(_display, (int)keycode, modifiers | 2 | 16, _rootWindow, true, 1, 1); // Both

            _registeredHotkeys[id] = new HotkeyRegistration
            {
                Id = id,
                Keycode = keycode,
                Modifiers = modifiers,
                Name = name,
                Shortcut = shortcut
            };

            // Start listener thread if not started
            StartListenerThread();

            _logger.Info($"Registered hotkey: {shortcut} ({name})");
            return id;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to register hotkey {shortcut}", ex);
            return -1;
        }
    }

    private void StartListenerThread()
    {
        if (_keyListenerThread != null && _keyListenerThread.IsAlive)
            return;

        _cancellationTokenSource = new CancellationTokenSource();
        _keyListenerThread = new Thread(KeyListenerLoop)
        {
            IsBackground = true,
            Name = "HotkeyListener"
        };
        _keyListenerThread.Start();
    }

    private void KeyListenerLoop()
    {
        var token = _cancellationTokenSource?.Token ?? CancellationToken.None;
        
        while (!token.IsCancellationRequested && _x11Available)
        {
            try
            {
                if (XPending(_display) > 0)
                {
                    var ev = new XEvent();
                    XNextEvent(_display, ref ev);

                    if (ev.type == KeyPress)
                    {
                        var keycode = ev.xkey.keycode;
                        var modifiers = ev.xkey.state & (ControlMask | Mod1Mask | ShiftMask | Mod4Mask);

                        foreach (var reg in _registeredHotkeys.Values)
                        {
                            if (reg.Keycode == keycode && 
                                (reg.Modifiers == modifiers || reg.Modifiers == (modifiers & ~(2u | 16u))))
                            {
                                _logger.Debug($"Hotkey pressed: {reg.Name}");
                                HotkeyPressed?.Invoke(this, new HotkeyEventArgs
                                {
                                    HotkeyId = reg.Id,
                                    Name = reg.Name,
                                    Shortcut = reg.Shortcut
                                });
                                break;
                            }
                        }
                    }
                }
                else
                {
                    Thread.Sleep(50); // Avoid busy waiting
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error in hotkey listener", ex);
                Thread.Sleep(100);
            }
        }
    }

    public bool UnregisterHotkey(int id)
    {
        if (!_x11Available)
            return false;

        if (_registeredHotkeys.TryGetValue(id, out var reg))
        {
            try
            {
                XUngrabKey(_display, (int)reg.Keycode, reg.Modifiers, _rootWindow);
                XUngrabKey(_display, (int)reg.Keycode, reg.Modifiers | 2, _rootWindow);
                XUngrabKey(_display, (int)reg.Keycode, reg.Modifiers | 16, _rootWindow);
                XUngrabKey(_display, (int)reg.Keycode, reg.Modifiers | 2 | 16, _rootWindow);
                
                _registeredHotkeys.Remove(id);
                _logger.Info($"Unregistered hotkey: {reg.Name}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to unregister hotkey {id}", ex);
            }
        }

        return false;
    }

    public void UnregisterAll()
    {
        foreach (var id in _registeredHotkeys.Keys.ToList())
        {
            UnregisterHotkey(id);
        }
    }

    private bool TryParseShortcut(string shortcut, out uint keycode, out uint modifiers)
    {
        keycode = 0;
        modifiers = 0;

        if (string.IsNullOrEmpty(shortcut) || !_x11Available)
            return false;

        var parts = shortcut.Split('+');

        foreach (var part in parts)
        {
            var trimmed = part.Trim().ToUpperInvariant();
            switch (trimmed)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ControlMask;
                    break;
                case "ALT":
                    modifiers |= Mod1Mask;
                    break;
                case "SHIFT":
                    modifiers |= ShiftMask;
                    break;
                case "SUPER":
                case "WIN":
                case "WINDOWS":
                    modifiers |= Mod4Mask;
                    break;
                default:
                    // Convert key name to keycode
                    var keysym = XStringToKeysym(trimmed.ToLower());
                    if (keysym != 0)
                    {
                        keycode = (uint)XKeysymToKeycode(_display, keysym);
                    }
                    else if (trimmed.Length == 1)
                    {
                        // Single character
                        keysym = XStringToKeysym(trimmed.ToLower());
                        if (keysym != 0)
                            keycode = (uint)XKeysymToKeycode(_display, keysym);
                    }
                    break;
            }
        }

        return keycode != 0 && modifiers != 0;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cancellationTokenSource?.Cancel();
        
        UnregisterAll();

        if (_display != IntPtr.Zero)
        {
            XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }

        _logger.Info("Hotkey service disposed");
    }

    private class HotkeyRegistration
    {
        public int Id { get; init; }
        public uint Keycode { get; init; }
        public uint Modifiers { get; init; }
        public string Name { get; init; } = "";
        public string Shortcut { get; init; } = "";
    }

    #region X11 P/Invoke

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow,
        bool ownerEvents, int pointerMode, int keyboardMode);

    [DllImport("libX11.so.6")]
    private static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);

    [DllImport("libX11.so.6")]
    private static extern int XPending(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XNextEvent(IntPtr display, ref XEvent eventReturn);

    [DllImport("libX11.so.6")]
    private static extern ulong XStringToKeysym(string str);

    [DllImport("libX11.so.6")]
    private static extern int XKeysymToKeycode(IntPtr display, ulong keysym);

    [StructLayout(LayoutKind.Sequential)]
    private struct XEvent
    {
        public int type;
        public XKeyEvent xkey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XKeyEvent
    {
        public int type;
        public ulong serial;
        public bool send_event;
        public IntPtr display;
        public IntPtr window;
        public IntPtr root;
        public IntPtr subwindow;
        public ulong time;
        public int x, y;
        public int x_root, y_root;
        public uint state;
        public uint keycode;
        public bool same_screen;
    }

    #endregion
}
