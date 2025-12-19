using Canopy.Core.Logging;
using Gtk;

namespace Canopy.Linux;

/// <summary>
/// Linux entry point for Canopy application
/// </summary>
public class Program
{
    public static int Main(string[] args)
    {
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
