using Canopy.Core.Application;
using Canopy.Core.GameDetection;
using Canopy.Core.IPC;
using Canopy.Windows.Interop;
using Canopy.Windows.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace Canopy.Windows;

/// <summary>
/// Overlay window that sits on top of games
/// Positioned at x = screen.Width - overlayWidth, y = 0
/// </summary>
public sealed partial class OverlayWindow : Window
{
    private readonly WebViewIpcBridge _ipcBridge;
    private readonly ISettingsService _settingsService;
    private AppWindow? _appWindow;
    private IntPtr _hwnd;
    private bool _isDragEnabled;
    private bool _isDragging;
    public Boolean IsBorderVisible = false;

    public BitmapImage? BorderSource = null;


    // Drag state - use screen coordinates for smooth dragging
    private POINT _dragStartCursor;
    private PointInt32 _windowStartPosition;

    // Animation storyboards
    private Storyboard? _currentStoryboard;

    // Default overlay dimensions
    private const int DefaultWidth = 280;
    private const int DefaultHeight = 520;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    public bool IsDragEnabled
    {
        get => _isDragEnabled;
        set
        {
            _isDragEnabled = value;
            DragHandle.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            UpdateClickThrough();
        }
    }

    public OverlayWindow()
    {
        InitializeComponent();

        _ipcBridge = App.Services.GetRequiredService<WebViewIpcBridge>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();

        _ipcBridge.Subscribe(IpcMessageTypes.GameStarted, this.OnGameStarted);
        _ipcBridge.Subscribe(IpcMessageTypes.DataReceived, this.OnDataReceived);
        Debug.WriteLine("overlay ready");


        SetupOverlayWindow();
        RestorePosition();

        // Default to indeterminate state at startup
        SetIndeterminate();

        SetupEventHandlers();

        _ = _ipcBridge.Send(new IpcMessage
        {
            Type = IpcMessageTypes.OverlayShow,
        });
    }

    public async Task<Bitmap> LoadImageFromUrlAsync(string imageUrl)
    {
        using var httpClient = new HttpClient();
        using var stream = await httpClient.GetStreamAsync(imageUrl);
        return new Bitmap(stream);
    }

