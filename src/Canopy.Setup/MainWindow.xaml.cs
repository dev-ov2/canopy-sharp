using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Canopy.Setup;

public partial class MainWindow : Window
{
    private enum InstallerPage { Welcome, Options, Installing, Complete, Error }
    private InstallerPage _currentPage = InstallerPage.Welcome;
    
    private readonly string _installPath;
    private readonly string _appName = "Canopy";
    private readonly string _exeName = "Canopy.Windows.exe";
    
    private bool _createDesktopShortcut = true;
    private bool _createStartMenuShortcut = true;
    private bool _addToStartup = true;
    private bool _launchAfterInstall = true;

    public MainWindow()
    {
        InitializeComponent();
        
        _installPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            _appName);
        
        InstallPathText.Text = $"Installation folder: {_installPath}";
        _ = LoadEmbeddedPngAsync("Canopy.Setup.Assets.Images.Sprout.png");
        UpdatePageVisibility();
    }

    public async Task LoadEmbeddedPngAsync(string resourceName)
    {
        var asm = Assembly.GetExecutingAssembly();
        using Stream? stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null)
            return;

        // Use MemoryStream instead of InMemoryRandomAccessStream
        var mem = new MemoryStream();
        await stream.CopyToAsync(mem);
        mem.Seek(0, SeekOrigin.Begin);

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = mem;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

        BackgroundImage.Source = bitmap;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage == InstallerPage.Installing)
        {
            var result = MessageBox.Show(
                "Installation is in progress. Are you sure you want to cancel?",
                "Cancel Installation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            
            if (result != MessageBoxResult.Yes)
                return;
        }
        
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage == InstallerPage.Options)
        {
            _currentPage = InstallerPage.Welcome;
            UpdatePageVisibility();
        }
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_currentPage)
        {
            case InstallerPage.Welcome:
                _currentPage = InstallerPage.Options;
                UpdatePageVisibility();
                break;
                
            case InstallerPage.Options:
                // Save options
                _createDesktopShortcut = DesktopShortcutCheckBox.IsChecked == true;
                _createStartMenuShortcut = StartMenuCheckBox.IsChecked == true;
                _addToStartup = StartupCheckBox.IsChecked == true;
                _launchAfterInstall = LaunchAfterInstallCheckBox.IsChecked == true;
                
                _currentPage = InstallerPage.Installing;
                UpdatePageVisibility();
                
                await PerformInstallation();
                break;
                
            case InstallerPage.Complete:
                if (_launchAfterInstall)
                {
                    LaunchApplication();
                }
                Close();
                break;
                
            case InstallerPage.Error:
                Close();
                break;
        }
    }

    private void UpdatePageVisibility()
    {
        WelcomePage.Visibility = Visibility.Collapsed;
        OptionsPage.Visibility = Visibility.Collapsed;
        InstallingPage.Visibility = Visibility.Collapsed;
        CompletePage.Visibility = Visibility.Collapsed;
        ErrorPage.Visibility = Visibility.Collapsed;
        
        BackButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Visible;
        NextButton.IsEnabled = true;
        
        switch (_currentPage)
        {
            case InstallerPage.Welcome:
                WelcomePage.Visibility = Visibility.Visible;
                NextButton.Content = "Next";
                break;
                
            case InstallerPage.Options:
                OptionsPage.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Visible;
                NextButton.Content = "Install";
                break;
                
            case InstallerPage.Installing:
                InstallingPage.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Collapsed;
                NextButton.IsEnabled = false;
                NextButton.Content = "Installing...";
                break;
                
            case InstallerPage.Complete:
                CompletePage.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Collapsed;
                NextButton.Content = _launchAfterInstall ? "Launch Canopy" : "Finish";
                break;
                
            case InstallerPage.Error:
                ErrorPage.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Collapsed;
                NextButton.Content = "Close";
                break;
        }
    }

    private async Task PerformInstallation()
    {
        try
        {
            await Task.Run(async () =>
            {
                // Step 1: Extract application files
                await UpdateProgress(0, "Preparing installation...");
                
                // Find the embedded package
                var packagePath = FindPackage();
                if (packagePath == null)
                {
                    throw new FileNotFoundException("Installation package not found.");
                }
                
                await UpdateProgress(10, "Creating installation directory...");
                
                // Create install directory
                if (Directory.Exists(_installPath))
                {
                    // Try to close any running instances
                    foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_exeName)))
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                        catch { }
                    }
                    
                    await Task.Delay(500);
                    
                    try
                    {
                        Directory.Delete(_installPath, true);
                    }
                    catch
                    {
                        // May fail if files are locked, continue anyway
                    }
                }
                
                Directory.CreateDirectory(_installPath);
                
                await UpdateProgress(20, "Extracting files...");
                
                // Extract the package (it's a .nupkg which is a zip file)
                using (var archive = ZipFile.OpenRead(packagePath))
                {
                    var entries = archive.Entries.Where(e => e.FullName.StartsWith("lib/")).ToList();
                    var totalEntries = entries.Count;
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
                        
                        var destPath = Path.Combine(_installPath, relativePath);
                        var destDir = Path.GetDirectoryName(destPath);
                        
                        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        {
                            Directory.CreateDirectory(destDir);
                        }
                        
                        entry.ExtractToFile(destPath, true);
                        
                        extractedCount++;
                        var progress = 20 + (int)(extractedCount * 50.0 / totalEntries);
                        await UpdateProgress(progress, $"Extracting files... ({extractedCount}/{totalEntries})");
                    }
                }
                
                await UpdateProgress(75, "Creating shortcuts...");
                
                var exePath = Path.Combine(_installPath, _exeName);
                
                // Create desktop shortcut
                if (_createDesktopShortcut)
                {
                    var desktopPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
                        $"{_appName}.lnk");
                    ShortcutHelper.CreateShortcut(desktopPath, exePath, _installPath, "Canopy - Game Overlay");
                }
                
                // Create Start Menu shortcut
                if (_createStartMenuShortcut)
                {
                    var startMenuPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                        "Programs",
                        $"{_appName}.lnk");
                    ShortcutHelper.CreateShortcut(startMenuPath, exePath, _installPath, "Canopy - Game Overlay");
                }
                
                await UpdateProgress(85, "Configuring startup...");
                
                // Add to startup
                if (_addToStartup)
                {
                    SetStartupRegistration(exePath, true);
                }
                
                await UpdateProgress(90, "Registering application...");
                
                // Register uninstaller
                RegisterUninstaller(exePath);
                
                await UpdateProgress(100, "Installation complete!");
            });
            
            _currentPage = InstallerPage.Complete;
            UpdatePageVisibility();
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                ErrorText.Text = $"An error occurred during installation:\n\n{ex.Message}";
                _currentPage = InstallerPage.Error;
                UpdatePageVisibility();
            });
        }
    }

    private string? FindPackage()
    {
        // First, try to extract embedded package from assembly resources
        var embeddedPackage = ExtractEmbeddedPackage();
        if (embeddedPackage != null)
            return embeddedPackage;
        
        // Fall back to looking for package in the same directory as the installer
        var installerDir = AppContext.BaseDirectory;
        
        // Look for Package subdirectory first
        var packageDir = Path.Combine(installerDir, "Package");
        if (Directory.Exists(packageDir))
        {
            var nupkg = Directory.GetFiles(packageDir, "*.nupkg").FirstOrDefault();
            if (nupkg != null) return nupkg;
        }
        
        // Look in current directory
        var nupkgInDir = Directory.GetFiles(installerDir, "*.nupkg").FirstOrDefault();
        if (nupkgInDir != null) return nupkgInDir;
        
        // Check current working directory
        var currentDir = Environment.CurrentDirectory;
        if (currentDir != installerDir)
        {
            var nupkgInCurrent = Directory.GetFiles(currentDir, "*.nupkg").FirstOrDefault();
            if (nupkgInCurrent != null) return nupkgInCurrent;
        }
        
        return null;
    }

    /// <summary>
    /// Extracts the embedded nupkg from assembly resources to a temp file
    /// </summary>
    private string? ExtractEmbeddedPackage()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();
        
        // Find the embedded nupkg resource
        var nupkgResource = resourceNames.FirstOrDefault(r => r.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
        
        if (nupkgResource == null)
        {
            Debug.WriteLine("No embedded nupkg found. Available resources: " + string.Join(", ", resourceNames));
            return null;
        }
        
        Debug.WriteLine($"Found embedded package: {nupkgResource}");
        
        try
        {
            using var stream = assembly.GetManifestResourceStream(nupkgResource);
            if (stream == null)
                return null;
            
            // Extract to temp directory
            var tempDir = Path.Combine(Path.GetTempPath(), "CanopySetup");
            Directory.CreateDirectory(tempDir);
            
            // Use a consistent filename
            var tempPath = Path.Combine(tempDir, "Canopy.nupkg");
            
            // Delete existing if present
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write);
            stream.CopyTo(fileStream);
            
            Debug.WriteLine($"Extracted package to: {tempPath}");
            return tempPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to extract embedded package: {ex.Message}");
            return null;
        }
    }

    private async Task UpdateProgress(int percentage, string status)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            InstallProgress.Value = percentage;
            ProgressPercentText.Text = $"{percentage}%";
            InstallStatusText.Text = status;
        });
    }

    private void SetStartupRegistration(string exePath, bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            
            if (key != null)
            {
                if (enable)
                {
                    key.SetValue(_appName, $"\"{exePath}\" --minimized");
                }
                else
                {
                    key.DeleteValue(_appName, false);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set startup registration: {ex.Message}");
        }
    }

    private void RegisterUninstaller(string exePath)
    {
        try
        {
            var uninstallKey = Registry.CurrentUser.CreateSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{_appName}");
            
            if (uninstallKey != null)
            {
                uninstallKey.SetValue("DisplayName", _appName);
                uninstallKey.SetValue("DisplayIcon", exePath);
                uninstallKey.SetValue("InstallLocation", _installPath);
                uninstallKey.SetValue("Publisher", "Canopy");
                uninstallKey.SetValue("DisplayVersion", "1.0.0");
                uninstallKey.SetValue("NoModify", 1);
                uninstallKey.SetValue("NoRepair", 1);
                
                // Calculate installed size
                try
                {
                    var dirInfo = new DirectoryInfo(_installPath);
                    var sizeInKb = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                        .Sum(f => f.Length) / 1024;
                    uninstallKey.SetValue("EstimatedSize", (int)sizeInKb);
                }
                catch { }
                
                uninstallKey.Close();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to register uninstaller: {ex.Message}");
        }
    }

    private void LaunchApplication()
    {
        try
        {
            var exePath = Path.Combine(_installPath, _exeName);
            if (File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch application: {ex.Message}");
        }
    }
}

/// <summary>
/// Helper class for creating Windows shortcuts using COM interop
/// </summary>
internal static class ShortcutHelper
{
    public static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string description)
    {
        try
        {
            var link = (IShellLink)new ShellLink();
            
            link.SetPath(targetPath);
            link.SetWorkingDirectory(workingDirectory);
            link.SetDescription(description);
            
            var file = (IPersistFile)link;
            file.Save(shortcutPath, false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to create shortcut: {ex.Message}");
        }
    }
    
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }
    
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
