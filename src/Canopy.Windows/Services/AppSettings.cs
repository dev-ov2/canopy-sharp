using Canopy.Core.Application;

namespace Canopy.Windows.Services;

/// <summary>
/// Windows-specific settings service implementation
/// </summary>
public class SettingsService : SettingsServiceBase
{
    protected override string GetSettingsFilePath()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Canopy");
        
        return Path.Combine(appDataPath, "settings.json");
    }
}
