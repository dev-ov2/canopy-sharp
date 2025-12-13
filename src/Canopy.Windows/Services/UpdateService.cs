using Canopy.Core.Application;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Velopack;
using Velopack.Sources;

namespace Canopy.Windows.Services;

/// <summary>
/// Service to check for and apply updates using Velopack
/// Falls back to GitHub releases API for update information
/// </summary>
public class UpdateService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly Timer _checkTimer;
    private readonly UpdateManager? _updateManager;
    private bool _isDisposed;

    // GitHub repository info - update these to your actual repo
    private const string GitHubOwner = "dev-ov2";
    private const string GitHubRepo = "canopy-sharp";
    private const string GitHubApiBaseUrl = "https://api.github.com";
    
    // Velopack GitHub source URL
    private static readonly string VelopackGitHubUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}";

    public event EventHandler<UpdateInfo>? UpdateAvailable;
    public event EventHandler<string>? UpdateError;
    public event EventHandler<int>? DownloadProgress;
    public event EventHandler? UpdateDownloaded;

    public Version CurrentVersion { get; }
    public bool IsCheckingForUpdates { get; private set; }
    public bool IsDownloading { get; private set; }
    public bool IsVelopackAvailable => _updateManager?.IsInstalled ?? false;

    public UpdateService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Canopy-App");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

        CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

        // Initialize Velopack update manager
        try
        {
            _updateManager = new UpdateManager(new GithubSource(VelopackGitHubUrl, null, false));
            Debug.WriteLine($"Velopack initialized. IsInstalled: {_updateManager.IsInstalled}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Velopack initialization failed (running in dev mode?): {ex.Message}");
            _updateManager = null;
        }

        // Check for updates every 4 hours
        _checkTimer = new Timer(
            async _ => await CheckForUpdatesAsync(),
            null,
            TimeSpan.FromMinutes(1), // Initial delay
            TimeSpan.FromHours(4));  // Check interval
    }

    /// <summary>
    /// Checks for updates using Velopack or GitHub API
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates || !_settingsService.Settings.AutoUpdate)
            return null;

        IsCheckingForUpdates = true;

        try
        {
            // Try Velopack first if available
            if (_updateManager?.IsInstalled == true)
            {
                return await CheckForUpdatesVelopackAsync();
            }

            // Fall back to GitHub API
            return await CheckForUpdatesGitHubAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex.Message}");
            UpdateError?.Invoke(this, ex.Message);
            return null;
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    /// <summary>
    /// Checks for updates using Velopack
    /// </summary>
    private async Task<UpdateInfo?> CheckForUpdatesVelopackAsync()
    {
        if (_updateManager == null) return null;

        var updateInfo = await _updateManager.CheckForUpdatesAsync();
        if (updateInfo == null)
        {
            Debug.WriteLine("No Velopack updates available");
            return null;
        }

        var info = new UpdateInfo
        {
            Version = new Version(updateInfo.TargetFullRelease.Version.Major, 
                                   updateInfo.TargetFullRelease.Version.Minor, 
                                   updateInfo.TargetFullRelease.Version.Patch),
            ReleaseNotes = "",
            DownloadUrl = "",
            ReleaseUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases",
            AssetName = updateInfo.TargetFullRelease.FileName,
            PublishedAt = null,
            IsVelopackUpdate = true
        };

        UpdateAvailable?.Invoke(this, info);
        return info;
    }

    /// <summary>
    /// Checks GitHub for newer releases (fallback when not installed via Velopack)
    /// </summary>
    private async Task<UpdateInfo?> CheckForUpdatesGitHubAsync()
    {
        var url = $"{GitHubApiBaseUrl}/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            Debug.WriteLine($"GitHub API returned {response.StatusCode}");
            return null;
        }

        var release = await response.Content.ReadFromJsonAsync<GitHubRelease>();
        if (release == null) return null;

        // Parse version from tag (e.g., "v1.2.3" -> "1.2.3")
        var versionString = release.TagName.TrimStart('v', 'V');
        if (!Version.TryParse(versionString, out var latestVersion))
        {
            Debug.WriteLine($"Could not parse version from tag: {release.TagName}");
            return null;
        }

        if (latestVersion > CurrentVersion)
        {
            // Find Windows asset
            var windowsAsset = release.Assets.FirstOrDefault(a =>
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                a.Name.Contains("windows", StringComparison.OrdinalIgnoreCase) ||
                a.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase));

            var updateInfo = new UpdateInfo
            {
                Version = latestVersion,
                ReleaseNotes = release.Body ?? "",
                DownloadUrl = windowsAsset?.BrowserDownloadUrl ?? release.HtmlUrl,
                ReleaseUrl = release.HtmlUrl,
                AssetName = windowsAsset?.Name,
                PublishedAt = release.PublishedAt,
                IsVelopackUpdate = false
            };

            UpdateAvailable?.Invoke(this, updateInfo);
            return updateInfo;
        }

        Debug.WriteLine($"Current version {CurrentVersion} is up to date (latest: {latestVersion})");
        return null;
    }

    /// <summary>
    /// Downloads and installs the update
    /// </summary>
    public async Task DownloadAndInstallAsync(UpdateInfo updateInfo)
    {
        if (IsDownloading)
            return;

        IsDownloading = true;

        try
        {
            if (updateInfo.IsVelopackUpdate && _updateManager?.IsInstalled == true)
            {
                await DownloadAndInstallVelopackAsync();
            }
            else
            {
                await DownloadAndInstallManualAsync(updateInfo);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update failed: {ex.Message}");
            UpdateError?.Invoke(this, ex.Message);
        }
        finally
        {
            IsDownloading = false;
        }
    }

    /// <summary>
    /// Downloads and applies update using Velopack (supports delta updates)
    /// </summary>
    private async Task DownloadAndInstallVelopackAsync()
    {
        if (_updateManager == null) return;

        var updateInfo = await _updateManager.CheckForUpdatesAsync();
        if (updateInfo == null) return;

        // Download with progress reporting
        await _updateManager.DownloadUpdatesAsync(updateInfo, progress =>
        {
            DownloadProgress?.Invoke(this, progress);
        });

        UpdateDownloaded?.Invoke(this, EventArgs.Empty);

        // Apply update and restart
        _updateManager.ApplyUpdatesAndRestart(updateInfo);
    }

    /// <summary>
    /// Downloads and launches the update installer manually
    /// </summary>
    private async Task DownloadAndInstallManualAsync(UpdateInfo updateInfo)
    {
        if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
            return;

        var tempPath = Path.Combine(Path.GetTempPath(), updateInfo.AssetName ?? "CanopyUpdate.exe");

        using var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var bytesRead = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[8192];
        int read;

        while ((read = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read));
            bytesRead += read;

            if (totalBytes > 0)
            {
                var progress = (int)(bytesRead * 100 / totalBytes);
                DownloadProgress?.Invoke(this, progress);
            }
        }

        UpdateDownloaded?.Invoke(this, EventArgs.Empty);

        // Launch installer and exit
        Process.Start(new ProcessStartInfo
        {
            FileName = tempPath,
            UseShellExecute = true
        });

        Application.Current.Exit();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _checkTimer.Dispose();
        _httpClient.Dispose();
    }
}

/// <summary>
/// Information about an available update
/// </summary>
public class UpdateInfo
{
    public required Version Version { get; init; }
    public required string ReleaseNotes { get; init; }
    public required string DownloadUrl { get; init; }
    public required string ReleaseUrl { get; init; }
    public string? AssetName { get; init; }
    public DateTime? PublishedAt { get; init; }
    public bool IsVelopackUpdate { get; init; }
}

/// <summary>
/// GitHub release API response
/// </summary>
internal class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("published_at")]
    public DateTime? PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = new();
}

/// <summary>
/// GitHub asset API response
/// </summary>
internal class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
