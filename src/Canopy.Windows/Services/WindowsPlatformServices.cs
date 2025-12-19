using System.Diagnostics;
using Canopy.Core.Platform;
using Microsoft.Win32;

namespace Canopy.Windows.Services;

/// <summary>
/// Windows-specific platform services implementation.
/// Consolidates startup registration, protocol registration, and other platform services.
/// </summary>
public class WindowsPlatformServices : IPlatformServices
{
    private const string AppName = "Canopy";
    private const string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    #region Auto-Start (IPlatformServices)

    public Task SetAutoStartAsync(bool enabled, bool startOpen)
    {
        SetStartupRegistration(enabled, startOpen);
        return Task.CompletedTask;
    }

    public Task<bool> IsAutoStartEnabledAsync()
    {
        return Task.FromResult(IsRegisteredForStartup());
    }

    #endregion

    #region Startup Registration

    /// <summary>
    /// Gets whether the app is registered to start with Windows
    /// </summary>
    public bool IsRegisteredForStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to check startup registration: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Registers or unregisters the app to start with Windows
    /// </summary>
    public bool SetStartupRegistration(bool enable, bool startOpen)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true);
            if (key == null) return false;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath)) return false;

                if (!startOpen)
                {
                    exePath += " --minimized";
                }
                key.SetValue(AppName, $"\"{exePath}\"");
                Debug.WriteLine($"Registered for startup: {exePath}");
            }
            else
            {
                key.DeleteValue(AppName, false);
                Debug.WriteLine("Unregistered from startup");
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set startup registration: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Protocol Registration

    /// <summary>
    /// Registers a custom URL protocol handler (e.g., canopy://)
    /// </summary>
    public static void RegisterProtocol(string protocol, string exePath)
    {
        try
        {
            string keyPath = $@"Software\Classes\{protocol}";
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            if (key == null) return;

            key.SetValue(string.Empty, $"URL:{protocol} Protocol");
            key.SetValue("URL Protocol", string.Empty);

            using (var defaultIcon = key.CreateSubKey("DefaultIcon"))
            {
                defaultIcon?.SetValue(string.Empty, exePath + ",1");
            }

            using (var shellKey = key.CreateSubKey("shell\\open\\command"))
            {
                shellKey?.SetValue(string.Empty, $"\"{exePath}\" \"%1\"");
            }

            Debug.WriteLine($"Registered protocol handler: {protocol}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to register protocol: {ex.Message}");
        }
    }

    #endregion

    #region URL/Path Opening

    public void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open URL: {ex.Message}");
        }
    }

    public void OpenPath(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open path: {ex.Message}");
        }
    }

    #endregion

    #region Screen Info

    public (int Width, int Height) GetPrimaryScreenSize()
    {
        return Interop.NativeMethods.GetPrimaryScreenSize();
    }

    public IReadOnlyList<ScreenInfo> GetAllScreens()
    {
        var (width, height) = Interop.NativeMethods.GetPrimaryScreenSize();
        return new List<ScreenInfo>
        {
            new()
            {
                DeviceName = "Primary",
                X = 0,
                Y = 0,
                Width = width,
                Height = height,
                IsPrimary = true,
                ScaleFactor = 1.0
            }
        }.AsReadOnly();
    }

    #endregion
}
