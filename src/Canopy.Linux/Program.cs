namespace Canopy.Linux;

/// <summary>
/// Linux entry point stub
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Canopy Linux - Not yet implemented");
        Console.WriteLine("This will use GTK# or Avalonia with WebKitGTK for the UI");
        
        // TODO: Implement using one of:
        // - GTK# with WebKitGTK: https://github.com/AvaloniaSUI/GtkSharp
        // - Avalonia UI: https://avaloniaui.net/
        // - Photino: https://www.photino.dev/
        
        throw new PlatformNotSupportedException("Linux support is not yet implemented");
    }
}
