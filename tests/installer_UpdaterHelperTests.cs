using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

internal static class InstallerUpdaterHelperTests
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--fixture-parent") {
            int ms = args.Length > 1 ? Int32.Parse(args[1]) : 1200;
            Thread.Sleep(ms);
            return 0;
        }
        try { RunAll(); return 0; }
        catch (Exception e) { Console.Error.WriteLine("FAIL " + e); return 1; }
    }

    private static void RunAll()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string helper = Path.Combine(baseDir, "installer_UpdaterHelper.exe");
        string fakeInstaller = Path.Combine(baseDir, "installer_FakeInstaller.exe");
        if (!File.Exists(helper)) throw new InvalidOperationException("compile UpdaterHelper.exe to the private tests folder first");
        if (!File.Exists(fakeInstaller)) throw new InvalidOperationException("compile the fake installer fixture first");

        string root = Path.Combine(Path.GetTempPath(), "nte-updater-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            string appDir = Path.Combine(root, "app");
            string settings = Path.Combine(appDir, "Settings");
            string downloads = Path.Combine(settings, "Downloads");
            Directory.CreateDirectory(downloads);
            string runtimeExe = Path.Combine(appDir, "เปิดคำแปลรุ่นหลัก-v28c.exe");
            File.Copy(Assembly.GetExecutingAssembly().Location, runtimeExe);
            string installer = Path.Combine(downloads, "verified-setup.exe");
            File.Copy(fakeInstaller, installer);
            string digest = Sha256(installer);
            string marker = Path.Combine(root, "installer-args.txt");
            Environment.SetEnvironmentVariable("NTE_TEST_INSTALLER_MARKER", marker);

            using (Process parent = StartParent(runtimeExe, 1500)) {
                string error;
                int exit = RunHelper(helper, parent.Id, installer, new string('0', 64), appDir, out error);
                Assert(exit != 0 && !File.Exists(marker), "rejects an installer with a mismatched digest");
                parent.WaitForExit();
            }

            string outside = Path.Combine(root, "outside.exe");
            File.Copy(fakeInstaller, outside);
            using (Process parent = StartParent(runtimeExe, 1500)) {
                string error;
                int exit = RunHelper(helper, parent.Id, outside, Sha256(outside), appDir, out error);
                Assert(exit != 0 && !File.Exists(marker), "rejects installers outside app Settings\\Downloads");
                parent.WaitForExit();
            }

            string linkedApp = Path.Combine(root, "linked-app");
            string linkedSettings = Path.Combine(linkedApp, "Settings");
            string externalDownloads = Path.Combine(root, "redirected-downloads");
            Directory.CreateDirectory(linkedSettings);
            Directory.CreateDirectory(externalDownloads);
            string linkedRuntime = Path.Combine(linkedApp, "เปิดคำแปลรุ่นหลัก-v28c.exe");
            File.Copy(Assembly.GetExecutingAssembly().Location, linkedRuntime);
            string redirectedInstaller = Path.Combine(externalDownloads, "linked-setup.exe");
            File.Copy(fakeInstaller, redirectedInstaller);
            string downloadsJunction = Path.Combine(linkedSettings, "Downloads");
            if (TryCreateJunction(downloadsJunction, externalDownloads)) {
                using (Process parent = StartParent(linkedRuntime, 1500)) {
                    string error;
                    int exit = RunHelper(helper, parent.Id, Path.Combine(downloadsJunction, "linked-setup.exe"),
                        Sha256(redirectedInstaller), linkedApp, out error);
                    Assert(exit != 0 && !File.Exists(marker), "rejects a Downloads directory redirected through a junction");
                    parent.WaitForExit();
                }
            }
            else Console.WriteLine("SKIP junction rejection test: Windows refused a private fixture junction");

            {
                string error;
                int exit = RunHelper(helper, Process.GetCurrentProcess().Id, installer, digest, appDir, out error);
                Assert(exit != 0 && !File.Exists(marker), "rejects a parent process that is not the installed app");
            }

            using (Process parent = StartParent(runtimeExe, 1400)) {
                string error;
                int exit = RunHelper(helper, parent.Id, installer, digest, appDir, out error);
                Assert(exit == 0, "waits for the verified app process, then starts the installer");
                parent.WaitForExit();
                WaitForFile(marker, 5000);
                string arguments = File.ReadAllText(marker);
                Assert(arguments.Contains("/VERYSILENT") && arguments.Contains("/NTEUPDATE=1")
                    && arguments.Contains("/DIR=" + Path.GetFullPath(appDir)),
                    "passes the current app directory and silent upgrade marker");
            }

            File.Delete(marker);
            string outsideReady = Path.Combine(root, Guid.NewGuid().ToString("N") + ".ready");
            using (Process parent = StartParent(runtimeExe, 1200)) {
                Process helperProcess = StartHelper(helper, parent.Id, installer, digest, appDir, outsideReady);
                string error = helperProcess.StandardError.ReadToEnd();
                helperProcess.WaitForExit();
                parent.WaitForExit();
                Assert(helperProcess.ExitCode != 0 && error.Contains("direct child") && !File.Exists(outsideReady)
                    && !File.Exists(marker), "rejects ready acknowledgements outside Settings\\Downloads");
            }

            string existingReady = Path.Combine(downloads, Guid.NewGuid().ToString("N") + ".ready");
            File.WriteAllText(existingReady, "do-not-overwrite");
            using (Process parent = StartParent(runtimeExe, 1200)) {
                Process helperProcess = StartHelper(helper, parent.Id, installer, digest, appDir, existingReady);
                string error = helperProcess.StandardError.ReadToEnd();
                helperProcess.WaitForExit();
                parent.WaitForExit();
                Assert(helperProcess.ExitCode != 0 && File.ReadAllText(existingReady) == "do-not-overwrite"
                    && !File.Exists(marker), "rejects and preserves an existing ready acknowledgement");
            }
            File.Delete(existingReady);

            string readyFile = Path.Combine(downloads, Guid.NewGuid().ToString("N") + ".ready");
            using (Process parent = StartParent(runtimeExe, 3000)) {
                Process helperProcess = StartHelper(helper, parent.Id, installer, digest, appDir, readyFile);
                try {
                    WaitForFile(readyFile, 3000);
                    Assert(File.ReadAllText(readyFile).Trim() == parent.Id.ToString() && !parent.HasExited,
                        "publishes a verified ready acknowledgement before waiting for the app to exit");
                    parent.WaitForExit();
                    if (!helperProcess.WaitForExit(7000)) { helperProcess.Kill(); throw new InvalidOperationException("acknowledged helper timed out"); }
                    string helperError = helperProcess.StandardError.ReadToEnd();
                    Assert(helperProcess.ExitCode == 0 && helperError.Length == 0,
                        "continues the update after the acknowledged app process exits");
                    WaitForFile(marker, 5000);
                }
                finally { if (!parent.HasExited) parent.WaitForExit(); }
            }

            File.Delete(marker);
            readyFile = Path.Combine(downloads, Guid.NewGuid().ToString("N") + ".ready");
            using (Process parent = StartParent(runtimeExe, 2500)) {
                Process helperProcess = StartHelper(helper, parent.Id, installer, digest, appDir, readyFile);
                try {
                    WaitForFile(readyFile, 3000);
                    File.WriteAllText(installer, "tampered after acknowledgement");
                    parent.WaitForExit();
                    if (!helperProcess.WaitForExit(7000)) { helperProcess.Kill(); throw new InvalidOperationException("tampered helper timed out"); }
                    string helperError = helperProcess.StandardError.ReadToEnd();
                    Assert(helperProcess.ExitCode != 0 && helperError.Contains("digest") && !File.Exists(marker),
                        "rehashes after waiting and refuses an installer changed after acknowledgement");
                }
                finally { if (!parent.HasExited) parent.WaitForExit(); }
            }
            File.Copy(fakeInstaller, installer, true);
        }
        finally {
            Environment.SetEnvironmentVariable("NTE_TEST_INSTALLER_MARKER", null);
            string downloadsJunction = Path.Combine(root, "linked-app", "Settings", "Downloads");
            if (Directory.Exists(downloadsJunction)) Directory.Delete(downloadsJunction, false);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        Console.WriteLine("INSTALLER_UPDATER_HELPER_TESTS_PASS");
    }

    private static Process StartParent(string runtimeExe, int milliseconds)
    {
        var p = Process.Start(new ProcessStartInfo {
            FileName = runtimeExe, Arguments = "--fixture-parent " + milliseconds,
            UseShellExecute = false, CreateNoWindow = true
        });
        if (p == null) throw new InvalidOperationException("could not launch private parent fixture");
        return p;
    }

    private static int RunHelper(string helper, int pid, string installer, string digest, string appDir, out string stderr)
    {
        var p = StartHelper(helper, pid, installer, digest, appDir, null);
        if (p == null) throw new InvalidOperationException("could not launch helper fixture");
        string err = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(7000)) { p.Kill(); throw new InvalidOperationException("updater helper timed out"); }
        stderr = err;
        return p.ExitCode;
    }

    private static Process StartHelper(string helper, int pid, string installer, string digest, string appDir, string readyFile)
    {
        string arguments = "--parent " + pid + " --installer \"" + installer + "\" --sha256 " + digest + " --app-dir \"" + appDir + "\"";
        if (readyFile != null) arguments += " --ready-file \"" + readyFile + "\"";
        return Process.Start(new ProcessStartInfo {
            FileName = helper, Arguments = arguments,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true
        });
    }

    private static string Sha256(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    private static void WaitForFile(string path, int timeoutMs)
    {
        DateTime until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!File.Exists(path) && DateTime.UtcNow < until) Thread.Sleep(20);
        if (!File.Exists(path)) throw new InvalidOperationException("fake installer was not launched");
    }

    private static bool TryCreateJunction(string link, string target)
    {
        using (var p = Process.Start(new ProcessStartInfo {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/c mklink /J \"" + link + "\" \"" + target + "\"",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true
        })) {
            if (p == null) return false;
            p.WaitForExit();
            return p.ExitCode == 0 && Directory.Exists(link);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
