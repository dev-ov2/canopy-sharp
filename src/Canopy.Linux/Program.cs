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
}
