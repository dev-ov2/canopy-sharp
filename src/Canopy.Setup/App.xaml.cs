using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace Canopy.Setup;

public partial class App : Application
{
    private const string AppName = "Canopy";
    private const string ExeName = "Canopy.Windows.exe";
    
    // Folders/files to preserve during silent update
    private static readonly string[] PreservePaths = new[]
    {
        "settings.json",
        "canopy.log",
        "canopy-update.log",
        "EBWebView",           // WebView2 user data (cookies, cache, localStorage, etc.)
        "WebView2",            // Alternative WebView2 folder name
        "Cache",
        "Data"
    };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = e.Args.Select(a => a.ToLowerInvariant()).ToArray();
        
        // Check for uninstall flag
        if (args.Contains("--uninstall"))
        {
            var quiet = args.Contains("--quiet") || args.Contains("/quiet") || args.Contains("-q");
            PerformUninstall(quiet);
            Shutdown();
            return;
        }

        // Check for silent update flag
        if (args.Contains("--silent") || args.Contains("/silent") || args.Contains("-s"))
        {
            PerformSilentUpdate();
            Shutdown();
            return;
        }

        // Normal startup - show installer window
    }

    /// <summary>
    /// Performs a silent update that preserves user data
    /// </summary>
    private void PerformSilentUpdate()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "canopy_silent_update.log");
        
        void Log(string message)
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(logPath, line);
            }
            catch { }
        }

        try
        {
            Log("Silent update started");
            
            var installPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppName);
            
            Log($"Install path: {installPath}");

            // Find the embedded package
            var packagePath = FindPackage();
            if (packagePath == null)
            {
                Log("ERROR: Package not found");
                return;
            }
            Log($"Package found: {packagePath}");

            // Close any running instances
            Log("Closing running instances...");
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
            {
                try
                {
                    Log($"Killing process: {process.Id}");
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    Log($"Failed to kill process: {ex.Message}");
                }
            }

            // Wait a bit for processes to fully exit
            System.Threading.Thread.Sleep(1000);

            // Create install directory if it doesn't exist
            Directory.CreateDirectory(installPath);

            // Build list of paths to preserve
            var preserveFullPaths = PreservePaths
                .Select(p => Path.Combine(installPath, p))
                .Where(p => File.Exists(p) || Directory.Exists(p))
                .ToList();

            Log($"Preserving {preserveFullPaths.Count} paths: {string.Join(", ", preserveFullPaths.Select(Path.GetFileName))}");

            // Extract the package, skipping preserved paths
            Log("Extracting package...");
            using (var archive = ZipFile.OpenRead(packagePath))
            {
                var entries = archive.Entries.Where(e => e.FullName.StartsWith("lib/")).ToList();
                var extractedCount = 0;

                foreach (var entry in entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    // Remove the lib/net8.0-windows*/ prefix
                    var relativePath = entry.FullName;
                    var libIndex = relativePath.IndexOf("lib/");
                    if (libIndex >= 0)
                    {
                        relativePath = relativePath.Substring(libIndex + 4);
                        var slashIndex = relativePath.IndexOf('/');
                        if (slashIndex >= 0)
                        {
                            relativePath = relativePath.Substring(slashIndex + 1);
                        }
                    }

                    if (string.IsNullOrEmpty(relativePath))
                        continue;

                    var destPath = Path.Combine(installPath, relativePath);
                    
                    // Check if this path should be preserved
                    var shouldPreserve = preserveFullPaths.Any(p => 
                        destPath.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                        destPath.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

                    if (shouldPreserve)
                    {
                        continue; // Skip preserved files
                    }

                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    try
                    {
                        entry.ExtractToFile(destPath, true);
                        extractedCount++;
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to extract {relativePath}: {ex.Message}");
                    }
                }

                Log($"Extracted {extractedCount} files");
            }

            // Copy uninstaller
            var installerSource = Environment.ProcessPath;
            var uninstallerPath = Path.Combine(installPath, "Uninstall.exe");
            if (!string.IsNullOrEmpty(installerSource) && File.Exists(installerSource))
            {
                try
                {
                    File.Copy(installerSource, uninstallerPath, true);
                    Log("Copied uninstaller");
                }
                catch (Exception ex)
                {
                    Log($"Failed to copy uninstaller: {ex.Message}");
                }
            }

            // Update registry version
            UpdateRegistryVersion(installPath);
            Log("Updated registry");

            // Restart the application
            var exePath = Path.Combine(installPath, ExeName);
            if (File.Exists(exePath))
            {
                Log($"Launching: {exePath}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
            }

            Log("Silent update completed successfully");
        }
        catch (Exception ex)
        {
            Log($"Silent update failed: {ex}");
        }
    }

    private string? FindPackage()
    {
        // First, try to extract embedded package from assembly resources
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();
        var nupkgResource = resourceNames.FirstOrDefault(r => r.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));

        if (nupkgResource != null)
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(nupkgResource);
                if (stream != null)
                {
                    var tempDir = Path.Combine(Path.GetTempPath(), "CanopySetup");
                    Directory.CreateDirectory(tempDir);
                    var tempPath = Path.Combine(tempDir, "Canopy.nupkg");

                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }

                    using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write);
                    stream.CopyTo(fileStream);
                    return tempPath;
                }
            }
            catch { }
        }

        // Fall back to looking for package in the same directory
        var installerDir = AppContext.BaseDirectory;
        var packageDir = Path.Combine(installerDir, "Package");
        if (Directory.Exists(packageDir))
        {
            var nupkg = Directory.GetFiles(packageDir, "*.nupkg").FirstOrDefault();
            if (nupkg != null) return nupkg;
        }

        return Directory.GetFiles(installerDir, "*.nupkg").FirstOrDefault();
    }

    private void UpdateRegistryVersion(string installPath)
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var versionString = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";

            var exePath = Path.Combine(installPath, ExeName);
            var uninstallerPath = Path.Combine(installPath, "Uninstall.exe");

            using var uninstallKey = Registry.CurrentUser.CreateSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{AppName}");

            if (uninstallKey != null)
            {
                uninstallKey.SetValue("DisplayVersion", versionString);
                uninstallKey.SetValue("DisplayName", AppName);
                uninstallKey.SetValue("DisplayIcon", exePath);
                uninstallKey.SetValue("InstallLocation", installPath);
                uninstallKey.SetValue("Publisher", "Canopy");
                uninstallKey.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall");
                uninstallKey.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" --uninstall --quiet");
                uninstallKey.SetValue("NoModify", 1);
                uninstallKey.SetValue("NoRepair", 1);
            }
        }
        catch { }
    }

    private void PerformUninstall(bool quiet = false)
    {
        if (!quiet)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to uninstall {AppName}?\n\nThis will remove the application and all its files.",
                $"Uninstall {AppName}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;
        }

        try
        {
            var installPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppName);

            // Close any running instances
            foreach (var process in Process.GetProcessesByName("Canopy.Windows"))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
                catch { }
            }

            // Remove startup registration
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                runKey?.DeleteValue(AppName, false);
            }
            catch { }

            // Remove uninstaller registration
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{AppName}", false);
            }
            catch { }

            // Remove desktop shortcut
            var desktopShortcut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"{AppName}.lnk");
            if (File.Exists(desktopShortcut))
                File.Delete(desktopShortcut);

            // Remove Start Menu shortcut
            var startMenuShortcut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs",
                $"{AppName}.lnk");
            if (File.Exists(startMenuShortcut))
                File.Delete(startMenuShortcut);

            // Delete installation directory (but not ourselves if we're in it)
            if (Directory.Exists(installPath))
            {
                // Schedule self-deletion after we exit
                var currentExe = Environment.ProcessPath;
                var isInInstallDir = currentExe?.StartsWith(installPath, StringComparison.OrdinalIgnoreCase) ?? false;
                
                if (isInInstallDir)
                {
                    // Use cmd to delete after we exit
                    var batchContent = $@"
@echo off
timeout /t 2 /nobreak >nul
rmdir /s /q ""{installPath}""
del ""%~f0""
";
                    var batchPath = Path.Combine(Path.GetTempPath(), "canopy_uninstall.bat");
                    File.WriteAllText(batchPath, batchContent);
                    
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{batchPath}\"",
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true
                    });
                }
                else
                {
                    try
                    {
                        Directory.Delete(installPath, true);
                    }
                    catch
                    {
                        if (!quiet)
                        {
                            MessageBox.Show(
                                "Some files could not be deleted and will be removed on next restart.",
                                "Uninstall",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                    }
                }
            }

            if (!quiet)
            {
                MessageBox.Show(
                    $"{AppName} has been successfully uninstalled.",
                    "Uninstall Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            if (!quiet)
            {
                MessageBox.Show(
                    $"An error occurred during uninstallation:\n\n{ex.Message}",
                    "Uninstall Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
