using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Canopy.Core.GameDetection;
using Canopy.Core.Logging;
using Canopy.Core.Models;

namespace Canopy.Windows.Services;

/// <summary>
/// Windows implementation for detecting running game processes
/// </summary>
public class GameDetector : IGameDetector
{
    private readonly ICanopyLogger _logger;
    private readonly Timer _pollTimer;
    private readonly HashSet<string> _runningGameIds = new();
    private IReadOnlyList<DetectedGame> _monitoredGames = Array.Empty<DetectedGame>();
    private bool _isMonitoring;
    private bool _hasDeepSearchGames;

    // Win32 constants
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // P/Invoke declarations
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, char[] lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public event EventHandler<DetectedGame>? GameStarted;
    public event EventHandler<DetectedGame>? GameStopped;

    public GameDetector()
    {
        _logger = CanopyLoggerFactory.CreateLogger<GameDetector>();
        _pollTimer = new Timer(PollRunningGames, null, Timeout.Infinite, Timeout.Infinite);
    }

    public IReadOnlyList<RunningProcess> GetRunningProcesses()
    {
        return GetRunningProcessesInternal(includeExtendedInfo: false);
    }

    /// <summary>
    /// Gets running processes with optional extended info for deep search
    /// </summary>
    private IReadOnlyList<RunningProcess> GetRunningProcessesInternal(bool includeExtendedInfo)
    {
        var processes = new List<RunningProcess>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                // Skip session 0 (system) processes
                if (process.SessionId == 0)
                {
                    process.Dispose();
                    continue;
                }

                var exePath = GetProcessPath(process.Id);
                var runningProcess = new RunningProcess
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName,
                    ExecutablePath = exePath,
                    WindowTitle = GetWindowTitleSafe(process),
                    StartTime = GetStartTimeSafe(process)
                };

                // Only gather extended info if needed (it's more expensive)
                if (includeExtendedInfo && !string.IsNullOrEmpty(exePath))
                {
                    PopulateExtendedInfo(runningProcess, process, exePath);
                }

