using Canopy.Core.Models;

namespace Canopy.Core.GameDetection;

/// <summary>
/// Interface for detecting running game processes
/// </summary>
public interface IGameDetector
{
    /// <summary>
    /// Gets all currently running processes
    /// </summary>
    IReadOnlyList<RunningProcess> GetRunningProcesses();

    /// <summary>
    /// Checks if a specific game is currently running
    /// </summary>
    bool IsGameRunning(DetectedGame game);

    /// <summary>
    /// Gets the running process for a game if it's running
    /// </summary>
    RunningProcess? GetRunningGameProcess(DetectedGame game);

    /// <summary>
    /// Event raised when a game starts
    /// </summary>
    event EventHandler<DetectedGame>? GameStarted;

    /// <summary>
    /// Event raised when a game stops
    /// </summary>
    event EventHandler<DetectedGame>? GameStopped;

    /// <summary>
    /// Starts monitoring for game processes
    /// </summary>
    void StartMonitoring(IEnumerable<DetectedGame> games);

    /// <summary>
    /// Stops monitoring for game processes
    /// </summary>
    void StopMonitoring();
}
