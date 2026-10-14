using System;
using System.IO;
using System.Threading;

internal sealed class WpfLauncherServiceFake : ILauncherService
{
    private int inspectCount;
    private int translationCalls;
    private int restoreCalls;
    private int progressCallbacks;
    private volatile bool active;
    private volatile string message = "พร้อมใช้งาน";
    private string selectedThaiId = "default";
    private string selectedEnglishId = "default";
    private static readonly FontChoice[] ThaiChoices = {
        new FontChoice { Id = "default", Name = "ค่าเดิมไทย", IsDefault = true, CanDelete = false },
        new FontChoice { Id = "thai-a", Name = "Thai Family", CanDelete = true },
        new FontChoice { Id = "thai-b", Name = "Thai Family", CanDelete = true }
    };
    private static readonly FontChoice[] EnglishChoices = {
        new FontChoice { Id = "default", Name = "Default English", IsDefault = true, CanDelete = false },
        new FontChoice { Id = "english-custom", Name = "English Custom", CanDelete = true }
    };

    public bool BlockTranslation { get; set; }
    public bool EmitRapidProgress { get; set; }
    public bool FailNextTranslate { get; set; }
    public string ThaiFontPreviewPath { get; set; }
    public string EnglishFontPreviewPath { get; set; }
    public readonly ManualResetEventSlim RapidReportsCompleted = new ManualResetEventSlim(false);
    public readonly ManualResetEventSlim ReleaseTranslation = new ManualResetEventSlim(false);
    public int InspectCount { get { return Volatile.Read(ref inspectCount); } }
    public int TranslationCalls { get { return Volatile.Read(ref translationCalls); } }
    public int RestoreCalls { get { return Volatile.Read(ref restoreCalls); } }
    public bool Active { get { return active; } }
    public int ProgressCallbacks { get { return Volatile.Read(ref progressCallbacks); } }
    public string SelectedThaiId { get { return selectedThaiId; } }
    public string SelectedEnglishId { get { return selectedEnglishId; } }
    public string Message { get { return message; } set { message = value; } }
    public string GuidePath { get { return null; } }

    public LauncherSnapshot Inspect()
    {
        Interlocked.Increment(ref inspectCount);
        return new LauncherSnapshot {
            Ready = true,
            Active = active,
            GameRunning = false,
            CanSelectFont = !active,
            FontIsDefault = selectedThaiId == "default",
            EnglishFontIsDefault = selectedEnglishId == "default",
            CanRestore = active,
            FontName = Find(ThaiChoices, selectedThaiId).Name,
            EnglishFontName = Find(EnglishChoices, selectedEnglishId).Name,
            FontPreviewPath = ThaiFontPreviewPath,
            EnglishFontPreviewPath = EnglishFontPreviewPath,
            FontId = selectedThaiId,
            EnglishFontId = selectedEnglishId,
            ThaiFonts = (FontChoice[])ThaiChoices.Clone(),
            EnglishFonts = (FontChoice[])EnglishChoices.Clone(),
            GameVersion = "1.4.5",
            Message = message
        };
    }

    public void SetGameRoot(string path) { }
    public FontChoice ImportFont(string path, Action<OperationProgress> progress) { return ImportFont(path, FontRole.Thai, progress); }
    public FontChoice ImportFont(string path, FontRole role, Action<OperationProgress> progress) { return null; }
    public void SelectDefaultFont() { SelectDefaultFont(FontRole.Thai); }
    public void SelectDefaultFont(FontRole role) { SelectFont(role, "default"); }
    public FontChoice[] ListFonts(FontRole role) { return (FontChoice[])(role == FontRole.Thai ? ThaiChoices : EnglishChoices).Clone(); }
    public void SelectFont(FontRole role, string id)
    {
        FontChoice[] choices = role == FontRole.Thai ? ThaiChoices : EnglishChoices;
        if (Find(choices, id) == null) throw new InvalidOperationException("unknown fixture font id");
        if (role == FontRole.Thai) selectedThaiId = id; else selectedEnglishId = id;
    }
    public void DeleteFont(FontRole role, string id)
    {
        if (id == "default") throw new InvalidOperationException("default font is protected");
    }

    public void Translate(Action<OperationProgress> progress)
    {
        Interlocked.Increment(ref translationCalls);
        if (EmitRapidProgress && progress != null) {
            for (int i = 0; i < 6000; i++) {
                progress(new OperationProgress { Text = "download chunk " + i, CompletedStages = 0, TotalStages = 0 });
                Interlocked.Increment(ref progressCallbacks);
            }
        }
        RapidReportsCompleted.Set();
        if (BlockTranslation) ReleaseTranslation.Wait();
        if (FailNextTranslate) {
            FailNextTranslate = false;
            throw new IOException("ตรวจสอบคำแปล fixture ล้มเหลว");
        }
        active = true;
        message = "ติดตั้งคำแปลแล้ว";
    }

    public void Restore(Action<OperationProgress> progress)
    {
        Interlocked.Increment(ref restoreCalls);
        if (progress != null) {
            progress(new OperationProgress { Text = "กำลังคืนค่า", CompletedStages = 1, TotalStages = 2 });
            progress(new OperationProgress { Text = "คืนค่าเสร็จแล้ว", CompletedStages = 2, TotalStages = 2 });
        }
        active = false;
        message = "คืนค่าเดิมแล้ว";
    }

    private static FontChoice Find(FontChoice[] choices, string id)
    {
        foreach (FontChoice choice in choices) if (String.Equals(choice.Id, id, StringComparison.Ordinal)) return choice;
        return null;
    }
}

internal sealed class WpfReleaseDeliveryFake : IReleaseDelivery
{
    private int checkCalls;
    private int downloadCalls;
    public bool FailNextCheck { get; set; }
    public bool FailNextDownload { get; set; }
    public bool BlockDownload { get; set; }
    public readonly ManualResetEventSlim DownloadCompleted = new ManualResetEventSlim(false);
    public readonly ManualResetEventSlim DownloadStarted = new ManualResetEventSlim(false);
    public int CheckCalls { get { return Volatile.Read(ref checkCalls); } }
    public int DownloadCalls { get { return Volatile.Read(ref downloadCalls); } }

    public ReleaseInfo Check(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Interlocked.Increment(ref checkCalls);
        if (FailNextCheck) { FailNextCheck = false; throw new IOException("offline fixture"); }
        return new ReleaseInfo { Version = "1.2.0", Tag = "v1.2.0", InstallerName = "fixture.exe" };
    }

    public string Download(ReleaseInfo release, Action<TransferProgress> progress, CancellationToken token)
    {
        Interlocked.Increment(ref downloadCalls);
        DownloadStarted.Set();
        if (progress != null) progress(new TransferProgress { Text = "downloading fixture", BytesReceived = 1, TotalBytes = 2 });
        if (FailNextDownload) { FailNextDownload = false; throw new IOException("download fixture failure"); }
        if (BlockDownload) {
            token.WaitHandle.WaitOne();
            token.ThrowIfCancellationRequested();
        }
        DownloadCompleted.Set();
        return "verified-fixture-installer.exe";
    }
}
