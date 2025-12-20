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
    private readonly object _lock = new();
    private int _nextHotkeyId = 1;
    private bool _isDisposed;
    private Thread? _keyListenerThread;
    private volatile bool _stopRequested;
    
    // X11 interop - separate display for listener thread
    private IntPtr _displayMain;
    private IntPtr _displayListener;
    private IntPtr _rootWindow;
    private bool _x11Available;

    public event EventHandler<HotkeyEventArgs>? HotkeyPressed;

    // X11 Key modifiers
    private const uint Mod1Mask = 1 << 3;  // Alt
    private const uint ControlMask = 1 << 2;  // Control
    private const uint ShiftMask = 1 << 0;  // Shift
    private const uint Mod4Mask = 1 << 6;  // Super/Windows
    private const uint LockMask = 1 << 1;  // CapsLock
    private const uint Mod2Mask = 1 << 4;  // NumLock

    // X11 Event types
    private const int KeyPress = 2;

    public LinuxHotkeyService()
    {
        _logger = CanopyLoggerFactory.CreateLogger<LinuxHotkeyService>();
        
        try
        {
            InitializeX11();
        }
        catch (Exception ex)
        {
            _logger.Warning($"X11 hotkey service initialization failed: {ex.Message}");
            _x11Available = false;
        }
    }

    private void InitializeX11()
    {
        try
        {
            // Check if we're on Wayland (X11 hotkeys won't work)
            var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            if (sessionType?.ToLower() == "wayland")
            {
                _logger.Warning("Running on Wayland - global hotkeys may not work");
            }

            _displayMain = XOpenDisplay(IntPtr.Zero);
            if (_displayMain == IntPtr.Zero)
            {
                _logger.Warning("Failed to open X11 display - hotkeys will not work");
                _x11Available = false;
                return;
            }

            _rootWindow = XDefaultRootWindow(_displayMain);
            _x11Available = true;
            _logger.Info("X11 hotkey service initialized");
        }
        catch (DllNotFoundException ex)
        {
            _logger.Warning($"X11 library not found: {ex.Message}");
            _x11Available = false;
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
            
            // Grab the key with various modifier combinations (NumLock, CapsLock)
            var modCombinations = new uint[] { 0, LockMask, Mod2Mask, LockMask | Mod2Mask };
            
            foreach (var extraMod in modCombinations)
            {
                var result = XGrabKey(_displayMain, (int)keycode, modifiers | extraMod, _rootWindow, false, 1, 1);
                if (result == 0)
                {
                    _logger.Warning($"XGrabKey failed for {shortcut} with mods {modifiers | extraMod}");
                }
            }
            
            XFlush(_displayMain);

            lock (_lock)
            {
                _registeredHotkeys[id] = new HotkeyRegistration
                {
                    Id = id,
                    Keycode = keycode,
                    Modifiers = modifiers,
                    Name = name,
                    Shortcut = shortcut
                };
            }

            // Start listener thread if not started
            StartListenerThread();

            _logger.Info($"Registered hotkey: {shortcut} ({name}) keycode={keycode} mods={modifiers}");
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

        _stopRequested = false;
        _keyListenerThread = new Thread(KeyListenerLoop)
        {
            IsBackground = true,
            Name = "HotkeyListener"
        };
        _keyListenerThread.Start();
        _logger.Debug("Hotkey listener thread started");
    }

    private void KeyListenerLoop()
    {
        // Open separate display connection for this thread
        _displayListener = XOpenDisplay(IntPtr.Zero);
        if (_displayListener == IntPtr.Zero)
        {
            _logger.Error("Failed to open X11 display for listener thread");
            return;
        }

        var rootWindow = XDefaultRootWindow(_displayListener);
        
        // Select key press events on root window
        XSelectInput(_displayListener, rootWindow, 1); // KeyPressMask = 1
        
        _logger.Debug("Hotkey listener loop started");
        
        while (!_stopRequested && _x11Available)
        {
            try
            {
                // Use XPending to check for events without blocking
                while (XPending(_displayListener) > 0 && !_stopRequested)
                {
                    var ev = new XKeyEvent();
                    XNextEvent(_displayListener, ref ev);

                    if (ev.type == KeyPress)
                    {
                        var keycode = ev.keycode;
                        // Mask out NumLock and CapsLock
                        var modifiers = ev.state & (ControlMask | Mod1Mask | ShiftMask | Mod4Mask);

                        _logger.Debug($"Key event: keycode={keycode} state={ev.state} modifiers={modifiers}");

                        lock (_lock)
                        {
                            foreach (var reg in _registeredHotkeys.Values)
                            {
                                if (reg.Keycode == keycode && reg.Modifiers == modifiers)
                                {
                                    _logger.Info($"Hotkey matched: {reg.Name}");
                                    
                                    // Dispatch to GTK thread
                                    var args = new HotkeyEventArgs
                                    {
                                        HotkeyId = reg.Id,
                                        Name = reg.Name,
                                        Shortcut = reg.Shortcut
                                    };
                                    
                                    GLib.Idle.Add(() =>
                                    {
                                        HotkeyPressed?.Invoke(this, args);
                                        return false;
                                    });
                                    break;
                                }
                            }
                        }
                    }
                }
                
                Thread.Sleep(10); // Small sleep to avoid busy-waiting
            }
            catch (Exception ex)
            {
                _logger.Error("Error in hotkey listener", ex);
                Thread.Sleep(100);
            }
        }

        if (_displayListener != IntPtr.Zero)
        {
            XCloseDisplay(_displayListener);
            _displayListener = IntPtr.Zero;
        }
        
        _logger.Debug("Hotkey listener loop ended");
    }

    public bool UnregisterHotkey(int id)
    {
        if (!_x11Available)
            return false;

        HotkeyRegistration? reg;
        lock (_lock)
        {
            if (!_registeredHotkeys.TryGetValue(id, out reg))
                return false;
            _registeredHotkeys.Remove(id);
        }

        try
        {
            var modCombinations = new uint[] { 0, LockMask, Mod2Mask, LockMask | Mod2Mask };
            foreach (var extraMod in modCombinations)
            {
                XUngrabKey(_displayMain, (int)reg.Keycode, reg.Modifiers | extraMod, _rootWindow);
            }
            XFlush(_displayMain);
            
            _logger.Info($"Unregistered hotkey: {reg.Name}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to unregister hotkey {id}", ex);
            return false;
        }
    }

    public void UnregisterAll()
    {
        List<int> ids;
        lock (_lock)
        {
            ids = _registeredHotkeys.Keys.ToList();
        }
        
        foreach (var id in ids)
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
        string? keyName = null;

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            switch (trimmed.ToUpperInvariant())
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
                    keyName = trimmed;
                    break;
            }
        }

        if (keyName == null)
            return false;

        // Try to get keysym - handle various formats
        var keysym = XStringToKeysym(keyName.ToLower());
        if (keysym == 0)
        {
            // Try uppercase for single letters
            keysym = XStringToKeysym(keyName.ToUpper());
        }
        if (keysym == 0 && keyName.Length == 1)
        {
            // For single characters, try the character code
            keysym = (ulong)keyName.ToLower()[0];
        }
        
        if (keysym != 0)
        {
            keycode = (uint)XKeysymToKeycode(_displayMain, keysym);
            _logger.Debug($"Parsed shortcut {shortcut}: keysym={keysym} keycode={keycode} modifiers={modifiers}");
        }

        return keycode != 0 && modifiers != 0;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _stopRequested = true;
        _keyListenerThread?.Join(1000);
        
        UnregisterAll();

        if (_displayMain != IntPtr.Zero)
        {
            XCloseDisplay(_displayMain);
            _displayMain = IntPtr.Zero;
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
    private static extern int XNextEvent(IntPtr display, ref XKeyEvent eventReturn);

    [DllImport("libX11.so.6")]
    private static extern int XSelectInput(IntPtr display, IntPtr window, long eventMask);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern ulong XStringToKeysym(string str);

    [DllImport("libX11.so.6")]
    private static extern int XKeysymToKeycode(IntPtr display, ulong keysym);

    [StructLayout(LayoutKind.Sequential)]
    private struct XKeyEvent
    {
        public int type;
        public IntPtr serial;
        public int send_event;
        public IntPtr display;
        public IntPtr window;
        public IntPtr root;
        public IntPtr subwindow;
        public IntPtr time;
        public int x, y;
        public int x_root, y_root;
        public uint state;
        public uint keycode;
        public int same_screen;
        // Padding to match X11 event size
        private readonly IntPtr _pad1, _pad2, _pad3, _pad4;
    }

    #endregion
}
