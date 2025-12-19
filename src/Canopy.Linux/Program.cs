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
