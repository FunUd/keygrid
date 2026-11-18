using System.IO;
using KeyGrid.Services;

namespace KeyGrid.UI.Tests;

public class FileLoggerTests
{
    [Theory]
    [InlineData(null, FileLogger.LogSeverity.Info)]
    [InlineData("", FileLogger.LogSeverity.Info)]
    [InlineData("debug", FileLogger.LogSeverity.Debug)]
    [InlineData(" VERBOSE ", FileLogger.LogSeverity.Debug)]
    [InlineData("info", FileLogger.LogSeverity.Info)]
    [InlineData("WARN", FileLogger.LogSeverity.Warning)]
    [InlineData("Error", FileLogger.LogSeverity.Error)]
    [InlineData("none", FileLogger.LogSeverity.None)]
    [InlineData("unexpected", FileLogger.LogSeverity.Info)]
    public void ParseLogLevel_ReturnsExpectedSeverity(
        string? configured,
        FileLogger.LogSeverity expected)
    {
        Assert.Equal(expected, FileLogger.ParseLogLevel(configured));
    }

    [Fact]
    public void GetLogFilePath_UsesDateInFileName()
    {
        string directory = Path.Combine(Path.GetTempPath(), "keygrid-tests");

        string path = FileLogger.GetLogFilePath(
            directory,
            new DateTime(2026, 11, 18, 23, 59, 59));

        Assert.Equal(Path.Combine(directory, "keygrid_20261118.log"), path);
    }

    [Fact]
    public void IsWithinSizeLimit_AllowsAnEntryThatExactlyFillsTheFile()
    {
        Assert.True(FileLogger.IsWithinSizeLimit(FileLogger.MaxLogFileBytes - 10, 10));
        Assert.False(FileLogger.IsWithinSizeLimit(FileLogger.MaxLogFileBytes - 9, 10));
    }

    [Fact]
    public void CleanOldLogs_DeletesOnlyExpiredKeyGridLogs()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"keygrid-log-retention-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            string expired = Path.Combine(directory, "keygrid_20200101.log");
            string recent = Path.Combine(directory, "keygrid_20261118.log");
            string unrelated = Path.Combine(directory, "notes.log");
            File.WriteAllText(expired, "expired");
            File.WriteAllText(recent, "recent");
            File.WriteAllText(unrelated, "unrelated");
            File.SetLastWriteTime(expired, DateTime.Now.AddDays(-8));
            File.SetLastWriteTime(recent, DateTime.Now.AddDays(-1));
            File.SetLastWriteTime(unrelated, DateTime.Now.AddDays(-8));

            FileLogger.CleanOldLogs(directory);

            Assert.False(File.Exists(expired));
            Assert.True(File.Exists(recent));
            Assert.True(File.Exists(unrelated));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
