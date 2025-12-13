using Canopy.Core.Auth;
using Canopy.Core.GameDetection;
using Canopy.Core.Input;
using Canopy.Core.IPC;

namespace Canopy.Core.Application;

/// <summary>
/// Central application coordinator that manages cross-cutting concerns.
/// Platform-specific code subscribes to events and invokes actions.
/// </summary>
public class AppCoordinator : IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly GameService _gameService;
    private readonly TokenExchangeService _tokenExchangeService;
    private bool _isDisposed;

    /// <summary>
    /// Raised when a protocol activation is received (e.g., canopy://...)
    /// </summary>
    public event EventHandler<ProtocolActivationEventArgs>? ProtocolActivated;

    /// <summary>
    /// Raised when a token has been exchanged and is ready to send to the webview
    /// </summary>
    public event EventHandler<TokenReceivedEventArgs>? TokenReady;

    /// <summary>
    /// Raised when the overlay should be toggled
    /// </summary>
    public event EventHandler? OverlayToggleRequested;

    /// <summary>
    /// Raised when the overlay drag mode should be toggled
    /// </summary>
    public event EventHandler? OverlayDragToggleRequested;

    /// <summary>
    /// Raised when the main window should be shown
    /// </summary>
    public event EventHandler? ShowWindowRequested;

    /// <summary>
    /// Raised when the settings window should be shown
    /// </summary>
    public event EventHandler? ShowSettingsRequested;

    /// <summary>
    /// Raised when games should be rescanned
    /// </summary>
    public event EventHandler? RescanGamesRequested;

    /// <summary>
    /// Raised when the application should quit
    /// </summary>
    public event EventHandler? QuitRequested;

    public AppCoordinator(
        ISettingsService settingsService,
        GameService gameService,
        TokenExchangeService? tokenExchangeService = null)
    {
        _settingsService = settingsService;
        _gameService = gameService;
        _tokenExchangeService = tokenExchangeService ?? new TokenExchangeService();
    }

    /// <summary>
    /// Handles a protocol activation URL
    /// </summary>
    public async Task HandleProtocolActivationAsync(Uri uri)
    {
        var uriString = uri.ToString();
        System.Diagnostics.Debug.WriteLine($"Protocol activated: {uriString}");

        // Raise the event for platform-specific handling
        ProtocolActivated?.Invoke(this, new ProtocolActivationEventArgs { Uri = uri });

        // Handle auth tokens
        if (uriString.Contains("id_token=", StringComparison.OrdinalIgnoreCase))
        {
            var idToken = TokenExchangeService.ExtractIdTokenFromUrl(uriString);
            if (!string.IsNullOrEmpty(idToken))
            {
                await HandleTokenAsync(idToken);
            }
        }
    }

    /// <summary>
    /// Handles token exchange
    /// </summary>
    public async Task HandleTokenAsync(string idToken)
    {
        System.Diagnostics.Debug.WriteLine("Exchanging token...");
        
        var customToken = await _tokenExchangeService.ExchangeTokenAsync(idToken);
        
        if (!string.IsNullOrEmpty(customToken))
        {
            System.Diagnostics.Debug.WriteLine("Token exchanged successfully");
            TokenReady?.Invoke(this, new TokenReceivedEventArgs { Token = customToken });
        }
    }

    /// <summary>
    /// Called when a hotkey is pressed - routes to appropriate action
    /// </summary>
    public void HandleHotkeyPressed(string hotkeyName)
    {
        switch (hotkeyName)
        {
            case HotkeyNames.ToggleOverlay:
                if (_settingsService.Settings.EnableOverlay)
                {
                    OverlayToggleRequested?.Invoke(this, EventArgs.Empty);
                }
                break;

            case HotkeyNames.ToggleOverlayDrag:
                if (_settingsService.Settings.EnableOverlay)
                {
                    OverlayDragToggleRequested?.Invoke(this, EventArgs.Empty);
                }
                break;
        }
    }

    /// <summary>
    /// Requests the main window to be shown
    /// </summary>
    public void RequestShowWindow()
    {
        ShowWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Requests the settings window to be shown
    /// </summary>
    public void RequestShowSettings()
    {
        ShowSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Requests a game rescan
    /// </summary>
    public async Task RequestRescanGamesAsync()
    {
        RescanGamesRequested?.Invoke(this, EventArgs.Empty);
        await _gameService.RescanGamesAsync();
    }

    /// <summary>
    /// Requests application quit
    /// </summary>
    public void RequestQuit()
    {
        QuitRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Event args for protocol activation
/// </summary>
public class ProtocolActivationEventArgs : EventArgs
{
    public required Uri Uri { get; init; }
}

/// <summary>
/// Event args for token received
/// </summary>
public class TokenReceivedEventArgs : EventArgs
{
    public required string Token { get; init; }
}
