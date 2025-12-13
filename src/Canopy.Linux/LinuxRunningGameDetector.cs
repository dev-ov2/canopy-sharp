using Canopy.Core.GameDetection;
using Canopy.Core.Models;

namespace Canopy.Linux;

/// <summary>
/// Linux stub - to be implemented.
/// Uses /proc filesystem for process detection.
/// </summary>
public class LinuxRunningGameDetector : IGameDetector
{
#pragma warning disable CS0067 // Events required by interface but not used in stub
    public event EventHandler<DetectedGame>? GameStarted;
    public event EventHandler<DetectedGame>? GameStopped;
#pragma warning restore CS0067

    public IReadOnlyList<RunningProcess> GetRunningProcesses()
    {
        // TODO: Implement using /proc filesystem
        throw new PlatformNotSupportedException("Linux process detection not yet implemented");
    }

    public RunningProcess? GetRunningGameProcess(DetectedGame game)
    {
        throw new PlatformNotSupportedException("Linux support not yet implemented");
    }

    public bool IsGameRunning(DetectedGame game)
    {
        throw new PlatformNotSupportedException("Linux support not yet implemented");
    }

    public void StartMonitoring(IEnumerable<DetectedGame> games)
    {
        throw new PlatformNotSupportedException("Linux support not yet implemented");
    }

    public void StopMonitoring()
    {
        // No-op for stub
    }
}
