using System.Runtime.CompilerServices;

namespace KeyGrid.UI.Tests;

internal static class TestModuleInitializer
{
    // Tests exercise UI paths that call FileLogger. Disable file output so test
    // runs do not append to or prune the user's real %LOCALAPPDATA% logs.
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("KEYGRID_LOG_LEVEL", "NONE");
    }
}
