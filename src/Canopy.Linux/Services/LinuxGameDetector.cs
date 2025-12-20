using System.Diagnostics;
using Canopy.Core.GameDetection;
using Canopy.Core.Logging;
using Canopy.Core.Models;

namespace Canopy.Linux.Services;

/// <summary>
/// Linux implementation for detecting running game processes using /proc filesystem
/// </summary>
public class LinuxGameDetector : IGameDetector
{
    private readonly ICanopyLogger _logger;
    private readonly Timer _pollTimer;
    private readonly HashSet<string> _runningGameIds = new();
    private IReadOnlyList<DetectedGame> _monitoredGames = Array.Empty<DetectedGame>();
    private bool _isMonitoring;

    public event EventHandler<DetectedGame>? GameStarted;
    public event EventHandler<DetectedGame>? GameStopped;

    public LinuxGameDetector()
    {
        _logger = CanopyLoggerFactory.CreateLogger<LinuxGameDetector>();
        _pollTimer = new Timer(PollRunningGames, null, Timeout.Infinite, Timeout.Infinite);
    }

    public IReadOnlyList<RunningProcess> GetRunningProcesses()
    {
        var processes = new List<RunningProcess>();

        try
        {
            var procDir = new DirectoryInfo("/proc");
            foreach (var dir in procDir.GetDirectories())
            {
                if (!int.TryParse(dir.Name, out var pid))
                    continue;

                try
                {
                    var process = GetProcessInfo(pid);
                    if (process != null)
                        processes.Add(process);
                }
                catch
                {   
                    // Process may have exited
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to enumerate processes", ex);
        }

        return processes.AsReadOnly();
    }

    private RunningProcess? GetProcessInfo(int pid)
    {
        try
        {
            var exePath = GetProcessPath(pid);
            if (string.IsNullOrEmpty(exePath))
                return null;

            var processName = Path.GetFileNameWithoutExtension(exePath);
            
            // Try to get command line for window title
            var cmdline = GetProcessCmdline(pid);
            
            return new RunningProcess
            {
                ProcessId = pid,
                ProcessName = processName,
                ExecutablePath = exePath,
                WindowTitle = cmdline,
                StartTime = GetProcessStartTime(pid)
            };
        }
        catch(Exception ex)
        {
            _logger.Error("Received error while getting process info!", ex);
            return null;
        }
    }

    private static string? GetProcessPath(int pid)
    {
        try
        {
            var exeLink = $"/proc/{pid}/exe";
            if (File.Exists(exeLink))
            {
                var target = Path.GetFullPath(exeLink);
                // ReadLink to get actual path
                var fi = new FileInfo(exeLink);
                if (fi.LinkTarget != null)
                    return fi.LinkTarget;
                
                // Fallback using realpath
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "readlink",
                    Arguments = $"-f {exeLink}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });
                
                if (process != null)
                {
                    var result = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    if (!string.IsNullOrEmpty(result) && !result.Contains("(deleted)"))
                        return result;
                }
            }
        }
        catch
        {
            // Process may not be accessible
        }

        return null;
    }

    private static string? GetProcessCmdline(int pid)
    {
        try
        {
            var cmdlinePath = $"/proc/{pid}/cmdline";
            if (File.Exists(cmdlinePath))
            {
                var content = File.ReadAllText(cmdlinePath);
                return content.Replace('\0', ' ').Trim();
            }
        }
        catch
        {
            // May not have permission
        }

        return null;
    }

    private static DateTime GetProcessStartTime(int pid)
    {
        try
        {
            var statPath = $"/proc/{pid}/stat";
            if (File.Exists(statPath))
            {
                var stat = File.ReadAllText(statPath);
                var parts = stat.Split(' ');
                if (parts.Length > 21)
                {
                    // Field 22 is start time in clock ticks since boot
                    if (long.TryParse(parts[21], out var startTicks))
                    {
                        // Get system uptime
                        var uptimePath = "/proc/uptime";
                        if (File.Exists(uptimePath))
                        {
                            var uptime = File.ReadAllText(uptimePath).Split(' ')[0];
                            if (double.TryParse(uptime, out var uptimeSeconds))
                            {
                                var ticksPerSecond = 100; // Usually 100 Hz on Linux
                                var processUptime = startTicks / ticksPerSecond;
                                return DateTime.Now.AddSeconds(-(uptimeSeconds - processUptime));
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }

        return DateTime.MinValue;
    }

    public bool IsGameRunning(DetectedGame game)
    {
        // Try matching by InstallPath
        if (!string.IsNullOrEmpty(game.InstallPath))
        {
            if (IsProcessRunningFromPath(game.InstallPath))
                return true;
        }

        // Fallback to ExecutablePath
        if (!string.IsNullOrEmpty(game.ExecutablePath))
        {
            var processes = GetRunningProcesses();
            return processes.Any(p => 
                string.Equals(p.ExecutablePath, game.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private bool IsProcessRunningFromPath(string installPath)
    {
        try
        {
            var normalizedPath = NormalizePath(installPath);
            var processes = GetRunningProcesses();

            foreach (var process in processes)
            {
                if (string.IsNullOrEmpty(process.ExecutablePath))
                    continue;

                if (IsPathWithinDirectory(process.ExecutablePath, normalizedPath) && 
                    IsNotIgnored(process.ExecutablePath))
                    return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Error checking running processes", ex);
        }

        return false;
    }

    public RunningProcess? GetRunningGameProcess(DetectedGame game)
    {
        if (!string.IsNullOrEmpty(game.InstallPath))
        {
            var normalizedPath = NormalizePath(game.InstallPath);
            var processes = GetRunningProcesses();

            foreach (var process in processes)
            {
                if (string.IsNullOrEmpty(process.ExecutablePath))
                    continue;

                if (IsPathWithinDirectory(process.ExecutablePath, normalizedPath))
                    return process;
            }
        }

        if (!string.IsNullOrEmpty(game.ExecutablePath))
        {
            var processes = GetRunningProcesses();
            return processes.FirstOrDefault(p =>
                string.Equals(p.ExecutablePath, game.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    public void StartMonitoring(IEnumerable<DetectedGame> games)
    {
        _monitoredGames = games.ToList().AsReadOnly();
        _runningGameIds.Clear();
        
        _logger.Info($"Starting game monitoring for {_monitoredGames.Count} games");
        
        _isMonitoring = true;
        _pollTimer.Change(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    public void StopMonitoring()
    {
        _isMonitoring = false;
        _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _logger.Info("Stopped game monitoring");
    }

    private void PollRunningGames(object? state)
    {
        if (!_isMonitoring) return;

        try
        {
            var runningProcesses = GetRunningProcesses();

            foreach (var game in _monitoredGames)
            {
                var isRunning = IsGameRunningCached(game, runningProcesses);

                var wasRunning = _runningGameIds.Contains(game.Id);

                if (isRunning && !wasRunning)
                {
                    _logger.Info($"Game started: {game.Name}");
                    _runningGameIds.Add(game.Id);
                    game.IsRunning = true;
                    GameStarted?.Invoke(this, game);
                }
                else if (!isRunning && wasRunning)
                {
                    _logger.Info($"Game stopped: {game.Name}");
                    _runningGameIds.Remove(game.Id);
                    game.IsRunning = false;
                    GameStopped?.Invoke(this, game);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Error polling running games", ex);
        }
    }

    private bool IsGameRunningCached(DetectedGame game, IReadOnlyList<RunningProcess> processes)
    {
        if (!string.IsNullOrEmpty(game.InstallPath))
        {
            var normalizedPath = NormalizePath(game.InstallPath);
            foreach (var process in processes)
            {
                if (process.WindowTitle.Contains(normalizedPath)) {
                    return true;
                }
                if (!string.IsNullOrEmpty(process.ExecutablePath) &&
                    IsPathWithinDirectory(process.ExecutablePath, normalizedPath) &&
                    IsNotIgnored(process.ExecutablePath))
                    return true;
            }
        }


        if (!string.IsNullOrEmpty(game.ExecutablePath))
        {
            foreach (var process in processes)
            {
                if (string.Equals(process.ExecutablePath, game.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
    }

    private static bool IsPathWithinDirectory(string filePath, string directoryPath)
    {
        var normalizedFilePath = Path.GetFullPath(filePath);
        return normalizedFilePath.StartsWith(directoryPath + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsNotIgnored(string filePath)
    {
        foreach (var element in SteamScanner.IgnoredPathElements)
        {
            if (filePath.Contains(element, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
}
