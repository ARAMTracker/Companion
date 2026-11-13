using System.Runtime.CompilerServices;

namespace AramTrackerCompanion.Tests;

internal static class TestData
{
    // Keeps test files out of the real data folder.
    [ModuleInitializer]
    internal static void UseTemporaryDataDirectory() =>
        Environment.SetEnvironmentVariable(
            AppInfo.DataDirectoryVariable,
            Directory.CreateTempSubdirectory("aramtracker-companion-tests-data-").FullName);
}
