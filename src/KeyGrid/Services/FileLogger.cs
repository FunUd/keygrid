using System.IO;

namespace KeyGrid.Services;

/// <summary>
/// Writes log messages to %LOCALAPPDATA%\KeyGrid\logs and maintains a 7-day retention policy
/// according to spec.md Section 12.
/// </summary>
public static class FileLogger
{
    private static readonly object _lock = new();
    private static readonly string _logDir;
    private static readonly string _logFilePath;

    static FileLogger()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _logDir = Path.Combine(localAppData, "KeyGrid", "logs");
        Directory.CreateDirectory(_logDir);

        string fileName = $"keygrid_{DateTime.Now:yyyyMMdd}.log";
        _logFilePath = Path.Combine(_logDir, fileName);

        CleanOldLogs();
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
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
            lock (_lock)
            {
                File.AppendAllText(_logFilePath, line);
            }
        }
        catch
        {
            // Logging failure should never crash the application
        }
    }

    private static void CleanOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-7);
            var dirInfo = new DirectoryInfo(_logDir);
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
