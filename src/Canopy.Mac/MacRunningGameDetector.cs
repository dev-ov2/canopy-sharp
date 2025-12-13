using Canopy.Core.GameDetection;
using Canopy.Core.Models;

namespace Canopy.Mac;

/// <summary>
/// macOS stub - to be implemented
/// Uses NSRunningApplication for process detection
/// </summary>
public class MacRunningGameDetector : IGameDetector
{
#pragma warning disable CS0067 // Events required by interface but not used in stub
    public event EventHandler<DetectedGame>? GameStarted;
    public event EventHandler<DetectedGame>? GameStopped;
#pragma warning restore CS0067

    public IReadOnlyList<RunningProcess> GetRunningProcesses()
    {
        // TODO: Implement using NSWorkspace.SharedWorkspace.RunningApplications
        // Or use Process.GetProcesses() which works on macOS too
        throw new PlatformNotSupportedException("macOS process detection not yet implemented");
    }

    public RunningProcess? GetRunningGameProcess(DetectedGame game)
    {
        throw new PlatformNotSupportedException("macOS support not yet implemented");
    }

    public bool IsGameRunning(DetectedGame game)
    {
        throw new PlatformNotSupportedException("macOS support not yet implemented");
    }

    public void StartMonitoring(IEnumerable<DetectedGame> games)
    {
        throw new PlatformNotSupportedException("macOS support not yet implemented");
    }

    public void StopMonitoring()
    {
        // No-op for stub
    }
}
