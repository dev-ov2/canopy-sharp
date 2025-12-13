using Canopy.Core.Application;
using Canopy.Windows.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Reflection;
using System.Text;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace Canopy.Windows;

/// <summary>
/// Settings window for Canopy application.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly WindowsPlatformServices _platformServices;
    private readonly UpdateService _updateService;
    private AppWindow? _appWindow;
    private bool _isCapturingShortcut;
    private TextBox? _activeShortcutBox;
    private bool _settingsLoaded;
    private UpdateInfo? _availableUpdate;

    public SettingsWindow()
    {
        InitializeComponent();

        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        _platformServices = App.Services.GetRequiredService<WindowsPlatformServices>();
        _updateService = App.Services.GetRequiredService<UpdateService>();

        // Subscribe to update events
        _updateService.DownloadProgress += OnDownloadProgress;
        _updateService.UpdateDownloaded += OnUpdateDownloaded;
        _updateService.UpdateError += OnUpdateError;

        SetupWindow();
        this.Activated += OnActivated;
        this.Closed += OnClosed;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        // Unsubscribe from events
        _updateService.DownloadProgress -= OnDownloadProgress;
        _updateService.UpdateDownloaded -= OnUpdateDownloaded;
        _updateService.UpdateError -= OnUpdateError;
    }

    private void OnActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        if (!_settingsLoaded)
        {
            _settingsLoaded = true;
            LoadSettings();
        }
    }

    private void SetupWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow != null)
        {
            _appWindow.Resize(new SizeInt32(500, 600));
            _appWindow.Title = "Canopy Settings";

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
            if (displayArea != null)
            {
                var centerX = (displayArea.WorkArea.Width - 500) / 2;
                var centerY = (displayArea.WorkArea.Height - 600) / 2;
                _appWindow.Move(new PointInt32(centerX, centerY));
            }
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Canopy v{version?.Major}.{version?.Minor}.{version?.Build}";
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Settings;
        StartWithWindowsToggle.IsOn = _platformServices.IsRegisteredForStartup();
        AutoUpdateToggle.IsOn = settings.AutoUpdate;
        EnableOverlayToggle.IsOn = settings.EnableOverlay;
        OverlayShortcutTextBox.Text = settings.OverlayToggleShortcut;
        DragShortcutTextBox.Text = settings.OverlayDragShortcut;
        UpdateOverlayControlsState();
    }

    private void UpdateOverlayControlsState()
    {
        var isEnabled = EnableOverlayToggle.IsOn;
        OverlayShortcutGrid.Opacity = isEnabled ? 1.0 : 0.5;
        DragShortcutGrid.Opacity = isEnabled ? 1.0 : 0.5;
        OverlayShortcutTextBox.IsEnabled = isEnabled;
        DragShortcutTextBox.IsEnabled = isEnabled;
        ResetOverlayPositionButton.IsEnabled = isEnabled;
    }

    private void StartWithWindowsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_settingsLoaded) return;
        _platformServices.SetStartupRegistration(StartWithWindowsToggle.IsOn);
        _settingsService.Update(s => s.StartWithWindows = StartWithWindowsToggle.IsOn);
    }

    private void AutoUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_settingsLoaded) return;
        _settingsService.Update(s => s.AutoUpdate = AutoUpdateToggle.IsOn);
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateNowButton.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = "Checking...";
        UpdateStatusText.Visibility = Visibility.Visible;

        try
        {
            _availableUpdate = await _updateService.CheckForUpdatesAsync();
            
            if (_availableUpdate != null)
            {
                UpdateStatusText.Text = $"v{_availableUpdate.Version} available!";
                UpdateNowButton.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateStatusText.Text = "You're up to date!";
                UpdateNowButton.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            UpdateStatusText.Text = "Check failed";
            UpdateNowButton.Visibility = Visibility.Collapsed;
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private async void UpdateNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate == null) return;

        // Disable buttons and show progress
        CheckUpdatesButton.IsEnabled = false;
        UpdateNowButton.IsEnabled = false;
        UpdateProgressPanel.Visibility = Visibility.Visible;
        UpdateProgressBar.Value = 0;
        UpdateProgressText.Text = "Starting download...";

        try
        {
            await _updateService.DownloadAndInstallAsync(_availableUpdate);
            // Note: If Velopack update, the app will restart automatically
            // If manual update, the app will exit and launch the installer
        }
        catch (Exception ex)
        {
            UpdateProgressPanel.Visibility = Visibility.Collapsed;
            UpdateStatusText.Text = $"Update failed: {ex.Message}";
            CheckUpdatesButton.IsEnabled = true;
            UpdateNowButton.IsEnabled = true;
        }
    }

    private void EnableOverlayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_settingsLoaded) return;
        _settingsService.Update(s => s.EnableOverlay = EnableOverlayToggle.IsOn);
        UpdateOverlayControlsState();

        if (!EnableOverlayToggle.IsOn)
        {
            try { App.Services.GetRequiredService<OverlayWindow>().HideOverlay(); } catch { }
        }
    }

    private void ShortcutTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _isCapturingShortcut = true;
        _activeShortcutBox = sender as TextBox;
        if (_activeShortcutBox != null) _activeShortcutBox.Text = "Press keys...";
    }

    private void ShortcutTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _isCapturingShortcut = false;
        var textBox = sender as TextBox;
        if (textBox?.Text == "Press keys...")
        {
            var settings = _settingsService.Settings;
            textBox.Text = textBox == OverlayShortcutTextBox 
                ? settings.OverlayToggleShortcut 
                : settings.OverlayDragShortcut;
        }
        _activeShortcutBox = null;
    }

    private void OverlayShortcutTextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (CaptureShortcut(e, out var shortcut))
            _settingsService.Update(s => s.OverlayToggleShortcut = shortcut);
    }

    private void DragShortcutTextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (CaptureShortcut(e, out var shortcut))
            _settingsService.Update(s => s.OverlayDragShortcut = shortcut);
    }

    private bool CaptureShortcut(KeyRoutedEventArgs e, out string shortcut)
    {
        shortcut = "";
        if (!_isCapturingShortcut || _activeShortcutBox == null) return false;

        e.Handled = true;
        var sb = new StringBuilder();

        if (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down))
            sb.Append("Ctrl+");
        if (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down))
            sb.Append("Alt+");
        if (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down))
            sb.Append("Shift+");

        if (sb.Length > 0 && !IsModifierKey(e.Key))
        {
            sb.Append(e.Key);
            shortcut = sb.ToString();
            _activeShortcutBox.Text = shortcut;
            _isCapturingShortcut = false;
            return true;
        }
        return false;
    }

    private static bool IsModifierKey(VirtualKey key) => key is 
        VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or
        VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu or
        VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift or
        VirtualKey.LeftWindows or VirtualKey.RightWindows;

    private void ResetOverlayPositionButton_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Update(s => { s.OverlayX = null; s.OverlayY = null; });
        try { App.Services.GetRequiredService<OverlayWindow>().PositionAtScreenEdge(); } catch { }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OnDownloadProgress(object? sender, int progress)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateProgressBar.Value = progress;
            UpdateProgressText.Text = $"Downloading... {progress}%";
        });
    }

    private void OnUpdateDownloaded(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateProgressText.Text = "Installing update... The app will restart.";
            UpdateProgressBar.IsIndeterminate = true;
        });
    }

    private void OnUpdateError(object? sender, string errorMessage)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateProgressPanel.Visibility = Visibility.Collapsed;
            UpdateStatusText.Text = $"Error: {errorMessage}";
            CheckUpdatesButton.IsEnabled = true;
            UpdateNowButton.IsEnabled = true;
        });
    }
}
