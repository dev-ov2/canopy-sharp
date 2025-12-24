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

    /// <summary>
    /// File description from the executable's version info
    /// </summary>
    public string? FileDescription { get; set; }

    /// <summary>
    /// Product name from the executable's version info
    /// </summary>
    public string? ProductName { get; set; }

    /// <summary>
    /// Company name from the executable's version info
    /// </summary>
    public string? CompanyName { get; set; }

    /// <summary>
    /// Command line arguments used to start the process
    /// </summary>
    public string? CommandLine { get; set; }

    /// <summary>
    /// Checks if any of the extended properties contain the search pattern (case-insensitive)
    /// </summary>
    public bool MatchesDeepSearch(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return false;

        return ContainsIgnoreCase(ProcessName, pattern) ||
               ContainsIgnoreCase(WindowTitle, pattern) ||
               ContainsIgnoreCase(FileDescription, pattern) ||
               ContainsIgnoreCase(ProductName, pattern) ||
               ContainsIgnoreCase(CompanyName, pattern) ||
               ContainsIgnoreCase(CommandLine, pattern) ||
               ContainsIgnoreCase(ExecutablePath, pattern);
    }

    private static bool ContainsIgnoreCase(string? source, string pattern)
    {
        return !string.IsNullOrEmpty(source) && 
               source.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
