using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class Ui6PreviewProgram
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string app = args.Length > 0 ? Path.GetFullPath(args[0])
            : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "app"));
        string settingsRoot = Path.Combine(Path.GetTempPath(), "nte-ui6-preview-" + Guid.NewGuid().ToString("N"));
        bool failurePreview = Array.IndexOf(args, "--failure") >= 0;
        var service = new PreviewService(app, failurePreview);
        using (var window = new ToggleWindow(app, service, new ThemeSettings(settingsRoot))) {
            window.Text = "UI6 PREVIEW — TEST ONLY (fake service)";
            if (failurePreview) window.Shown += (s, e) => {
                typeof(ToggleWindow).GetMethod("SetStatus", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[] { "error", "ทำรายการไม่สำเร็จ\r\nไม่สามารถเตรียมแพ็กฟอนต์ได้เนื่องจากเกมยังเปิดอยู่ กรุณาปิดเกมแล้วลองอีกครั้ง หากยังเกิดปัญหา ให้คืนค่าเดิมก่อนเปลี่ยนฟอนต์หรืออัปเดตเกม" });
            };
            window.FormClosed += (s, e) => {
                try { if (Directory.Exists(settingsRoot)) Directory.Delete(settingsRoot, true); }
                catch { }
            };
            Application.Run(window);
        }
    }

    private sealed class PreviewService : ILauncherService
    {
        private readonly string app;
        private string gameRoot;
        private bool active;
        private bool gameRunning;
        private FontChoice selectedThai;
        private FontChoice selectedEnglish;
        private FontChoice[] thaiFonts;
        private FontChoice[] englishFonts;
        private int importedFontCount;

        public PreviewService(string app, bool failurePreview)
        {
            this.app = app;
            gameRoot = Path.Combine(app, "PreviewGameRoot");
            gameRunning = failurePreview;
            active = failurePreview;
            selectedThai = ThaiDefault();
            selectedEnglish = EnglishDefault();
            thaiFonts = new[] { ThaiDefault() };
            englishFonts = new[] { EnglishDefault(), Monocraft() };
        }

        public string GuidePath { get { return Path.Combine(app, "วิธีใช้.md"); } }

        public LauncherSnapshot Inspect()
        {
            return new LauncherSnapshot {
                Ready = true,
                Active = active,
                GameRunning = gameRunning,
                CanRestore = active && !gameRunning,
                CanSelectFont = !active && !gameRunning,
                FontIsDefault = selectedThai.IsDefault,
                FontName = selectedThai.Name,
                FontPreviewPath = selectedThai.PreviewPath,
                FontId = selectedThai.Id,
                EnglishFontIsDefault = selectedEnglish.IsDefault,
                EnglishFontName = selectedEnglish.Name,
                EnglishFontPreviewPath = selectedEnglish.PreviewPath,
                EnglishFontId = selectedEnglish.Id,
                ThaiFonts = (FontChoice[])thaiFonts.Clone(),
                EnglishFonts = (FontChoice[])englishFonts.Clone(),
                GameVersion = "1.4.5",
                GameRoot = gameRoot,
                Message = gameRunning ? "กรุณาปิดเกมก่อนทำรายการ" : active ? "ติดตั้งคำแปลครบแล้ว พร้อมเปิดเกม" : "พร้อมทดสอบหน้าต่างตัวอย่าง"
            };
        }

        public void SetGameRoot(string path) { gameRoot = path; }

        public FontChoice ImportFont(string path, Action<OperationProgress> progress)
        {
            return ImportFont(path, FontRole.Thai, progress);
        }

        public FontChoice ImportFont(string path, FontRole role, Action<OperationProgress> progress)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new InvalidOperationException("ไม่พบไฟล์ฟอนต์ตัวอย่าง");
            string id = "preview-import-" + Interlocked.Increment(ref importedFontCount);
            var choice = new FontChoice {
                Id = id,
                Name = Path.GetFileNameWithoutExtension(path),
                ManagedSourcePath = path,
                PreviewPath = path,
                CanDelete = true
            };
            if (role == FontRole.Thai) { thaiFonts = Append(thaiFonts, choice); selectedThai = choice; }
            else { englishFonts = Append(englishFonts, choice); selectedEnglish = choice; }
            Report(progress, "เพิ่มฟอนต์สำหรับแสดงตัวอย่างแล้ว", 1, 1);
            return choice;
        }

        public void SelectDefaultFont() { SelectFont(FontRole.Thai, "default"); }
        public void SelectDefaultFont(FontRole role) { SelectFont(role, "default"); }

        public FontChoice[] ListFonts(FontRole role)
        {
            return (FontChoice[])(role == FontRole.Thai ? thaiFonts : englishFonts).Clone();
        }

        public void SelectFont(FontRole role, string id)
        {
            FontChoice choice = Find(role, id);
            if (role == FontRole.Thai) selectedThai = choice;
            else selectedEnglish = choice;
        }

        public void DeleteFont(FontRole role, string id)
        {
            if (String.Equals(id, "default", StringComparison.Ordinal)) throw new InvalidOperationException("ฟอนต์เดิมลบไม่ได้");
            if (role == FontRole.Thai) {
                thaiFonts = Remove(thaiFonts, id);
                if (selectedThai.Id == id) selectedThai = ThaiDefault();
            }
            else {
                englishFonts = Remove(englishFonts, id);
                if (selectedEnglish.Id == id) selectedEnglish = EnglishDefault();
            }
        }

        public void Translate(Action<OperationProgress> progress)
        {
            if (active) {
                Report(progress, "กำลังตรวจสอบคำแปลที่ติดตั้งแล้ว", 1, 1);
                return;
            }
            for (int stage = 0; stage <= 100; stage++) {
                Thread.Sleep(75);
                Report(progress, "กำลังแสดงตัวอย่างขั้นตอน " + stage + " จาก 100", stage, 100);
            }
            active = true;
        }

        public void Restore(Action<OperationProgress> progress)
        {
            for (int stage = 0; stage <= 100; stage++) {
                Thread.Sleep(55);
                Report(progress, "กำลังแสดงตัวอย่างการคืนค่า " + stage + " จาก 100", stage, 100);
            }
            active = false;
        }

        private FontChoice Find(FontRole role, string id)
        {
            foreach (FontChoice choice in role == FontRole.Thai ? thaiFonts : englishFonts)
                if (String.Equals(choice.Id, id, StringComparison.Ordinal)) return choice;
            throw new InvalidOperationException("ไม่พบฟอนต์ตัวอย่าง");
        }

        private FontChoice ThaiDefault()
        {
            return new FontChoice { Id = "default", Name = "MiSans + Noto Sans Thai (ฟอนต์เดิม)",
                PreviewPath = Path.Combine(app, "Fonts", "Default", "current.ttf"), IsDefault = true, CanDelete = false };
        }

        private FontChoice EnglishDefault()
        {
            return new FontChoice { Id = "default", Name = "MiSans (ฟอนต์อังกฤษเดิม)",
                PreviewPath = Path.Combine(app, "Fonts", "Default", "english.ttf"), IsDefault = true, CanDelete = false };
        }

        private FontChoice Monocraft()
        {
            return new FontChoice { Id = "c3906e842d1b7e99947abcc5ae3c8b409abf8cefd1006cbba1890f1096e132f2",
                Name = "Monocraft (สไตล์ Minecraft)",
                ManagedSourcePath = Path.Combine(app, "Fonts", "Bundled", "Monocraft", "font.ttf"),
                PreviewPath = Path.Combine(app, "Fonts", "Bundled", "Monocraft", "preview.ttf"), CanDelete = true };
        }

        private static FontChoice[] Append(FontChoice[] source, FontChoice choice)
        {
            var result = new FontChoice[source.Length + 1];
            Array.Copy(source, result, source.Length);
            result[result.Length - 1] = choice;
            return result;
        }

        private static FontChoice[] Remove(FontChoice[] source, string id)
        {
            var result = new System.Collections.Generic.List<FontChoice>();
            foreach (FontChoice choice in source) if (choice.Id != id) result.Add(choice);
            return result.ToArray();
        }

        private static void Report(Action<OperationProgress> progress, string text, int completed, int total)
        {
            if (progress != null) progress(new OperationProgress { Text = text, CompletedStages = completed, TotalStages = total });
        }
    }
}

