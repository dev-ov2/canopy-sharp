using Canopy.Core.Application;
using Canopy.Core.Logging;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Canopy.Linux.Services;

/// <summary>
/// Service to check for updates on Linux.
/// Linux updates are handled via package manager or manual download.
/// This service checks GitHub for new releases and notifies the user.
/// </summary>
public class LinuxUpdateService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly ICanopyLogger _logger;
    private readonly Timer _checkTimer;
    private bool _isDisposed;

    private const string GitHubOwner = "dev-ov2";
    private const string GitHubRepo = "canopy-sharp";
    private const string GitHubApiBaseUrl = "https://api.github.com";

    public event EventHandler<LinuxUpdateInfo>? UpdateAvailable;
    public event EventHandler<string>? UpdateError;

    public Version CurrentVersion { get; }
    public bool IsCheckingForUpdates { get; private set; }

    public LinuxUpdateService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _logger = CanopyLoggerFactory.CreateLogger<LinuxUpdateService>();
        
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Canopy-Linux");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        _logger.Info($"LinuxUpdateService initialized. CurrentVersion: {CurrentVersion}");

        // Check for updates every 4 hours (starting after 1 minute)
        _checkTimer = new Timer(
            async _ => await CheckForUpdatesAsync(),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromHours(4));
    }

    /// <summary>
    /// Checks GitHub for newer releases.
    /// </summary>
    public async Task<LinuxUpdateInfo?> CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates)
        {
            return null;
        }

        if (!_settingsService.Settings.AutoUpdate)
        {
            _logger.Debug("Auto-update disabled, skipping check");
            return null;
        }

        IsCheckingForUpdates = true;

        try
        {
            var url = $"{GitHubApiBaseUrl}/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
            _logger.Debug($"Checking for updates: {url}");

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.Debug($"GitHub API returned {response.StatusCode}");
                return null;
            }

            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>();
            if (release == null)
            {
                _logger.Debug("Failed to parse GitHub release");
                return null;
            }

            // Parse version from tag
            var versionString = release.TagName.TrimStart('v', 'V');
            if (!Version.TryParse(versionString, out var latestVersion))
            {
                _logger.Debug($"Could not parse version from tag: {release.TagName}");
                return null;
            }

            _logger.Debug($"Version check: Current={CurrentVersion}, Latest={latestVersion}");

            if (latestVersion > CurrentVersion)
            {
                // Find Linux asset
                var linuxAsset = release.Assets.FirstOrDefault(a =>
                    a.Name.Contains("linux", StringComparison.OrdinalIgnoreCase) &&
                    (a.Name.EndsWith(".tar.gz") || a.Name.EndsWith(".zip")));

                var updateInfo = new LinuxUpdateInfo
                {
                    Version = latestVersion,
                    ReleaseNotes = release.Body ?? "",
                    DownloadUrl = linuxAsset?.BrowserDownloadUrl ?? release.HtmlUrl,
                    ReleaseUrl = release.HtmlUrl,
                    AssetName = linuxAsset?.Name,
                    PublishedAt = release.PublishedAt
                };

                _logger.Info($"Update available: v{latestVersion}");
                UpdateAvailable?.Invoke(this, updateInfo);
                return updateInfo;
            }

            _logger.Debug($"Current version {CurrentVersion} is up to date");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.Debug($"Network error checking for updates: {ex.Message}");
            return null;
        }
        catch (TaskCanceledException)
        {
            _logger.Debug("Update check timed out");
            return null;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Update check failed: {ex.Message}");
            UpdateError?.Invoke(this, ex.Message);
            return null;
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    /// <summary>
    /// Opens the release page in the default browser for manual download.
    /// </summary>
    public void OpenReleasePage(LinuxUpdateInfo updateInfo)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = updateInfo.ReleaseUrl,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            _logger.Info($"Opened release page: {updateInfo.ReleaseUrl}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to open release page: {ex.Message}");
        }
    }

    /// <summary>
    /// Downloads the update to the user's Downloads folder.
    /// </summary>
    public async Task<string?> DownloadUpdateAsync(LinuxUpdateInfo updateInfo, Action<int>? progress = null)
    {
        if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
        {
            _logger.Warning("No download URL available");
            return null;
        }

        try
        {
            var downloadsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");
            Directory.CreateDirectory(downloadsDir);

            var fileName = updateInfo.AssetName ?? $"canopy-{updateInfo.Version}-linux-x64.tar.gz";
            var filePath = Path.Combine(downloadsDir, fileName);

            _logger.Info($"Downloading update to: {filePath}");

            using var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var bytesRead = 0L;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            int read;

            while ((read = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                bytesRead += read;

                if (totalBytes > 0)
                {
                    var percent = (int)(bytesRead * 100 / totalBytes);
                    progress?.Invoke(percent);
                }
            }

            _logger.Info($"Download complete: {filePath}");
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.Error($"Download failed: {ex.Message}");
            UpdateError?.Invoke(this, ex.Message);
            return null;
        }
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
/// Information about an available update for Linux.
/// </summary>
public class LinuxUpdateInfo
{
    public required Version Version { get; init; }
    public required string ReleaseNotes { get; init; }
    public required string DownloadUrl { get; init; }
    public required string ReleaseUrl { get; init; }
    public string? AssetName { get; init; }
    public DateTime? PublishedAt { get; init; }
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
