using Canopy.Core.Application;

namespace Canopy.Linux.Services;

/// <summary>
/// Linux-specific settings service implementation
/// </summary>
public class LinuxSettingsService : SettingsServiceBase
{
    protected override string GetSettingsFilePath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configPath = Path.Combine(home, ".config", "canopy");
        
        return Path.Combine(configPath, "settings.json");
    }
}
