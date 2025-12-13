using Canopy.Core.Models;

namespace Canopy.Core.GameDetection;

/// <summary>
/// Interface for platform-specific game scanning
/// </summary>
public interface IGameScanner
{
    /// <summary>
    /// Gets the platform this scanner handles
    /// </summary>
    GamePlatform Platform { get; }

    /// <summary>
    /// Whether this scanner is available on the current OS
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Detects all installed games for this platform
    /// </summary>
    Task<IReadOnlyList<DetectedGame>> DetectGamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the installation path for this platform's launcher
    /// </summary>
    string? GetInstallPath();
}
