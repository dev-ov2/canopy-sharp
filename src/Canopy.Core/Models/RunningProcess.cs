namespace Canopy.Core.Models;

/// <summary>
/// Represents a running process that may be a game
/// </summary>
public class RunningProcess
{
    public required int ProcessId { get; set; }
    public required string ProcessName { get; set; }
    public string? ExecutablePath { get; set; }
    public string? WindowTitle { get; set; }
    public DateTime StartTime { get; set; }
}
