using System.IO;

namespace KeyGrid.Services;

/// <summary>
/// Writes log messages to %LOCALAPPDATA%\KeyGrid\logs and maintains a 7-day retention policy
/// according to spec.md Section 12.
/// </summary>
public static class FileLogger
{
    private static readonly object _lock = new();
    private static bool _initialized;
    private static string? _logFilePath;

    /// <summary>
    /// Initializes the log directory on first use instead of in a static constructor.
    /// A failing static constructor surfaces as TypeInitializationException at the first
    /// log call and can crash startup, so initialization failures only disable logging.
    /// </summary>
    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string logDir = Path.Combine(localAppData, "KeyGrid", "logs");
            Directory.CreateDirectory(logDir);

            _logFilePath = Path.Combine(logDir, $"keygrid_{DateTime.Now:yyyyMMdd}.log");
            CleanOldLogs(logDir);
        }
        catch
        {
            // Logging must never prevent the application from starting.
            _logFilePath = null;
        }
        finally
        {
            _initialized = true;
        }
    }

    public static void LogInfo(string message) => Log("INFO", message);
    public static void LogWarning(string message) => Log("WARN", message);
    public static void LogError(string message, Exception? ex = null)
    {
        string text = ex != null ? $"{message} - Exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" : message;
        Log("ERROR", text);
    }

    private static void Log(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                EnsureInitialized();
                if (_logFilePath == null)
                {
                    return;
                }

                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, line);
            }
        }
        catch
        {
            // Logging failure should never crash the application
        }
    }

    private static void CleanOldLogs(string logDir)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-7);
            var dirInfo = new DirectoryInfo(logDir);
            foreach (var file in dirInfo.GetFiles("keygrid_*.log"))
            {
                if (file.LastWriteTime < cutoff)
                {
                    file.Delete();
                }
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }
}
