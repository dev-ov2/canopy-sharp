namespace Canopy.Core.Application;

/// <summary>
/// Ensures only one instance of the application is running.
/// Uses file-based locking for cross-platform compatibility.
/// </summary>
public static class SingleInstanceGuard
{
    private static Mutex? _mutex;
    private static FileStream? _lockFile;
    private const string MutexName = "CanopySharp_SingleInstance_Mutex";
    private const string LockFileName = "canopy.lock";

    /// <summary>
    /// Attempts to acquire the single instance lock
    /// </summary>
    /// <returns>True if this is the first instance, false if another instance is running</returns>
    public static bool TryAcquire()
    {
        // On Windows, use Mutex (more reliable)
        if (OperatingSystem.IsWindows())
        {
            return TryAcquireMutex();
        }
        
        // On Linux/macOS, use file-based locking
        return TryAcquireFileLock();
    }

    private static bool TryAcquireMutex()
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

    private static bool TryAcquireFileLock()
    {
        try
        {
            var lockPath = GetLockFilePath();
            var lockDir = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrEmpty(lockDir))
            {
                Directory.CreateDirectory(lockDir);
            }

            // Try to create/open the lock file with exclusive access
            _lockFile = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

            // Write PID to lock file for debugging
            using var writer = new StreamWriter(_lockFile, leaveOpen: true);
            writer.WriteLine(Environment.ProcessId);
            writer.Flush();
            _lockFile.Seek(0, SeekOrigin.Begin);

            return true;
        }
        catch (IOException)
        {
            // File is locked by another process
            _lockFile?.Dispose();
            _lockFile = null;
            return false;
        }
        catch (Exception)
        {
            _lockFile?.Dispose();
            _lockFile = null;
            return false;
        }
    }

    private static string GetLockFilePath()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Canopy", LockFileName);
        }
        else
        {
            // Linux/macOS: use XDG runtime dir or fall back to /tmp
            var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (!string.IsNullOrEmpty(runtimeDir) && Directory.Exists(runtimeDir))
            {
                return Path.Combine(runtimeDir, LockFileName);
            }
            
            // Fallback to user's local share directory
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share", "canopy", LockFileName);
        }
    }

    /// <summary>
    /// Checks if another instance is running without acquiring the lock.
    /// </summary>
    public static bool IsAnotherInstanceRunning()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var mutex = Mutex.OpenExisting(MutexName);
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
        }
        else
        {
            var lockPath = GetLockFilePath();
            if (!File.Exists(lockPath))
                return false;

            try
            {
                using var fs = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                // If we can open it, no one else has it locked
                return false;
            }
            catch (IOException)
            {
                return true;
            }
        }
    }

    /// <summary>
    /// Releases the single instance lock
    /// </summary>
    public static void Release()
    {
        // Release mutex (Windows)
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

        // Release file lock (Linux/macOS)
        try
        {
            var lockPath = _lockFile != null ? GetLockFilePath() : null;
            _lockFile?.Dispose();
            _lockFile = null;
            
            // Try to delete the lock file
            if (lockPath != null && File.Exists(lockPath))
            {
                try { File.Delete(lockPath); } catch { }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
