using System;
using System.IO;

internal static class InstallerFakeInstaller
{
    private static int Main(string[] args)
    {
        string marker = Environment.GetEnvironmentVariable("NTE_TEST_INSTALLER_MARKER");
        if (String.IsNullOrWhiteSpace(marker)) return 4;
        File.WriteAllText(marker, String.Join("\n", args));
        return 0;
    }
}
