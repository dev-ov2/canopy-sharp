namespace Canopy.Core.Platform;

/// <summary>
/// Platform detection and abstraction
/// </summary>
public static class PlatformInfo
{
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsLinux => OperatingSystem.IsLinux();
    public static bool IsMacOS => OperatingSystem.IsMacOS();

    public static string PlatformName
    {
        get
        {
            if (IsWindows) return "Windows";
            if (IsLinux) return "Linux";
            if (IsMacOS) return "macOS";
            return "Unknown";
        }
    }

    public static string GetAppDataPath()
    {
        if (IsWindows)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Canopy");
        }
        else if (IsMacOS)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "Canopy");
        }
        else // Linux
        {
            var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrEmpty(xdgData))
            {
                return Path.Combine(xdgData, "canopy");
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share", "canopy");
        }
    }

    public static string GetConfigPath()
    {
        if (IsWindows)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Canopy");
        }
        else if (IsMacOS)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Preferences", "Canopy");
        }
        else // Linux
        {
            var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrEmpty(xdgConfig))
            {
                return Path.Combine(xdgConfig, "canopy");
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "canopy");
        }
    }
}
