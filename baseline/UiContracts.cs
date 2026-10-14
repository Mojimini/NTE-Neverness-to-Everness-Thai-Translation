using System;
public enum FontRole { Thai, English }

public sealed class LauncherSnapshot {
    public bool Ready, Active, GameRunning, CanSelectFont, FontIsDefault, CanRestore;
    public string Message, GameRoot, FontName, FontPreviewPath;
    public bool EnglishFontIsDefault;
    public string EnglishFontName, EnglishFontPreviewPath;
    public string GameVersion;
    public string FontId, EnglishFontId;
    public string FontLibraryError;
    public FontChoice[] ThaiFonts, EnglishFonts;
}

public sealed class FontChoice {
    public string Id, Name, ManagedSourcePath, PreviewPath;
    public bool IsDefault, CanDelete;
}

public sealed class OperationProgress {
    public string Text;
    public int CompletedStages, TotalStages;
}

public interface ILauncherService {
    LauncherSnapshot Inspect();
    void SetGameRoot(string path);
    FontChoice ImportFont(string path, Action<OperationProgress> progress);
    FontChoice ImportFont(string path, FontRole role, Action<OperationProgress> progress);
    void SelectDefaultFont();
    void SelectDefaultFont(FontRole role);
    FontChoice[] ListFonts(FontRole role);
    void SelectFont(FontRole role, string id);
    void DeleteFont(FontRole role, string id);
    void Translate(Action<OperationProgress> progress);
    void Restore(Action<OperationProgress> progress);
    string GuidePath { get; }
}
