using Canopy.Core.Application;
using Canopy.Core.Logging;

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
        _logger.Info($"Settings path: {_settingsPath}");
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
            _logger.Debug($"Created settings directory: {path}");
        }
    }
}
