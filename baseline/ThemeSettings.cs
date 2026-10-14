using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public enum LauncherTheme { Dark, Light }

public sealed class ThemeSettings
{
    private readonly string filePath;
    private readonly string applicationDirectory;

    public ThemeSettings(string applicationPath)
    {
        if (String.IsNullOrWhiteSpace(applicationPath)) throw new ArgumentException("Application path is required", "applicationPath");
        applicationDirectory = Path.GetFullPath(applicationPath);
        filePath = Path.Combine(applicationDirectory, "Settings", "theme.txt");
    }

    public static ThemeSettings ForApplication(string appPath)
    {
        return new ThemeSettings(appPath);
    }

    public LauncherTheme Load()
    {
        try {
            EnsureSafePath(false);
            if (String.Equals(File.ReadAllText(filePath, Encoding.UTF8).Trim(), "Light", StringComparison.OrdinalIgnoreCase))
                return LauncherTheme.Light;
        }
        catch (DirectoryNotFoundException) { }
        catch (FileNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (InvalidOperationException) { }
        return LauncherTheme.Dark;
    }

    public void Save(LauncherTheme theme)
    {
        string settings = Path.Combine(applicationDirectory, "Settings");
        EnsureSafePath(false);
        Directory.CreateDirectory(settings);
        EnsureSafePath(true);
        string temp = Path.Combine(settings, "theme." + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp");
        byte[] data = new UTF8Encoding(false).GetBytes(theme == LauncherTheme.Light ? "Light" : "Dark");
        try {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                stream.Write(data, 0, data.Length);
                stream.Flush(true);
            }
            if (File.Exists(filePath)) File.Replace(temp, filePath, null);
            else File.Move(temp, filePath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private void EnsureSafePath(bool includeFile)
    {
        CheckAncestors(applicationDirectory);
        CheckNotReparse(Path.Combine(applicationDirectory, "Settings"));
        if (includeFile) CheckNotReparse(filePath);
    }

    private static void CheckAncestors(string directory)
    {
        string full = Path.GetFullPath(directory);
        string root = Path.GetPathRoot(full);
        CheckNotReparse(root);
        string remainder = full.Substring(root.Length);
        string current = root;
        foreach (string part in remainder.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)) {
            current = Path.Combine(current, part);
            CheckNotReparse(current);
        }
    }

    private static void CheckNotReparse(string path)
    {
        if (Directory.Exists(path) || File.Exists(path)) {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Theme settings path cannot be a link");
        }
    }
}

public sealed class ThemePalette
{
    public readonly bool IsDark;
    public readonly System.Drawing.Color Window, Page, Card, Border, Text, Muted, Accent, AccentHover;
    public readonly System.Drawing.Color Track, StatusIdle, StatusBusy, StatusBusyText;
    public readonly System.Drawing.Color StatusSuccess, StatusSuccessText, StatusError, StatusErrorText, StatusInfo, StatusInfoText;

    private ThemePalette(bool dark)
    {
        IsDark = dark;
        if (dark) {
            Window = System.Drawing.Color.FromArgb(13, 20, 34);
            Page = System.Drawing.Color.FromArgb(17, 28, 46);
            Card = System.Drawing.Color.FromArgb(25, 39, 61);
            Border = System.Drawing.Color.FromArgb(46, 64, 91);
            Text = System.Drawing.Color.FromArgb(230, 238, 249);
            Muted = System.Drawing.Color.FromArgb(158, 178, 204);
            Accent = System.Drawing.Color.FromArgb(101, 145, 188);
            AccentHover = System.Drawing.Color.FromArgb(125, 168, 209);
            Track = System.Drawing.Color.FromArgb(37, 54, 77);
            StatusIdle = Card;
            StatusBusy = System.Drawing.Color.FromArgb(71, 55, 29);
            StatusBusyText = System.Drawing.Color.FromArgb(255, 221, 156);
            StatusSuccess = System.Drawing.Color.FromArgb(24, 57, 52);
            StatusSuccessText = System.Drawing.Color.FromArgb(164, 241, 211);
            StatusError = System.Drawing.Color.FromArgb(68, 39, 47);
            StatusErrorText = System.Drawing.Color.FromArgb(255, 185, 190);
            StatusInfo = System.Drawing.Color.FromArgb(37, 57, 82);
            StatusInfoText = System.Drawing.Color.FromArgb(187, 215, 247);
        }
        else {
            Window = System.Drawing.Color.FromArgb(243, 247, 253);
            Page = System.Drawing.Color.FromArgb(248, 250, 254);
            Card = System.Drawing.Color.White;
            Border = System.Drawing.Color.FromArgb(218, 228, 241);
            Text = System.Drawing.Color.FromArgb(27, 45, 69);
            Muted = System.Drawing.Color.FromArgb(91, 108, 132);
            Accent = System.Drawing.Color.FromArgb(79, 126, 170);
            AccentHover = System.Drawing.Color.FromArgb(97, 147, 193);
            Track = System.Drawing.Color.FromArgb(228, 235, 245);
            StatusIdle = Card;
            StatusBusy = System.Drawing.Color.FromArgb(255, 247, 204);
            StatusBusyText = System.Drawing.Color.FromArgb(143, 86, 0);
            StatusSuccess = System.Drawing.Color.FromArgb(220, 252, 231);
            StatusSuccessText = System.Drawing.Color.FromArgb(22, 101, 52);
            StatusError = System.Drawing.Color.FromArgb(254, 226, 226);
            StatusErrorText = System.Drawing.Color.FromArgb(153, 27, 27);
            StatusInfo = System.Drawing.Color.FromArgb(231, 241, 255);
            StatusInfoText = System.Drawing.Color.FromArgb(23, 61, 112);
        }
    }

    public static ThemePalette For(LauncherTheme theme) { return new ThemePalette(theme == LauncherTheme.Dark); }
}
