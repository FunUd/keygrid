using System.IO;
using System.Text;

namespace KeyGrid.Services;

/// <summary>
/// Writes log messages to %LOCALAPPDATA%\KeyGrid\logs and maintains a 7-day retention policy
/// according to spec.md Sections 11.3 and 12.
/// </summary>
/// <remarks>
/// The default level is INFO. Set KEYGRID_LOG_LEVEL to DEBUG, INFO, WARN, ERROR, or NONE
/// before starting the application to change it. DEBUG includes metadata such as window
/// class names, process IDs, window handles, click coordinates, and focus transitions.
/// Each daily file is capped at 5 MB so verbose diagnostics cannot grow without bound.
/// Once the cap is reached, further entries are dropped for the rest of that day.
/// </remarks>
public static class FileLogger
{
    internal const long MaxLogFileBytes = 5 * 1024 * 1024;
    private static readonly object _lock = new();
    private static readonly LogSeverity MinimumSeverity =
        ParseLogLevel(Environment.GetEnvironmentVariable("KEYGRID_LOG_LEVEL"));
    private static bool _initialized;
    private static string? _logFilePath;

    /// <summary>Log levels supported by the KEYGRID_LOG_LEVEL environment variable.</summary>
    public enum LogSeverity
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        None = 4
    }

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

            _logFilePath = GetLogFilePath(logDir, DateTime.Now);
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

    /// <summary>
    /// Initializes the log directory and applies retention cleanup even when the
    /// configured level disables new log output.
    /// </summary>
    internal static void Initialize()
    {
        lock (_lock)
        {
            EnsureInitialized();
        }
    }

    // Allows callers to skip collecting expensive or privacy-sensitive metadata
    // when debug output is disabled.
    internal static bool IsDebugEnabled => MinimumSeverity <= LogSeverity.Debug;

    public static void LogDebug(string message) => Log(LogSeverity.Debug, "DEBUG", message);
    public static void LogInfo(string message) => Log(LogSeverity.Info, "INFO", message);
    public static void LogWarning(string message) => Log(LogSeverity.Warning, "WARN", message);
    public static void LogError(string message, Exception? ex = null)
    {
        string text = ex != null ? $"{message} - Exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" : message;
        Log(LogSeverity.Error, "ERROR", text);
    }

    private static void Log(LogSeverity severity, string label, string message)
    {
        // Compare before touching the filesystem so a disabled level has no I/O cost.
        if (MinimumSeverity == LogSeverity.None || severity < MinimumSeverity)
        {
            return;
        }

        try
        {
            lock (_lock)
            {
                EnsureInitialized();
                if (_logFilePath == null)
                {
                    return;
                }

                // Roll over at midnight and re-run retention cleanup so a resident
                // process does not keep writing to the previous day's file.
                string? logDirectory = Path.GetDirectoryName(_logFilePath);
                if (logDirectory == null)
                {
                    return;
                }

                DateTime now = DateTime.Now;
                string currentLogFilePath = GetLogFilePath(logDirectory, now);
                if (!string.Equals(_logFilePath, currentLogFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    _logFilePath = currentLogFilePath;
                    CleanOldLogs(logDirectory);
                }

                string line = $"[{now:yyyy-MM-dd HH:mm:ss.fff}] [{label}] {message}{Environment.NewLine}";
                long currentLength = File.Exists(currentLogFilePath)
                    ? new FileInfo(currentLogFilePath).Length
                    : 0;
                if (!IsWithinSizeLimit(currentLength, Encoding.UTF8.GetByteCount(line)))
                {
                    return;
                }

                File.AppendAllText(currentLogFilePath, line);
            }
        }
        catch
        {
            // Logging failure should never crash the application.
        }
    }

    internal static LogSeverity ParseLogLevel(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return LogSeverity.Info;
        }

        return configured.Trim().ToUpperInvariant() switch
        {
            "DEBUG" or "VERBOSE" => LogSeverity.Debug,
            "INFO" or "INFORMATION" => LogSeverity.Info,
            "WARN" or "WARNING" => LogSeverity.Warning,
            "ERROR" or "SEVERE" => LogSeverity.Error,
            "NONE" or "OFF" => LogSeverity.None,
            _ => LogSeverity.Info
        };
    }

    internal static string GetLogFilePath(string logDirectory, DateTime timestamp) =>
        Path.Combine(logDirectory, $"keygrid_{timestamp:yyyyMMdd}.log");

    internal static bool IsWithinSizeLimit(long currentLength, int incomingByteCount) =>
        currentLength + incomingByteCount <= MaxLogFileBytes;

    internal static void CleanOldLogs(string logDir)
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
            // Ignore cleanup failures.
        }
    }
}
