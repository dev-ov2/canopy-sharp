using Canopy.Core.Logging;
using Canopy.Core.Models;
using System.Diagnostics;
using System.Text.Json;

namespace Canopy.Core.GameDetection;

/// <summary>
/// Aggregates game information across all platforms
/// </summary>
public class GameService
{
    private readonly ICanopyLogger _logger;
    private readonly IEnumerable<IGameScanner> _scanners;
    private readonly IGameDetector _gameDetector;
    private List<DetectedGame> _cachedGames = [];

    public event EventHandler<IReadOnlyList<DetectedGame>>? GamesDetected;
    public event EventHandler<GameStatePayload>? GameStarted;
    public event EventHandler<GameStatePayload>? GameStopped;

    public IReadOnlyList<DetectedGame> CachedGames => _cachedGames.AsReadOnly();

    public GameService(
        IEnumerable<IGameScanner> scanners, 
        IGameDetector runningGameDetector)
    {
        _logger = CanopyLoggerFactory.CreateLogger<GameService>();
        _scanners = scanners;
        _gameDetector = runningGameDetector;

        _gameDetector.GameStarted += (_, game) =>
        {
            _logger.Info($"Game started: {game.Name} ({game.Platform})");
            GameStarted?.Invoke(this, new GameStatePayload
            {
                State = GameState.Started.ToString().ToLowerInvariant(),
                Source = game.Platform.ToString().ToLowerInvariant(),
                AppId = game.Id,
                Name = game.Name
            });
        };
        
        _gameDetector.GameStopped += (_, game) =>
        {
            _logger.Info($"Game stopped: {game.Name} ({game.Platform})");
            GameStopped?.Invoke(this, new GameStatePayload
            {
                State = GameState.Stopped.ToString().ToLowerInvariant(),
                Source = game.Platform.ToString().ToLowerInvariant(),
                AppId = game.Id,
                Name = game.Name
            });
        };
    }

    /// <summary>
    /// Scans all available platforms for installed games
    /// </summary>
    public async Task<IReadOnlyList<DetectedGame>> ScanAllGamesAsync(CancellationToken cancellationToken = default)
    {
        var allGames = new List<DetectedGame>();
        var availableScanners = _scanners.Where(d => d.IsAvailable).ToList();
        
        _logger.Info($"Scanning games with {availableScanners.Count} available scanners");

        var detectionTasks = availableScanners
            .Select(async detector =>
            {
                try
                {
                    var games = await detector.DetectGamesAsync(cancellationToken);
                    _logger.Debug($"{detector.Platform}: Found {games.Count} games");
                    return games;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error scanning {detector.Platform}", ex);
                    return Array.Empty<DetectedGame>();
                }
            });

        var results = await Task.WhenAll(detectionTasks);

        foreach (var games in results)
        {
            allGames.AddRange(games);
        }

        // Update running status
        foreach (var game in allGames)
        {
            // Detect any currently running game (on startup)
            game.IsRunning = _gameDetector.IsGameRunning(game);
        }

        _cachedGames = allGames;
        _gameDetector.StartMonitoring(allGames);
        
        _logger.Info($"Game scan complete: {allGames.Count} total games detected");
        GamesDetected?.Invoke(this, allGames.AsReadOnly());
        return allGames.AsReadOnly();
    }

    /// <summary>
    /// Rescans games from all platforms
    /// </summary>
    public Task<IReadOnlyList<DetectedGame>> RescanGamesAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info("Rescanning games...");
        _gameDetector.StopMonitoring();
        _cachedGames.Clear();
        return ScanAllGamesAsync(cancellationToken);
    }
}