                processes.Add(runningProcess);
            }
            catch
            {
                // Process may have exited or we don't have access
            }
            finally
            {
                process.Dispose();
            }
        }

        return processes.AsReadOnly();
    }

    /// <summary>
    /// Populates extended process info from file version info
    /// </summary>
    private void PopulateExtendedInfo(RunningProcess runningProcess, Process process, string exePath)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
            runningProcess.FileDescription = versionInfo.FileDescription;
            runningProcess.ProductName = versionInfo.ProductName;
            runningProcess.CompanyName = versionInfo.CompanyName;
        }
        catch
        {
            // File version info may not be accessible
        }

        // Use the executable path as a fallback for command line search
        // Full command line retrieval requires WMI or elevated access
        runningProcess.CommandLine = exePath;
    }

    public bool IsGameRunning(DetectedGame game)
    {
        // Try deep search first (if configured)
        if (game.DeepSearchPatterns?.Length > 0)
        {
            if (IsProcessRunningByDeepSearch(game.DeepSearchPatterns))
                return true;
        }

        // Try matching by process names (for remote mappings)
        if (game.ProcessNames?.Length > 0)
        {
            if (IsProcessRunningByName(game.ProcessNames))
                return true;
        }

        // Try matching by InstallPath (any exe running from the game's install directory)
        if (!string.IsNullOrEmpty(game.InstallPath))
        {
            if (IsProcessRunningFromPath(game.InstallPath))
                return true;
        }

        // Fallback to exact ExecutablePath match
        if (!string.IsNullOrEmpty(game.ExecutablePath))
        {
            var exeName = Path.GetFileNameWithoutExtension(game.ExecutablePath);

            try
            {
                var processes = Process.GetProcessesByName(exeName);
                var result = processes.Any(p =>
                {
                    try
                    {
                        // Skip session 0 processes
                        if (p.SessionId == 0)
                            return false;

                        var path = GetProcessPath(p.Id);
                        return string.Equals(path, game.ExecutablePath, StringComparison.OrdinalIgnoreCase);
                    }
                    finally
                    {
                        p.Dispose();
                    }
                });
                return result;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if any process matches the deep search patterns
    /// </summary>
    private bool IsProcessRunningByDeepSearch(string[] patterns)
    {
        var processes = GetRunningProcessesInternal(includeExtendedInfo: true);
        
        foreach (var process in processes)
        {
            foreach (var pattern in patterns)
            {
                if (process.MatchesDeepSearch(pattern))
                {
                    _logger.Debug($"Deep search matched: pattern='{pattern}' on process={process.ProcessName}");
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if any process with the given names is running
    /// </summary>
    private bool IsProcessRunningByName(string[] processNames)
    {
        foreach (var processName in processNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                if (processes.Any(p =>
                {
                    try { return p.SessionId != 0; }
                    finally { p.Dispose(); }
                }))
                {
                    return true;
                }
            }
            catch
            {
                // Process enumeration may fail
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if any process is running from within the specified install path
    /// </summary>
    private bool IsProcessRunningFromPath(string installPath, IReadOnlyList<(int Pid, string? Path)>? cachedProcesses = null)
    {
        try
        {
            var normalizedInstallPath = NormalizePath(installPath);
            var processes = cachedProcesses ?? GetAllProcessPaths();

            foreach (var (pid, processPath) in processes)
            {
                if (string.IsNullOrEmpty(processPath))
                    continue;

                if (IsPathWithinDirectory(processPath, normalizedInstallPath))
                    return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Error in IsProcessRunningFromPath", ex);
        }

        return false;
    }

    /// <summary>
    /// Gets all process paths once, for efficient batch checking.
    /// </summary>
    private IReadOnlyList<(int Pid, string? Path)> GetAllProcessPaths()
    {
        var result = new List<(int, string?)>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                // Skip session 0 (system) processes
                if (process.SessionId == 0)
                    continue;

                var path = GetProcessPath(process.Id);
                if (!string.IsNullOrEmpty(path))
                {
                    result.Add((process.Id, path));
                }
            }
            catch
            {
                // Process may have exited
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    public RunningProcess? GetRunningGameProcess(DetectedGame game)
    {
        // First try matching by InstallPath
        if (!string.IsNullOrEmpty(game.InstallPath))
        {
            var runningProcess = GetProcessRunningFromPath(game.InstallPath);
            if (runningProcess != null)
                return runningProcess;
        }

        // Fallback to exact ExecutablePath match
        if (!string.IsNullOrEmpty(game.ExecutablePath))
        {
            var exeName = Path.GetFileNameWithoutExtension(game.ExecutablePath);

            try
            {
                var processes = Process.GetProcessesByName(exeName);
                foreach (var process in processes)
                {
                    try
                    {
                        // Skip session 0 processes
                        if (process.SessionId == 0)
                            continue;

                        var path = GetProcessPath(process.Id);
                        if (string.Equals(path, game.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                        {
                            return new RunningProcess
                            {
                                ProcessId = process.Id,
                                ProcessName = process.ProcessName,
                                ExecutablePath = path,
                                WindowTitle = GetWindowTitleSafe(process),
                                StartTime = GetStartTimeSafe(process)
                            };
                        }
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                // Failed to get processes
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the first process running from within the specified install path
    /// </summary>
    private RunningProcess? GetProcessRunningFromPath(string installPath)
    {
        try
        {
            var normalizedInstallPath = NormalizePath(installPath);

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    // Skip session 0 (system) processes
                    if (process.SessionId == 0)
                        continue;

                    var processPath = GetProcessPath(process.Id);
                    if (string.IsNullOrEmpty(processPath))
                        continue;

                    if (IsPathWithinDirectory(processPath, normalizedInstallPath))
                    {
                        return new RunningProcess
                        {
                            ProcessId = process.Id,
                            ProcessName = process.ProcessName,
                            ExecutablePath = processPath,
                            WindowTitle = GetWindowTitleSafe(process),
                            StartTime = GetStartTimeSafe(process)
                        };
                    }
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // Failed to enumerate processes
        }

        return null;
    }

    public void StartMonitoring(IEnumerable<DetectedGame> games)
    {
        _monitoredGames = games.ToList().AsReadOnly();
        _runningGameIds.Clear();
        
        // Check if any games use deep search (affects performance)
        _hasDeepSearchGames = _monitoredGames.Any(g => g.DeepSearchPatterns?.Length > 0);
        if (_hasDeepSearchGames)
        {
            _logger.Debug("Deep search games detected - extended process info will be collected");
        }

        _logger.Info($"Starting game monitoring for {_monitoredGames.Count} games");

        // Get all process paths once for efficient checking
        var allProcesses = GetAllProcessPaths();
        _logger.Debug($"Found {allProcesses.Count} user processes");

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
            // Get processes - include extended info only if needed
            IReadOnlyList<RunningProcess>? extendedProcesses = null;
            if (_hasDeepSearchGames)
            {
                extendedProcesses = GetRunningProcessesInternal(includeExtendedInfo: true);
            }

            // Get all process paths once per poll cycle for path-based detection
            var allProcessPaths = GetAllProcessPaths();

            foreach (var game in _monitoredGames)
            {
                var isRunning = IsGameRunningCached(game, allProcessPaths, extendedProcesses);
                var wasRunning = _runningGameIds.Contains(game.Id);

                if (isRunning && !wasRunning)
                {
                    _logger.Info($"Game STARTED: {game.Name}");
                    _runningGameIds.Add(game.Id);
                    game.IsRunning = true;
                    GameStarted?.Invoke(this, game);
                }
                else if (!isRunning && wasRunning)
                {
                    _logger.Info($"Game STOPPED: {game.Name}");
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

    /// <summary>
    /// Checks if a game is running using cached process lists.
    /// </summary>
    private bool IsGameRunningCached(
        DetectedGame game, 
        IReadOnlyList<(int Pid, string? Path)> cachedProcessPaths,
        IReadOnlyList<RunningProcess>? extendedProcesses)
    {
        // Try deep search first (if configured and we have extended process info)
        if (game.DeepSearchPatterns?.Length > 0 && extendedProcesses != null)
        {
            foreach (var process in extendedProcesses)
            {
                foreach (var pattern in game.DeepSearchPatterns)
                {
                    if (process.MatchesDeepSearch(pattern))
                        return true;
                }
            }
        }

        // Try matching by process names (for remote mappings)
        if (game.ProcessNames?.Length > 0)
        {
            if (IsProcessRunningByName(game.ProcessNames))
                return true;
        }

        // Try matching by InstallPath
        if (!string.IsNullOrEmpty(game.InstallPath))
        {
            var normalizedInstallPath = NormalizePath(game.InstallPath);
            foreach (var (_, processPath) in cachedProcessPaths)
            {
                if (!string.IsNullOrEmpty(processPath) && IsPathWithinDirectory(processPath, normalizedInstallPath) && IsNotIgnored(processPath))
                    return true;
            }
        }

        // Fallback to exact ExecutablePath match
        if (!string.IsNullOrEmpty(game.ExecutablePath))
        {
            foreach (var (_, processPath) in cachedProcessPaths)
            {
                if (string.Equals(processPath, game.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the full image path of a process using QueryFullProcessImageNameW.
    /// This works with PROCESS_QUERY_LIMITED_INFORMATION and is more reliable than MainModule.
    /// </summary>
    private static string? GetProcessPath(int processId)
    {
        IntPtr hProcess = IntPtr.Zero;
        try
        {
            hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
            if (hProcess == IntPtr.Zero)
                return null;

            var buffer = new char[1024];
            uint size = (uint)buffer.Length;

            if (QueryFullProcessImageNameW(hProcess, 0, buffer, ref size))
            {
                return new string(buffer, 0, (int)size);
            }

            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hProcess != IntPtr.Zero)
                CloseHandle(hProcess);
        }
    }

    private static string GetWindowTitleSafe(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static DateTime GetStartTimeSafe(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsPathWithinDirectory(string filePath, string directoryPath)
    {
        var normalizedFilePath = Path.GetFullPath(filePath);
        return normalizedFilePath.StartsWith(directoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalizedFilePath.StartsWith(directoryPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNotIgnored(string filePath)
    {
        foreach (var element in SteamScanner.IgnoredPathElements)
        {
            if (filePath.Contains(element, StringComparison.CurrentCultureIgnoreCase)) return false;
        }

        return true;
    }
}