    private void SetupOverlayWindow()
    {
        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow != null)
        {
            // Set window size
            _appWindow.Resize(new SizeInt32(DefaultWidth, DefaultHeight));

            // Use OverlappedPresenter to configure topmost
            var presenter = OverlappedPresenter.Create();
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(true, false);
            _appWindow.SetPresenter(presenter);

            // Hide from taskbar
            _appWindow.IsShownInSwitchers = false;

            // Track position changes for persistence
            _appWindow.Changed += AppWindow_Changed;
        }
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        // Save position when window is moved (after drag ends)
        if (args.DidPositionChange && !_isDragging && _isDragEnabled)
        {
            SavePosition();
        }
    }

    private void SetupEventHandlers()
    {
        // Event handlers are wired up in XAML
    }

    /// <summary>
    /// Restores the overlay position from settings, or defaults to screen edge
    /// </summary>
    private void RestorePosition()
    {
        if (_appWindow == null) return;

        var settings = _settingsService.Settings;
        if (settings.OverlayX.HasValue && settings.OverlayY.HasValue)
        {
            _appWindow.Move(new PointInt32(settings.OverlayX.Value, settings.OverlayY.Value));
        }
        else
        {
            PositionAtScreenEdge();
        }
    }

    /// <summary>
    /// Saves the current overlay position to settings
    /// </summary>
    private void SavePosition()
    {
        if (_appWindow == null) return;

        var position = _appWindow.Position;
        _settingsService.Update(s =>
        {
            s.OverlayX = position.X;
            s.OverlayY = position.Y;
        });
        Debug.WriteLine($"Saved overlay position: {position.X}, {position.Y}");
    }

    /// <summary>
    /// Positions the overlay at the right edge of the primary screen
    /// </summary>
    public void PositionAtScreenEdge()
    {
        if (_appWindow == null) return;
        var (screenWidth, _) = NativeMethods.GetPrimaryScreenSize();
        var x = screenWidth - DefaultWidth;
        var y = 0;

        _appWindow.Move(new PointInt32(x, y));
    }

    private void OnGameStarted(IpcMessage message)
    {
        Debug.WriteLine("OnGameStarted");
        Debug.Write(message.Payload);
    }

    private void UpdateLabels(int label, JsonElement element)
    {
        string labelText = element.GetProperty("label").GetString() ?? "";
        string valueText = element.GetProperty("value").GetString() ?? "";
        int subValue;
        string subValueText;
        string helpText = element.GetProperty("helptext").GetString() ?? "";
        
        switch (label)
        {
            case 1:
                Stat1Label.Text = labelText;
                Stat1Value.Text = valueText;
                Stat1Helptext.Text = helpText;
                break;
            case 2:
                subValue = element.GetProperty("subvalue").GetInt32();
                subValueText = subValue > 0 ? ("+" + subValue.ToString()) : "";
                Stat2Label.Text = labelText;
                Stat2Value.Text = valueText;
                Stat2SubVvalue.Text = subValueText;
                Stat2Helptext.Text = helpText;
                break;
            case 3:
                Stat3Label.Text = labelText;
                Stat3Value.Text = valueText;
                Stat3Helptext.Text = helpText;
                break;
            case 4:
                subValue = element.GetProperty("subvalue").GetInt32();
                subValueText = subValue > 0 ? ("+" + subValue.ToString()) : "";
                Stat4Label.Text = labelText;
                Stat4Value.Text = valueText;
                Stat4SubVvalue.Text = subValueText;
                Stat4Helptext.Text = helpText;
                break;

            default: break;
        }
    }

    private void OnDataReceived(IpcMessage message)
    {
        if (message.Payload == null) return;

        try
        {
            Debug.WriteLine($"OnDataReceived: {message.Payload}");
            var payload = (JsonElement)message.Payload;
            
            if (!payload.TryGetProperty("type", out var typeElement))
                return;
                
            var type = typeElement.GetString();

            switch (type)
            {
                case "OVERLAY_STATISTICS":
                    if (payload.TryGetProperty("data", out var statsData))
                    {
                        var data = statsData.EnumerateArray().ToList();
                        for (int i = 0; i < data.Count; i++)
                        {
                            UpdateLabels(i + 1, data[i]);
                        }
                    }
                    break;

                case "INTERVAL_COUNTER_UPDATE":
                    if (payload.TryGetProperty("data", out var counterData))
                    {
                        var counter = counterData.GetInt32();
                        PointsText.Text = counter.ToString();
                    }
                    break;

                case "BORDER_IMAGE_UPDATE":
                    if (payload.TryGetProperty("data", out var borderData))
                    {
                        UpdateBorderImage(borderData.GetString());
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error processing overlay data: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the border/frame image overlay.
    /// </summary>
    private void UpdateBorderImage(string? borderSource)
    {
        if (string.IsNullOrEmpty(borderSource))
        {
            // Hide border
            OverlayBorder.Source = null;
            OverlayBorder.Visibility = Visibility.Collapsed;
            BorderSource = null;
            IsBorderVisible = false;
            Debug.WriteLine("Border image cleared");
            return;
        }

        try
        {
            var bmp = new BitmapImage { UriSource = new Uri(borderSource) };
            OverlayBorder.Source = bmp;
            OverlayBorder.Visibility = Visibility.Visible;
            BorderSource = bmp;
            IsBorderVisible = true;
            Debug.WriteLine($"Border image set: {borderSource}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load border image: {ex.Message}");
            OverlayBorder.Source = null;
            OverlayBorder.Visibility = Visibility.Collapsed;
            BorderSource = null;
            IsBorderVisible = false;
        }
    }

    /// <summary>
    /// Toggles drag mode on/off
    /// </summary>
    public void ToggleDragMode()
    {
        IsDragEnabled = !IsDragEnabled;
        Debug.WriteLine($"Drag mode: {IsDragEnabled}");
    }

    /// <summary>
    /// Updates the game info displayed in the overlay header
    /// </summary>
    /// <param name="gameName">Name of the running game, or null if no game</param>
    /// <param name="elapsedTime">Formatted elapsed time string (e.g., "01:23:45")</param>
    public void UpdateGameInfo(string? gameName, string? elapsedTime = null)
    {
        if (gameName != null)
        {
            GameNameText.Text = gameName;
            GameStatusText.Text = elapsedTime != null
                ? $"Playing now · Time elapsed: {elapsedTime}"
                : "Playing now";
            PointsText.Text = "0";
            SetActive();
        }
        else
        {
            GameNameText.Text = "No game running";
            GameStatusText.Text = "sleeping - start a game to wake";
            PointsText.Text = "--";
            SetIndeterminate();
        }
    }

    /// <summary>
    /// Updates the points counter display
    /// </summary>
    public void UpdatePoints(int? points)
    {
        PointsText.Text = points?.ToString("N0") ?? "--";
    }

    /// <summary>
    /// Updates a statistic by index (1-4)
    /// </summary>
    public void UpdateStat(int index, string label, string value)
    {
        switch (index)
        {
            case 1:
                Stat1Label.Text = label;
                Stat1Value.Text = value;
                break;
            case 2:
                Stat2Label.Text = label;
                Stat2Value.Text = value;
                break;
            case 3:
                Stat3Label.Text = label;
                Stat3Value.Text = value;
                break;
            case 4:
                Stat4Label.Text = label;
                Stat4Value.Text = value;
                break;
        }
    }

    /// <summary>
    /// Updates click-through behavior based on drag state
    /// </summary>
    private void UpdateClickThrough()
    {
        if (_hwnd == IntPtr.Zero) return;

        if (_isDragEnabled)
        {
            // Remove click-through when dragging is enabled
            NativeMethods.RemoveClickThrough(_hwnd);
        }
        else
        {
            // Make click-through when not dragging
            NativeMethods.MakeClickThrough(_hwnd);
            
            // Save position when exiting drag mode
            SavePosition();
        }
    }

    /// <summary>
    /// Handles drag initiation from the drag handle
    /// </summary>
    private void DragHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragEnabled && _appWindow != null)
        {
            _isDragging = true;
            
            // Get cursor position in screen coordinates
            GetCursorPos(out _dragStartCursor);
            _windowStartPosition = _appWindow.Position;
            
            // Capture pointer for smooth dragging
            DragHandle.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles drag movement using screen coordinates for smooth tracking
    /// </summary>
    private void DragHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging && _appWindow != null)
        {
            // Get current cursor position in screen coordinates
            GetCursorPos(out var currentCursor);
            
            var deltaX = currentCursor.X - _dragStartCursor.X;
            var deltaY = currentCursor.Y - _dragStartCursor.Y;

            var newX = _windowStartPosition.X + deltaX;
            var newY = _windowStartPosition.Y + deltaY;

            _appWindow.Move(new PointInt32(newX, newY));
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles drag completion
    /// </summary>
    private void DragHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            DragHandle.ReleasePointerCapture(e.Pointer);
            SavePosition();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles pointer capture lost (e.g., window loses focus during drag)
    /// </summary>
    private void DragHandle_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            SavePosition();
        }
    }

    /// <summary>
    /// Toggles overlay visibility
    /// </summary>
    public void Toggle()
    {
        if (_appWindow == null) return;

        // Check if overlay is enabled in settings
        if (!_settingsService.Settings.EnableOverlay)
            return;

        if (_appWindow.IsVisible)
        {
            HideOverlay();
        }
        else
        {
            Show();
        }
    }

    /// <summary>
    /// Shows the overlay
    /// </summary>
    public void Show()
    {
        if (_appWindow == null) return;

        // Check if overlay is enabled in settings
        if (!_settingsService.Settings.EnableOverlay)
            return;

        _appWindow.Show();
        RestorePosition();
    }

    /// <summary>
    /// Hides the overlay
    /// </summary>
    public void HideOverlay()
    {
        // Disable drag mode when hiding
        if (_isDragEnabled)
        {
            IsDragEnabled = false;
        }
        _appWindow?.Hide();
    }

    /// <summary>
    /// Gets whether the overlay is currently visible
    /// </summary>
    public bool IsVisible => _appWindow?.IsVisible ?? false;

    /// <summary>
    /// Starts the breathing animation (indeterminate / no game)
    /// </summary>
    public void SetIndeterminate()
    {
        try
        {
            StopCurrentAnimation();

            // Ensure the animation targets exist
            if (AnimatedBorderScale == null || AnimatedCounterBorder == null)
            {
                Debug.WriteLine("Animation targets not ready yet");
                return;
            }

            var storyboard = new Storyboard
            {
                RepeatBehavior = RepeatBehavior.Forever,
                AutoReverse = true
            };

            var scaleXAnim = new DoubleAnimation
            {
                From = 1.0,
                To = 1.06,
                Duration = new Duration(TimeSpan.FromSeconds(2.5))
            };
            Storyboard.SetTarget(scaleXAnim, AnimatedBorderScale);
            Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");
            storyboard.Children.Add(scaleXAnim);

            var scaleYAnim = new DoubleAnimation
            {
                From = 1.0,
                To = 1.06,
                Duration = new Duration(TimeSpan.FromSeconds(2.5))
            };
            Storyboard.SetTarget(scaleYAnim, AnimatedBorderScale);
            Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");
            storyboard.Children.Add(scaleYAnim);

            var opacityAnim = new DoubleAnimation
            {
                From = 0.85,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromSeconds(2.5))
            };
            Storyboard.SetTarget(opacityAnim, AnimatedCounterBorder);
            Storyboard.SetTargetProperty(opacityAnim, "Opacity");
            storyboard.Children.Add(opacityAnim);

            _currentStoryboard = storyboard;
            storyboard.Begin();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error starting indeterminate animation: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts the ping animation (active / game running)
    /// Mimics CSS: scale 0.8 -> 1.5, opacity 0.8 -> 0
    /// </summary>
    public void SetActive()
    {
        try
        {
            StopCurrentAnimation();

            // Ensure the animation targets exist
            if (AnimatedBorderScale == null || AnimatedCounterBorder == null)
            {
                Debug.WriteLine("Animation targets not ready yet");
                return;
            }

            var storyboard = new Storyboard
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            // Total duration 3s, scale/opacity reach target at 70% (2.1s)
            var duration = TimeSpan.FromSeconds(3);
            var peakTime = TimeSpan.FromSeconds(2.1); // 70% of 3s

            // ScaleX: 0.8 -> 1.5 (at 70%), hold at 1.5 until 100%
            var scaleXAnim = new DoubleAnimationUsingKeyFrames();
            scaleXAnim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
                Value = 0.8
            });
            scaleXAnim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(peakTime),
                Value = 1.5,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            scaleXAnim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(duration),
                Value = 1.5
            });
            Storyboard.SetTarget(scaleXAnim, AnimatedBorderScale);
            Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");
            storyboard.Children.Add(scaleXAnim);

            // ScaleY: 0.8 -> 1.5 (at 70%), hold at 1.5 until 100%
            var scaleYAnim = new DoubleAnimationUsingKeyFrames();
            scaleYAnim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
                Value = 0.8
            });
            scaleYAnim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(peakTime),
                Value = 1.5,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            scaleYAnim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(duration),
                Value = 1.5
            });
            Storyboard.SetTarget(scaleYAnim, AnimatedBorderScale);
            Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");
            storyboard.Children.Add(scaleYAnim);

            // Opacity: 0.8 -> 0 (at 70%), hold at 0 until 100%
            var opacityAnim = new DoubleAnimationUsingKeyFrames();
            opacityAnim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
                Value = 0.8
            });
            opacityAnim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(peakTime),
                Value = 0,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            opacityAnim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(duration),
                Value = 0
            });
            Storyboard.SetTarget(opacityAnim, AnimatedCounterBorder);
            Storyboard.SetTargetProperty(opacityAnim, "Opacity");
            storyboard.Children.Add(opacityAnim);

            _currentStoryboard = storyboard;
            storyboard.Begin();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error starting active animation: {ex.Message}");
        }
    }

    private void StopCurrentAnimation()
    {
        try
        {
            _currentStoryboard?.Stop();
            _currentStoryboard = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error stopping animation: {ex.Message}");
            _currentStoryboard = null;
        }
    }
}
