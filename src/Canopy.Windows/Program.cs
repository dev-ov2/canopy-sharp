using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.Activation;

namespace Canopy.Windows;

/// <summary>
/// Application entry point with single-instance support and protocol activation handling.
/// </summary>
public class Program
{
    private const string InstanceKey = "canopy_instance";

    /// <summary>
    /// Event raised when the app is activated via protocol (e.g., canopy://...)
    /// </summary>
    public static event EventHandler<Uri>? ProtocolActivated;

    [STAThread]
    static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (!DecideRedirection())
        {
            Application.Start((p) =>
            {
                var context = new DispatcherQueueSynchronizationContext(
                    DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
        }

        return 0;
    }

    private static bool DecideRedirection()
    {
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();
        var keyInstance = AppInstance.FindOrRegisterForKey(InstanceKey);

        if (keyInstance.IsCurrent)
        {
            keyInstance.Activated += OnActivated;
            HandleActivationArgs(args);
            return false;
        }

        RedirectActivationTo(args, keyInstance);
        return true;
    }

    private static void OnActivated(object? sender, AppActivationArguments args)
    {
        HandleActivationArgs(args);
    }

    private static void HandleActivationArgs(AppActivationArguments args)
    {
        Uri? protocolUri = null;

        if (args.Kind == ExtendedActivationKind.Protocol)
        {
            if (args.Data is IProtocolActivatedEventArgs protocolArgs)
            {
                protocolUri = protocolArgs.Uri;
            }
        }
        else if (args.Kind == ExtendedActivationKind.Launch)
        {
            string? launchArgs = null;

            if (args.Data is ILaunchActivatedEventArgs launchData && 
                !string.IsNullOrWhiteSpace(launchData.Arguments))
            {
                // Extract the URI from launch arguments
                var parts = launchData.Arguments.Replace("\"", "").Split(' ');
                if (parts.Length > 1)
                {
                    launchArgs = parts[1];
                }
            }

            // Fallback to command line args for initial launch
            if (string.IsNullOrWhiteSpace(launchArgs))
            {
                var env = Environment.GetCommandLineArgs();
                if (env.Length > 1)
                {
                    launchArgs = env[1];
                }
            }

            if (!string.IsNullOrWhiteSpace(launchArgs) &&
                Uri.TryCreate(launchArgs.Trim('"'), UriKind.Absolute, out var uri))
            {
                protocolUri = uri;
            }
        }

        if (protocolUri != null)
        {
            Debug.WriteLine($"Protocol activated: {protocolUri}");
            ProtocolActivated?.Invoke(null, protocolUri);
        }
    }

    #region Win32 Interop for Single Instance Redirection

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll")]
    private static extern bool SetEvent(IntPtr hEvent);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, ulong nHandles, IntPtr[] pHandles, out uint dwIndex);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static void RedirectActivationTo(AppActivationArguments args, AppInstance keyInstance)
    {
        var redirectEventHandle = CreateEvent(IntPtr.Zero, true, false, null);

        Task.Run(() =>
        {
            keyInstance.RedirectActivationToAsync(args).AsTask().Wait();
            SetEvent(redirectEventHandle);
        });

        const uint CWMO_DEFAULT = 0;
        const uint INFINITE = 0xFFFFFFFF;
        _ = CoWaitForMultipleObjects(CWMO_DEFAULT, INFINITE, 1, [redirectEventHandle], out _);

        // Bring the existing instance to foreground
        try
        {
            var process = Process.GetProcessById((int)keyInstance.ProcessId);
            SetForegroundWindow(process.MainWindowHandle);
        }
        catch
        {
            // Process may have exited
        }
    }

    #endregion
}



