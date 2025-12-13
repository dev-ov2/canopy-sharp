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
    private readonly string _logPath;
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

        // Setup log file in AppData
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Canopy");
        Directory.CreateDirectory(appDataPath);
        _logPath = Path.Combine(appDataPath, "canopy-update.log");

        CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        Log($"UpdateService initialized. CurrentVersion: {CurrentVersion}");

        // Initialize Velopack update manager
        try
        {
            _updateManager = new UpdateManager(new GithubSource(VelopackGitHubUrl, null, false));
            Log($"Velopack initialized. IsInstalled: {_updateManager.IsInstalled}, Source: {VelopackGitHubUrl}");
        }
        catch (Exception ex)
        {
            Log($"Velopack initialization failed: {ex.Message}");
            Debug.WriteLine($"Velopack initialization failed (running in dev mode?): {ex.Message}");
            _updateManager = null;
        }

        // Check for updates every 4 hours
        Log($"Starting update timer: InitialDelay=1min, Interval=4hrs");
        _checkTimer = new Timer(
            async _ => await CheckForUpdatesAsync(),
            null,
            TimeSpan.FromMinutes(1), // Initial delay
            TimeSpan.FromHours(4));  // Check interval
    }

    private void Log(string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logLine = $"[{timestamp}] {message}{Environment.NewLine}";
            File.AppendAllText(_logPath, logLine);
            Debug.WriteLine($"[UpdateService] {message}");
        }
        catch
        {
            // Don't throw on log failures
        }
    }

    /// <summary>
    /// Checks for updates using Velopack or GitHub API
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        Log($"CheckForUpdatesAsync called. IsCheckingForUpdates={IsCheckingForUpdates}");
        
        if (IsCheckingForUpdates)
        {
            Log("Already checking for updates, skipping");
            return null;
        }

        IsCheckingForUpdates = true;
        Log($"AutoUpdate setting: {_settingsService.Settings.AutoUpdate}");

        try
        {
            // Try Velopack first if available
            if (_updateManager?.IsInstalled == true)
            {
                Log("Using Velopack to check for updates");
                return await CheckForUpdatesVelopackAsync();
            }
            else
            {
                Log($"Velopack not available (IsInstalled={_updateManager?.IsInstalled}), falling back to GitHub API");
                // Fall back to GitHub API
                return await CheckForUpdatesGitHubAsync();
            }
        }
        catch (Exception ex)
        {
            Log($"Update check failed with exception: {ex}");
            Debug.WriteLine($"Update check failed: {ex.Message}");
            UpdateError?.Invoke(this, ex.Message);
            return null;
        }
        finally
        {
            IsCheckingForUpdates = false;
            Log("CheckForUpdatesAsync completed");
        }
    }

    /// <summary>
    /// Checks for updates using Velopack
    /// </summary>
    private async Task<UpdateInfo?> CheckForUpdatesVelopackAsync()
    {
        if (_updateManager == null)
        {
            Log("Velopack UpdateManager is null");
            return null;
        }

        Log("Calling Velopack CheckForUpdatesAsync...");
        var updateInfo = await _updateManager.CheckForUpdatesAsync();
        
        if (updateInfo == null)
        {
            Log("Velopack returned null - no updates available");
            Debug.WriteLine("No Velopack updates available");
            return null;
        }

        Log($"Velopack found update: {updateInfo.TargetFullRelease.Version}, File: {updateInfo.TargetFullRelease.FileName}");

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

        Log($"Firing UpdateAvailable event for version {info.Version}");
        UpdateAvailable?.Invoke(this, info);
        return info;
    }

    /// <summary>
    /// Checks GitHub for newer releases (fallback when not installed via Velopack)
    /// </summary>
    private async Task<UpdateInfo?> CheckForUpdatesGitHubAsync()
    {
        var url = $"{GitHubApiBaseUrl}/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
        Log($"Fetching GitHub releases: {url}");
        
        var response = await _httpClient.GetAsync(url);
        Log($"GitHub API response: {response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            Log($"GitHub API error: {response.StatusCode} - {response.ReasonPhrase}");
            Debug.WriteLine($"GitHub API returned {response.StatusCode}");
            return null;
        }

        var release = await response.Content.ReadFromJsonAsync<GitHubRelease>();
        if (release == null)
        {
            Log("Failed to parse GitHub release response");
            return null;
        }

        Log($"GitHub latest release: Tag={release.TagName}, Name={release.Name}");

        // Parse version from tag (e.g., "v1.2.3" -> "1.2.3")
        var versionString = release.TagName.TrimStart('v', 'V');
        if (!Version.TryParse(versionString, out var latestVersion))
        {
            Log($"Could not parse version from tag: {release.TagName}");
            Debug.WriteLine($"Could not parse version from tag: {release.TagName}");
            return null;
        }

        Log($"Version comparison: Current={CurrentVersion}, Latest={latestVersion}, NeedsUpdate={latestVersion > CurrentVersion}");

        if (latestVersion > CurrentVersion)
        {
            // Find Windows asset
            Log($"Available assets: {string.Join(", ", release.Assets.Select(a => a.Name))}");
            
            var windowsAsset = release.Assets.FirstOrDefault(a =>
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                a.Name.Contains("windows", StringComparison.OrdinalIgnoreCase) ||
                a.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase));

            Log($"Selected Windows asset: {windowsAsset?.Name ?? "null"}, URL: {windowsAsset?.BrowserDownloadUrl ?? "null"}");

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

            Log($"Firing UpdateAvailable event for version {updateInfo.Version}");
            UpdateAvailable?.Invoke(this, updateInfo);
            return updateInfo;
        }

        Log($"Current version {CurrentVersion} is up to date (latest: {latestVersion})");
        Debug.WriteLine($"Current version {CurrentVersion} is up to date (latest: {latestVersion})");
        return null;
    }

    /// <summary>
    /// Downloads and installs the update
    /// </summary>
    public async Task DownloadAndInstallAsync(UpdateInfo updateInfo)
    {
        Log($"DownloadAndInstallAsync called. Version={updateInfo.Version}, IsVelopack={updateInfo.IsVelopackUpdate}");
        
        if (IsDownloading)
        {
            Log("Already downloading, skipping");
            return;
        }

        IsDownloading = true;

        try
        {
            if (updateInfo.IsVelopackUpdate && _updateManager?.IsInstalled == true)
            {
                Log("Using Velopack to download and install");
                await DownloadAndInstallVelopackAsync();
            }
            else
            {
                Log("Using manual download and install");
                await DownloadAndInstallManualAsync(updateInfo);
            }
        }
        catch (Exception ex)
        {
            Log($"Update failed with exception: {ex}");
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
        if (_updateManager == null)
        {
            Log("Velopack UpdateManager is null, cannot download");
            return;
        }

        Log("Checking for updates via Velopack before download...");
        var updateInfo = await _updateManager.CheckForUpdatesAsync();
        if (updateInfo == null)
        {
            Log("No update info returned from Velopack");
            return;
        }

        Log($"Downloading update: {updateInfo.TargetFullRelease.FileName}");
        
        // Download with progress reporting
        await _updateManager.DownloadUpdatesAsync(updateInfo, progress =>
        {
            Log($"Download progress: {progress}%");
            DownloadProgress?.Invoke(this, progress);
        });

        Log("Download complete, firing UpdateDownloaded event");
        UpdateDownloaded?.Invoke(this, EventArgs.Empty);

        Log("Applying update and restarting...");
        // Apply update and restart
        _updateManager.ApplyUpdatesAndRestart(updateInfo);
    }

    /// <summary>
    /// Downloads and launches the update installer manually
    /// </summary>
    private async Task DownloadAndInstallManualAsync(UpdateInfo updateInfo)
    {
        if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
        {
            Log("DownloadUrl is null or empty, cannot download");
            return;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), updateInfo.AssetName ?? "CanopyUpdate.exe");
        Log($"Downloading to: {tempPath}");
        Log($"Download URL: {updateInfo.DownloadUrl}");

        // Delete existing file if present
        if (File.Exists(tempPath))
        {
            try
            {
                File.Delete(tempPath);
                Log("Deleted existing temp file");
            }
            catch (Exception ex)
            {
                Log($"Failed to delete existing temp file: {ex.Message}");
                // Generate a unique filename instead
                tempPath = Path.Combine(Path.GetTempPath(), $"CanopyUpdate_{DateTime.Now:yyyyMMddHHmmss}.exe");
                Log($"Using alternative path: {tempPath}");
            }
        }

        using var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        Log($"Response received. ContentLength: {totalBytes}");
        
        var bytesRead = 0L;

        // Download to file - explicitly manage stream lifetimes
        using (var contentStream = await response.Content.ReadAsStreamAsync())
        using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough))
        {
            var buffer = new byte[8192];
            int read;
            var lastLoggedProgress = -1;

            while ((read = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                bytesRead += read;

                if (totalBytes > 0)
                {
                    var progress = (int)(bytesRead * 100 / totalBytes);
                    if (progress != lastLoggedProgress && progress % 10 == 0)
                    {
                        Log($"Download progress: {progress}% ({bytesRead}/{totalBytes} bytes)");
                        lastLoggedProgress = progress;
                    }
                    DownloadProgress?.Invoke(this, progress);
                }
            }

            // Explicitly flush and close
            await fileStream.FlushAsync();
        }
        // Streams are now closed

        Log($"Download complete. Total bytes: {bytesRead}");
        
        // Verify file exists and has content
        var fileInfo = new FileInfo(tempPath);
        if (!fileInfo.Exists || fileInfo.Length == 0)
        {
            Log($"ERROR: Downloaded file is missing or empty. Exists={fileInfo.Exists}, Length={fileInfo.Length}");
            throw new IOException("Downloaded file is missing or empty");
        }
        Log($"Verified file: {fileInfo.Length} bytes");

        UpdateDownloaded?.Invoke(this, EventArgs.Empty);

        // Use cmd.exe to launch the installer after a delay, allowing this app to exit first
        // This ensures the current app is fully closed before the installer runs
        // Use --silent flag for silent update that preserves user data
        var batchContent = $@"@echo off
timeout /t 2 /nobreak >nul
start """" ""{tempPath}"" --silent
del ""%~f0""
";
        var batchPath = Path.Combine(Path.GetTempPath(), "canopy_update_launcher.bat");
        File.WriteAllText(batchPath, batchContent);
        
        Log($"Created launcher batch file: {batchPath}");
        Log($"Launching installer via batch file with --silent flag...");
        
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batchPath}\"",
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false
        });

        Log("Exiting application for update...");
        
        // Exit the app - the batch file will launch the installer after we're gone
        Application.Current.Exit();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        Log("UpdateService disposing");
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
