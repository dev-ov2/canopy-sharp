using Canopy.Core.Application;
using Canopy.Core.Logging;
using Gtk;
using System.Runtime.InteropServices;

namespace Canopy.Linux;

/// <summary>
/// Linux entry point for Canopy application
/// </summary>
public class Program
{
    [DllImport("libglib-2.0.so.0")]
    private static extern void g_set_prgname(string prgname);

    [DllImport("libglib-2.0.so.0")]
    private static extern void g_set_application_name(string applicationName);

    public static int Main(string[] args)
    {
        // Set WebKit environment variables BEFORE any GTK/WebKit initialization
        // This fixes crashes on Arch Linux and other systems with certain GPU drivers
        // when using the DMA-BUF renderer in WebKitGTK
        ConfigureWebKitEnvironment();

        // Initialize logging early
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var logDir = Path.Combine(home, ".local", "share", "canopy");
        Directory.CreateDirectory(logDir);
        CanopyLoggerFactory.SetLogDirectory(logDir);
        var logger = CanopyLoggerFactory.CreateLogger<Program>();
        
        logger.Info($"Canopy starting with args: {string.Join(" ", args)}");

        // Check for protocol URI
        string? protocolUri = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("canopy://"))
            {
                protocolUri = arg;
                logger.Info($"Protocol URI detected: {protocolUri}");
                break;
            }
        }

        // Check single instance BEFORE GTK initialization
        if (SingleInstanceGuard.IsAnotherInstanceRunning())
        {
            logger.Info("Another instance is already running");
            
            if (protocolUri != null)
            {
                // Send protocol to running instance
                SendProtocolToRunningInstance(protocolUri, logger);
            }
            
            logger.Info("Exiting second instance");
            return 0;
        }

        // Set program name BEFORE GTK init - critical for WM_CLASS
        try
        {
            g_set_prgname("canopy");
            g_set_application_name("Canopy");
        }
        catch
        {
            // Ignore if not available
        }

        // Initialize GTK
        Gtk.Application.Init();

        // Parse command line arguments
        var startMinimized = args.Contains("--minimized");

        // Create and run the application
        var app = new App(startMinimized, args);
        
        // Register and activate
        app.Register(GLib.Cancellable.Current);
        app.Activate();
        
        // Run GTK main loop
        Gtk.Application.Run();
        
        return 0;
    }

    /// <summary>
    /// Configures WebKit environment variables to prevent crashes on certain systems.
    /// Must be called BEFORE any GTK or WebKit initialization.
    /// </summary>
    private static void ConfigureWebKitEnvironment()
    {
        // WEBKIT_DISABLE_DMABUF_RENDERER=1
        // Fixes crash on Arch Linux (and derivatives like CachyOS, EndeavourOS, Manjaro)
        // and other systems where the DMA-BUF renderer causes issues with certain GPU drivers.
        // This is a known issue with WebKitGTK on systems with NVIDIA proprietary drivers,
        // AMD drivers with certain Mesa versions, or hybrid GPU setups.
        // See: https://bugs.webkit.org/show_bug.cgi?id=245703
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBKIT_DISABLE_DMABUF_RENDERER")))
        {
            Environment.SetEnvironmentVariable("WEBKIT_DISABLE_DMABUF_RENDERER", "1");
        }

        // Additional WebKit environment tweaks for stability:
        
        // Disable GPU compositing if running under Wayland with problematic drivers
        // Users can override this by setting the variable before launching
        // Uncomment if needed:
        // if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBKIT_DISABLE_COMPOSITING_MODE")))
        // {
        //     Environment.SetEnvironmentVariable("WEBKIT_DISABLE_COMPOSITING_MODE", "1");
        // }
    }

    private static void SendProtocolToRunningInstance(string uri, Canopy.Core.Logging.ICanopyLogger logger)
    {
        try
        {
            var protocolDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share", "canopy", "protocol");
            
            Directory.CreateDirectory(protocolDir);
            
            var filename = Path.Combine(protocolDir, $"protocol_{DateTime.UtcNow.Ticks}.uri");
            File.WriteAllText(filename, uri);
            
            logger.Info($"Wrote protocol file: {filename}");
        }
        catch (Exception ex)
        {
            logger.Error($"Failed to send protocol to running instance: {ex.Message}");
        }
    }
}
