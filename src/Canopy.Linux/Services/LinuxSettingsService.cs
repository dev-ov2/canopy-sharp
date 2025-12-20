using Canopy.Core.Application;
using Canopy.Core.Logging;
using System.Text.Json;

namespace Canopy.Linux.Services;

/// <summary>
/// Linux-specific settings service implementation
/// </summary>
public class LinuxSettingsService : SettingsServiceBase
{
    private static readonly ICanopyLogger _logger = CanopyLoggerFactory.CreateLogger<LinuxSettingsService>();
    private readonly string _settingsPath;

    public LinuxSettingsService()
    {
        _settingsPath = GetSettingsFilePath();
        _logger.Info($"Settings file: {_settingsPath}");
        _logger.Debug($"Initial settings: StartWithWindows={Settings.StartWithWindows}, AutoUpdate={Settings.AutoUpdate}, EnableOverlay={Settings.EnableOverlay}");
    }

    protected override string GetSettingsFilePath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configPath = Path.Combine(home, ".config", "canopy");
        
        return Path.Combine(configPath, "settings.json");
    }

    protected override void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    protected override void OnSettingsSaved()
    {
        _logger.Debug($"Settings saved: StartWithWindows={Settings.StartWithWindows}, AutoUpdate={Settings.AutoUpdate}, EnableOverlay={Settings.EnableOverlay}");
    }
}
