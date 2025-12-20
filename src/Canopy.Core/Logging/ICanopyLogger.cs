using Microsoft.Extensions.Logging;

namespace Canopy.Core.Logging;

/// <summary>
/// Application-wide logging interface
/// </summary>
public interface ICanopyLogger
{
    void Debug(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}

/// <summary>
/// File-based logger implementation for Canopy
/// </summary>
public class FileLogger : ICanopyLogger
{
    private readonly string _logPath;
    private readonly string _source;
    private readonly object _lock = new();
    private readonly LogLevel _minLevel;

    public FileLogger(string source, string? logDirectory = null, LogLevel minLevel = LogLevel.Debug)
    {
        _source = source;
        _minLevel = minLevel;
        
        var logDir = logDirectory ?? GetDefaultLogDirectory();
        Directory.CreateDirectory(logDir);
        _logPath = Path.Combine(logDir, "canopy.log");
        
        // Rotate log if too large (> 5MB)
        RotateLogIfNeeded();
    }

    private static string GetDefaultLogDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Canopy");
        }
        else if (OperatingSystem.IsLinux())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".local", "share", "canopy");
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "Canopy");
        }
        
        return Path.Combine(Environment.CurrentDirectory, "logs");
    }

    private void RotateLogIfNeeded()
    {
        try
        {
            if (File.Exists(_logPath))
            {
                var fileInfo = new FileInfo(_logPath);
                if (fileInfo.Length > 5 * 1024 * 1024) // 5MB
                {
                    var backupPath = _logPath + ".old";
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                    File.Move(_logPath, backupPath);
                }
            }
        }
        catch
        {
            // Ignore rotation errors
        }
    }

    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Information, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message, Exception? exception = null) => 
        Log(LogLevel.Error, exception != null ? $"{message}: {exception}" : message);

    private void Log(LogLevel level, string message)
    {
        if (level < _minLevel) return;
        
        lock (_lock)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var levelStr = level switch
                {
                    LogLevel.Debug => "DBG",
                    LogLevel.Information => "INF",
                    LogLevel.Warning => "WRN",
                    LogLevel.Error => "ERR",
                    _ => "???"
                };
                
                var logLine = $"[{timestamp}] [{levelStr}] [{_source}] {message}{Environment.NewLine}";
                File.AppendAllText(_logPath, logLine);
                System.Diagnostics.Debug.WriteLine($"[{_source}] {message}");
            }
            catch
            {
                // Don't throw on log failures
            }
        }
    }
}

/// <summary>
/// Factory for creating loggers
/// </summary>
public static class CanopyLoggerFactory
{
    private static string? _logDirectory;
    
    public static void SetLogDirectory(string directory)
    {
        _logDirectory = directory;
        Directory.CreateDirectory(directory);
    }
    
    public static ICanopyLogger CreateLogger(string source)
    {
        return new FileLogger(source, _logDirectory);
    }
    
    public static ICanopyLogger CreateLogger<T>()
    {
        return CreateLogger(typeof(T).Name);
    }
}
