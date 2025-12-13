using System.Text.Json;

namespace Canopy.Core.Application;

/// <summary>
/// Interface for platform-specific settings persistence
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Current application settings
    /// </summary>
    AppSettings Settings { get; }

    /// <summary>
    /// Saves the current settings
    /// </summary>
    void Save();

    /// <summary>
    /// Updates settings and triggers save + change notification
    /// </summary>
    void Update(Action<AppSettings> updateAction);

    /// <summary>
    /// Event raised when settings are changed
    /// </summary>
    event EventHandler<AppSettings>? SettingsChanged;
}

/// <summary>
/// Base implementation for settings service with JSON persistence
/// </summary>
public abstract class SettingsServiceBase : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly AppSettings _settings;
    private readonly object _lock = new();

    public event EventHandler<AppSettings>? SettingsChanged;

    public AppSettings Settings
    {
        get
        {
            lock (_lock)
            {
                return _settings;
            }
        }
    }

    protected SettingsServiceBase()
    {
        _settingsPath = GetSettingsFilePath();
        EnsureDirectoryExists(Path.GetDirectoryName(_settingsPath)!);
        _settings = Load();
    }

    /// <summary>
    /// Gets the platform-specific settings file path
    /// </summary>
    protected abstract string GetSettingsFilePath();

    /// <summary>
    /// Ensures the directory exists
    /// </summary>
    protected virtual void EnsureDirectoryExists(string path)
    {
        Directory.CreateDirectory(path);
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }

        return new AppSettings();
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(_settings, JsonOptions);
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
    }

    public void Update(Action<AppSettings> updateAction)
    {
        lock (_lock)
        {
            updateAction(_settings);
            Save();
            SettingsChanged?.Invoke(this, _settings);
        }
    }
}
