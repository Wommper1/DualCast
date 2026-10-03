using System.IO;
using System.Runtime.CompilerServices;

namespace DualBluetooth.Tests;

internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void RedirectLog() =>
        Environment.SetEnvironmentVariable("DUALCAST_LOG_DIR", Path.Combine(Path.GetTempPath(), "DualCastTestLogs"));
}
