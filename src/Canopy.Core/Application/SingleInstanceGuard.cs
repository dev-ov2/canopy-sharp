namespace Canopy.Core.Application;

/// <summary>
/// Ensures only one instance of the application is running.
/// Platform-agnostic implementation using named Mutex.
/// </summary>
public static class SingleInstanceGuard
{
    private static Mutex? _mutex;
    private const string MutexName = "CanopySharp_SingleInstance_Mutex";

    /// <summary>
    /// Attempts to acquire the single instance lock
    /// </summary>
    /// <returns>True if this is the first instance, false if another instance is running</returns>
    public static bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);

        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Releases the single instance lock
    /// </summary>
    public static void Release()
    {
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Mutex was not owned by this thread
        }
        finally
        {
            _mutex?.Dispose();
            _mutex = null;
        }
    }
}
