using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace Canopy.Setup;

public partial class App : Application
{
    private const string AppName = "Canopy";

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

        // Normal startup - show installer window
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
