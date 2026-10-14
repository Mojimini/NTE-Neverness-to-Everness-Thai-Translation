using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

internal static class UpdaterHelper
{
    private const string AppExecutableName = "เปิดคำแปลรุ่นหลัก-v28c.exe";
    private const int ParentExitTimeoutMs = 120000;

    private static int Main(string[] args)
    {
        string publishedReadyFile = null;
        try {
            string parentPidText, installerArg, digestArg, appDirArg, readyFileArg;
            if (!TryReadArguments(args, out parentPidText, out installerArg, out digestArg, out appDirArg, out readyFileArg))
                throw new ArgumentException("Usage: --parent PID --installer ABS_PATH --sha256 HEX --app-dir ABS_PATH [--ready-file ABS_PATH]");

            int parentPid;
            if (!Int32.TryParse(parentPidText, out parentPid) || parentPid <= 0)
                throw new ArgumentException("Invalid parent process ID.");
            if (digestArg.Length != 64 || !IsHex(digestArg))
                throw new ArgumentException("Installer SHA-256 must contain 64 hexadecimal characters.");
            if (!Path.IsPathRooted(installerArg) || !Path.IsPathRooted(appDirArg))
                throw new ArgumentException("Installer and app directory must be absolute paths.");

            string appDir = Path.GetFullPath(appDirArg).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string appExe = Path.Combine(appDir, AppExecutableName);
            string settingsDir = Path.Combine(appDir, "Settings");
            string downloadsDir = Path.Combine(settingsDir, "Downloads");
            string installer = Path.GetFullPath(installerArg);
            string readyFile = null;

            RequireDirectory(appDir, "Application directory does not exist.");
            RequireDirectory(settingsDir, "Settings directory does not exist.");
            RequireDirectory(downloadsDir, "Settings/Downloads directory does not exist.");
            RequireFile(appExe, "Installed application executable is missing.");
            RequireFile(installer, "Installer is missing.");
            RejectReparseAncestors(appDir);
            RejectReparseAncestors(settingsDir);
            RejectReparseAncestors(downloadsDir);
            RejectReparseAncestors(appExe);
            RejectReparseAncestors(installer);

            string downloadsPrefix = downloadsDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!installer.StartsWith(downloadsPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Installer must be inside the app's Settings/Downloads directory.");

            if (readyFileArg != null) {
                if (!Path.IsPathRooted(readyFileArg))
                    throw new ArgumentException("Ready acknowledgement path must be absolute.");
                readyFile = Path.GetFullPath(readyFileArg);
                string readyParent = Path.GetDirectoryName(readyFile);
                if (!String.Equals(readyParent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    downloadsDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Ready acknowledgement must be a direct child of Settings/Downloads.");
                if (!String.Equals(Path.GetExtension(readyFile), ".ready", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Ready acknowledgement must use the .ready extension.");
                if (File.Exists(readyFile) || Directory.Exists(readyFile))
                    throw new IOException("Ready acknowledgement path already exists.");
                RejectReparseAncestors(readyFile);
            }

            string actualHash = ComputeSha256(installer);
            if (!String.Equals(actualHash, digestArg, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Installer digest does not match the verified SHA-256.");

            using (Process parent = Process.GetProcessById(parentPid)) {
                string actualParentPath = Path.GetFullPath(parent.MainModule.FileName);
                if (!String.Equals(actualParentPath, appExe, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Parent process is not the installed NTE Thai launcher.");
                if (readyFile != null) {
                    PublishReadyAcknowledgement(readyFile, parentPid);
                    publishedReadyFile = readyFile;
                }
                if (!parent.WaitForExit(ParentExitTimeoutMs))
                    throw new TimeoutException("Parent application did not exit within the updater wait limit.");
            }

            RejectReparseAncestors(installer);
            string hashAfterWait = ComputeSha256(installer);
            if (!String.Equals(hashAfterWait, digestArg, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Installer digest changed while waiting for the application to exit.");
            if (publishedReadyFile != null) { TryDeleteReadyFile(publishedReadyFile); publishedReadyFile = null; }

            var start = new ProcessStartInfo {
                FileName = installer,
                Arguments = "/DIR=\"" + appDir + "\" /VERYSILENT /NTEUPDATE=1",
                WorkingDirectory = appDir,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(start);
            return 0;
        }
        catch (Exception ex) {
            Console.Error.WriteLine("Updater rejected request: " + ex.Message);
            return 2;
        }
        finally {
            if (publishedReadyFile != null) TryDeleteReadyFile(publishedReadyFile);
        }
    }

    private static bool TryReadArguments(string[] args, out string pid, out string installer,
        out string hash, out string appDir, out string readyFile)
    {
        pid = installer = hash = appDir = readyFile = null;
        if (args == null || (args.Length != 8 && args.Length != 10)) return false;
        for (int i = 0; i < args.Length; i += 2) {
            if (i + 1 >= args.Length) return false;
            switch (args[i]) {
                case "--parent": if (pid != null) return false; pid = args[i + 1]; break;
                case "--installer": if (installer != null) return false; installer = args[i + 1]; break;
                case "--sha256": if (hash != null) return false; hash = args[i + 1]; break;
                case "--app-dir": if (appDir != null) return false; appDir = args[i + 1]; break;
                case "--ready-file": if (readyFile != null) return false; readyFile = args[i + 1]; break;
                default: return false;
            }
        }
        return pid != null && installer != null && hash != null && appDir != null
            && ((args.Length == 8 && readyFile == null) || (args.Length == 10 && readyFile != null));
    }

    private static void PublishReadyAcknowledgement(string readyFile, int parentPid)
    {
        string tempFile = readyFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            byte[] content = Encoding.ASCII.GetBytes(parentPid.ToString() + "\n");
            using (var stream = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                stream.Write(content, 0, content.Length);
                stream.Flush(true);
            }
            File.Move(tempFile, readyFile);
        }
        finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private static void TryDeleteReadyFile(string path)
    {
        try {
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                File.Delete(path);
        }
        catch { }
    }

    private static bool IsHex(string value)
    {
        foreach (char c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
        return true;
    }

    private static string ComputeSha256(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            byte[] hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", String.Empty).ToLowerInvariant();
        }
    }

    private static void RequireFile(string path, string message)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(message, path);
    }

    private static void RequireDirectory(string path, string message)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException(message + " " + path);
    }

    private static void RejectReparseAncestors(string path)
    {
        string full = Path.GetFullPath(path);
        if (File.Exists(full)) {
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Reparse points are not allowed in updater paths.");
            full = Path.GetDirectoryName(full);
        }
        DirectoryInfo current = new DirectoryInfo(full);
        while (current != null) {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Reparse points are not allowed in updater paths.");
            current = current.Parent;
        }
    }
}
