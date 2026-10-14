using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class Ui6RegressionHarness
{
    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        string app = Path.Combine(Path.GetTempPath(), "nte-ui6-harness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(app);
        var service = new FakeService();
        var settings = new ThemeSettings(app);
        using (var form = new ToggleWindow(app, service, settings)) {
            form.Show();
            var applySnapshot = typeof(ToggleWindow).GetMethod("ApplySnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            PumpUntil(() => service.InspectCount > 0, 3000, "initial inspect");
            PumpUntil(() => Find<Control>(form, "ReleaseBadge") != null, 1000, "header loaded");
            Assert(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Ui6PreviewProgram.cs"))
                .IndexOf("TEST ONLY", StringComparison.OrdinalIgnoreCase) >= 0,
                "test preview source labels its window as TEST ONLY");
            Assert(Find<Control>(form, "ReleaseBadge").Text == "NTE 1.4.5", "header uses game version with fallback");

            var thai = Find<AntdUI.Select>(form, "ThaiFontLibrary");
            var english = Find<AntdUI.Select>(form, "EnglishFontLibrary");
            Assert(thai != null && english != null, "separate Thai and English library selectors exist");
            Assert(thai.Items.Count == 3 && english.Items.Count == 2, "library shows default and custom choices");
            Assert(thai.List && english.List && thai.BackColor == ThemePalette.For(LauncherTheme.Dark).Card
                && thai.ForeColor == ThemePalette.For(LauncherTheme.Dark).Text,
                "font selectors use the dark card palette with fully themed dropdowns");
            Assert(((FontChoice)((AntdUI.SelectItem)thai.Items[0]).Tag).IsDefault && !((FontChoice)((AntdUI.SelectItem)thai.Items[0]).Tag).CanDelete,
                "Thai default is first and protected from deletion");
            Assert(((FontChoice)((AntdUI.SelectItem)english.Items[0]).Tag).IsDefault && !((FontChoice)((AntdUI.SelectItem)english.Items[0]).Tag).CanDelete,
                "English default is first and protected from deletion");
            Assert(Find<AntdUI.Button>(form, "FontDefault") == null
                && Find<AntdUI.Button>(form, "EnglishFontDefault") == null,
                "font page has no default action buttons");
            Assert(Find<AntdUI.Button>(form, "FontDelete") != null && !Find<AntdUI.Button>(form, "FontDelete").Enabled,
                "default selection cannot be deleted");
            var statusBadge = Find<AntdUI.Panel>(form, "StatusBadge");
            var statusText = Find<Label>(form, "ReadOnlyStatus");
            var mainCard = Find<AntdUI.Panel>(form, "TranslationCard");
            var actionRows = Find<TableLayoutPanel>(form, "TranslationCardRows");
            var headerCard = Find<AntdUI.Panel>(form, "HeaderCard");
            var mainGameBadge = Find<AntdUI.Panel>(form, "MainGameBadge");
            var mainGameHint = Find<Control>(form, "MainGameHint");
            var fontCard = Find<AntdUI.Panel>(form, "FontLibraryCard");
            var fontRows = Find<TableLayoutPanel>(form, "FontLibraryCardRows");
            var englishFontActions = Find<TableLayoutPanel>(form, "EnglishFontActions");
            var fontStack = Find<AntdUI.StackPanel>(form, "FontPageStack");
            Assert(statusBadge != null && statusBadge.Radius == 18 && statusText.BackColor == Color.Transparent,
                "status uses the shared rounded card surface");
            Assert(mainCard != null && mainCard.BackColor == Color.Transparent,
                "cards avoid exposed rectangular background fills");
            Assert(mainGameBadge != null && mainGameBadge.Radius == 12 && mainGameBadge.Shadow == 0
                && mainGameHint.BackColor == Color.Transparent, "game hint uses a compact rounded badge without shadow");
            Rectangle headerRect = headerCard.RectangleToScreen(headerCard.ClientRectangle);
            Rectangle cardRect = mainCard.RectangleToScreen(mainCard.ClientRectangle);
            Console.WriteLine("DIAGNOSTIC header="+headerRect+" card="+cardRect+" inner="+mainCard.Parent.Bounds+" outer="+mainCard.Parent.Parent.Bounds);
            Assert(Math.Abs(headerRect.Left - cardRect.Left) <= 1 && Math.Abs(headerRect.Right - cardRect.Right) <= 1,
                "header and main card frames align horizontally");
            Assert(fontCard.Height == 402 && fontRows.Height >= 364,
                "healthy font library collapses its hidden error row and fits all controls");
            Assert(englishFontActions.Bounds.Bottom <= fontRows.ClientSize.Height,
                "font action controls stay inside the healthy library card");

            string longFailure = "ตรวจสอบคำแปลไม่สำเร็จ: ไฟล์ที่ติดตั้งไม่ตรงกับข้อมูลที่บันทึกไว้ และยังพบไฟล์ที่กำลังใช้งานอยู่ในโฟลเดอร์เกม กรุณาปิดเกมแล้วลองคืนค่าเดิมก่อนเริ่มใหม่อีกครั้ง";
            typeof(ToggleWindow).GetMethod("SetStatus", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, new object[] { "error", longFailure });
            Application.DoEvents();
            int requiredStatusHeight = TextRenderer.MeasureText(longFailure, statusText.Font,
                new Size(statusText.ClientSize.Width, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
            var recoveryRow = Find<AntdUI.Button>(form, "RestoreOriginal").Parent as TableLayoutPanel;
            Assert(!statusText.AutoEllipsis && statusText.Height >= requiredStatusHeight,
                "multiline failure status is fully visible without ellipsis");
            Assert(actionRows.GetRowHeights()[8] >= requiredStatusHeight + statusBadge.Padding.Vertical
                && recoveryRow.Bounds.Bottom <= actionRows.ClientSize.Height,
                "expanded status keeps recovery controls below the full error text");
            var stack = Find<AntdUI.StackPanel>(form, "TranslatePageStack");
            PropertyInfo doubleBuffered = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert((bool)doubleBuffered.GetValue(stack, null), "scroll surface retains buffered painting");
            using (var background = new Bitmap(8, 8))
            using (var graphics = Graphics.FromImage(background)) {
                graphics.Clear(Color.White);
                MethodInfo paintBackground = stack.GetType().GetMethod("OnPaintBackground", BindingFlags.Instance | BindingFlags.NonPublic);
                paintBackground.Invoke(stack, new object[] { new PaintEventArgs(graphics, new Rectangle(0, 0, 8, 8)) });
                Assert(background.GetPixel(2, 2).ToArgb() == stack.BackColor.ToArgb(),
                    "scroll stack paints its theme background instead of a white erase frame");
            }
            service.GameRunning = true;
            applySnapshot.Invoke(form, new object[] { service.Inspect(), false, true });
            Assert(mainGameHint.Text.StartsWith("เกมกำลังเปิดอยู่", StringComparison.Ordinal),
                "game-running hint names the blocked game state");
            Assert(statusText.Tag as string == "error", "external game detection preserves a real operation error");
            service.GameRunning = false;
            applySnapshot.Invoke(form, new object[] { service.Inspect(), false, true });
            typeof(ToggleWindow).GetMethod("SetStatus", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, new object[] { "idle", "พร้อมใช้งาน" });
            service.GameRunning = true;
            applySnapshot.Invoke(form, new object[] { service.Inspect(), false, true });
            Assert(statusText.Tag as string == "waiting" && statusText.Text == "กรุณาปิดเกมก่อนทำรายการ"
                && statusText.Text.IndexOf("Launcher", StringComparison.OrdinalIgnoreCase) < 0,
                "blocked notice asks users to close the game only");
            service.GameRunning = false;
            applySnapshot.Invoke(form, new object[] { service.Inspect(), false, true });
            Assert(statusText.Tag as string == "success", "waiting notice clears when the actual game closes");

            var tabs = Find<AntdUI.Tabs>(form, "MainTabs");
            tabs.SelectedIndex = 1;
            PumpUntil(() => fontStack.YScroll != null && fontStack.YScroll.Visible, 3000, "font page scrollbar");
            Console.WriteLine("DIAGNOSTIC fontWidth="+fontCard.Width+" mainWidth="+mainCard.Width+" rail="+fontStack.YScroll.Bounds+" padding="+fontStack.Padding+" outer="+fontStack.Bounds);
            Assert(fontCard.Width == mainCard.Width, "scrolled font cards align to the main card column");
            thai.ExpandDrop = true;
            applySnapshot.Invoke(form, new object[] { service.Inspect(), false, true });
            Assert(thai.ExpandDrop, "unchanged periodic snapshot preserves an open font selector");
            thai.ExpandDrop = false;

            thai.SelectedIndex = 1;
            PumpUntil(() => service.SelectFontCount == 1, 3000, "Thai library selection");
            Assert(service.LastSelectedRole == FontRole.Thai && service.LastSelectedId == "thai-custom",
                "Thai selector calls the Thai selection operation");
            PumpUntil(() => thai.SelectedIndex == 1, 3000, "Thai selection retained after snapshot refresh");
            thai.SelectedIndex = 2;
            PumpUntil(() => service.SelectFontCount == 2, 3000, "same-name Thai font selection");
            Assert(service.LastSelectedRole == FontRole.Thai && service.LastSelectedId == "thai-custom-2",
                "same-name Thai font selection uses exact ID");
            PumpUntil(() => thai.SelectedIndex == 2, 3000, "same-name Thai selection retained after snapshot refresh");
            var delete = Find<AntdUI.Button>(form, "FontDelete");
            Assert(delete != null && delete.Enabled, "custom font can be deleted");
            delete.PerformClick();
            PumpUntil(() => service.DeleteFontCount == 1, 3000, "font library deletion");
            Assert(service.LastDeletedRole == FontRole.Thai && service.LastDeletedId == "thai-custom-2",
                "delete removes only the selected Thai font");
            PumpUntil(() => thai.Items.Count == 2, 3000, "Thai catalog refresh after deletion");
            english.SelectedIndex = 1;
            PumpUntil(() => service.SelectFontCount == 3, 3000, "English library selection");
            Assert(service.LastSelectedRole == FontRole.English && service.LastSelectedId == "english-custom",
                "English selector calls the English selection operation");
            var englishDelete = Find<AntdUI.Button>(form, "EnglishFontDelete");
            Assert(englishDelete != null && englishDelete.Enabled, "custom English font can be deleted");
            englishDelete.PerformClick();
            PumpUntil(() => service.DeleteFontCount == 2, 3000, "English library deletion");
            Assert(service.LastDeletedRole == FontRole.English && service.LastDeletedId == "english-custom",
                "delete removes only the selected English font");
            PumpUntil(() => english.Items.Count == 1, 3000, "English catalog refresh after deletion");

            service.FontLibraryError = "รายการฟอนต์ไม่ถูกต้อง";
            applySnapshot = typeof(ToggleWindow).GetMethod("ApplySnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            applySnapshot.Invoke(form, new object[] { service.Inspect(), false, true });
            var libraryError = Find<Control>(form, "FontLibraryError");
            var restore = Find<AntdUI.Button>(form, "RestoreOriginal");
            Assert(libraryError != null && libraryError.Visible && libraryError.Text.Contains(service.FontLibraryError),
                "font catalog error is visible on the font page");
            Assert(fontCard.Height == 444 && fontRows.RowStyles[1].Height == 42f && fontRows.Height >= 406,
                "font error row expands without clipping library actions");
            Assert(englishFontActions.Bounds.Bottom <= fontRows.ClientSize.Height,
                "font action controls stay inside the error-state library card");
            Assert(restore != null && restore.Enabled, "font catalog error does not disable active restore");
            Assert(!Find<AntdUI.Select>(form, "ThaiFontLibrary").Enabled
                && !Find<AntdUI.Button>(form, "FontImport").Enabled
                && !Find<AntdUI.Button>(form, "EnglishFontImport").Enabled,
                "font catalog error disables font library mutations");
            Assert(Find<Control>(form, "ReadOnlyStatus").Tag as string == "success",
                "font catalog error does not replace installed translation status");
            service.FontLibraryError = null;
            applySnapshot.Invoke(form, new object[] { service.Inspect(), false, true });
            Assert(!libraryError.Visible && Find<AntdUI.Button>(form, "FontImport").Enabled,
                "font catalog warning clears after a healthy snapshot");

            var progress = Find<AnimatedProgressControl>(form, "OperationProgress");
            var translate = Find<AntdUI.Button>(form, "EnableTranslation");
            Assert(progress != null && progress.Value > 0.99f, "active reopen begins at verified 100 percent");
            Assert(translate != null && translate.Enabled, "active translation can be checked again");
            Assert(translate.Text == "ตรวจอีกครั้ง" || translate.Text == "ตรวจคำแปล",
                "active action is labeled as a recheck");
            translate.PerformClick();
            PumpUntil(() => service.TranslateCount == 1 && !Find<AntdUI.Button>(form, "EnableTranslation").Loading,
                3000, "active translation recheck");
            Assert(progress.Value > 0.99f, "active recheck does not reset verified progress");
            Assert(service.InspectCount >= 2, "recheck follows normal async inspection flow");

            service.FailNextActiveVerify = true;
            translate.PerformClick();
            PumpUntil(() => service.TranslateCount == 2 && !translate.Loading, 3000, "failed active recheck");
            Assert(Find<Control>(form, "ReadOnlyStatus").Tag as string == "error",
                "failed active recheck reports an error");
            Assert(progress.Value > 0.99f && !progress.IsVerifiedSuccess,
                "failed active recheck keeps 100 percent but stops verified-success animation");
            string retainedFailure = statusText.Text;
            service.Message = "พร้อมใช้งาน";
            applySnapshot.Invoke(form, new object[] { service.Inspect(), false, true });
            Assert(statusText.Tag as string == "error" && statusText.Text == retainedFailure,
                "periodic inspection preserves the last real operation failure");

            tabs.SelectedIndex = 2;
            var statusDetail = Find<Label>(form, "StatusDetail");
            typeof(ToggleWindow).GetMethod("SetStatus", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, new object[] { "idle", "พร้อมใช้งาน" });
            service.Message = "รายละเอียด: ไม่สามารถเตรียมฟอนต์ได้ เนื่องจากมีไฟล์ของโปรแกรมกำลังใช้งานอยู่ กรุณาปิดเกมแล้วลองอีกครั้ง หลังจากเล่นเสร็จสามารถปิดโปรแกรมได้โดยคงคำแปลไว้ และคืนค่าเดิมเมื่อเปลี่ยนฟอนต์หรืออัปเดตเกม.";
            applySnapshot.Invoke(form, new object[] { service.Inspect(), false, false });
            int requiredDetailHeight = TextRenderer.MeasureText(statusDetail.Text, statusDetail.Font,
                new Size(statusDetail.ClientSize.Width, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
            Console.WriteLine("DIAGNOSTIC detail="+statusDetail.Bounds+" required="+requiredDetailHeight+" visible="+statusDetail.Visible+" hasmessage="+statusDetail.Text.Contains(service.Message));
            Assert(statusDetail.Text.Contains(service.Message) && statusDetail.Height >= requiredDetailHeight,
                "status-page diagnostics expand to show the full wrapped error");
            var stepRows = Find<TableLayoutPanel>(form, "StatusStepsCardRows");
            var stepText = stepRows.GetControlFromPosition(0, 1) as Label;
            int requiredStepsHeight = TextRenderer.MeasureText(stepText.Text, stepText.Font,
                new Size(stepText.ClientSize.Width, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
            Assert(stepText.Height >= requiredStepsHeight && stepRows.Height >= requiredStepsHeight + 26,
                "updated play and restore instructions fit without clipping");
            Assert(stepText.Text.Contains("ปิดเกม") && !stepText.Text.Contains("Launcher")
                && stepText.Text.Contains("คืนค่าเดิมเมื่อเปลี่ยนฟอนต์"),
                "usage instructions permit leaving the Launcher open and make restore optional");

            var theme = Find<CheckBox>(form, "ThemeToggle");
            tabs.SelectedIndex = 1;
            theme.Checked = !theme.Checked;
            Application.DoEvents();
            Assert(tabs.SelectedIndex == 1, "theme change keeps the current tab selected");
            Assert(thai.BackColor == ThemePalette.For(LauncherTheme.Light).Card
                && thai.ForeColor == ThemePalette.For(LauncherTheme.Light).Text
                && thai.List,
                "font selector colors follow the light theme and preserve keyboard dropdown behavior");
            tabs.SelectedIndex = 0;
            theme.Checked = !theme.Checked;
            Application.DoEvents();
            Assert(tabs.SelectedIndex == 0, "tab switching remains stable after theme change");

            Assert(typeof(AnimatedProgressControl).GetMethod("DrawBurst", BindingFlags.Instance | BindingFlags.NonPublic) == null,
                "verified progress has no crack or shards renderer");
        }
        var isolatedService = new FakeService();
        using (var isolated = new ToggleWindow(app, isolatedService, settings)) {
            isolated.Show();
            PumpUntil(() => isolatedService.InspectCount > 0, 3000, "isolated status inspect");
            Find<AntdUI.Tabs>(isolated, "MainTabs").SelectedIndex = 2;
            Application.DoEvents();
            var isolatedDetail = Find<Label>(isolated, "StatusDetail");
            var isolatedRows = isolatedDetail.Parent as TableLayoutPanel;
            int baselineDetailRow = isolatedRows.GetRowHeights()[2];
            string directFailure = "ทำรายการไม่สำเร็จ: " + new string('ก', 600);
            typeof(ToggleWindow).GetMethod("SetStatus", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(isolated, new object[] { "error", directFailure });
            int requiredHeight = TextRenderer.MeasureText(directFailure, isolatedDetail.Font,
                new Size(isolatedDetail.ClientSize.Width, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
            Assert(isolatedRows.GetRowHeights()[2] > baselineDetailRow && isolatedDetail.Height >= requiredHeight,
                "direct long operation errors resize status diagnostics after the page is laid out");
        }
        Directory.Delete(app, true);
        Console.WriteLine("UI6_REGRESSION_PASS");
        return 0;
    }

    private static T Find<T>(Control root, string name) where T : Control
    {
        Control[] found = root.Controls.Find(name, true);
        return found.Length == 0 ? null : found[0] as T;
    }

    private static void PumpUntil(Func<bool> condition, int timeoutMs, string label)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }
        Application.DoEvents();
        if (!condition()) throw new InvalidOperationException("ASSERT: timed out waiting for " + label);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT: " + message);
        Console.WriteLine("PASS " + message);
    }

    private sealed class FakeService : ILauncherService
    {
        public int InspectCount, TranslateCount, SelectFontCount, DeleteFontCount;
        public FontRole LastSelectedRole, LastDeletedRole;
        public string LastSelectedId, LastDeletedId;
        public string FontLibraryError;
        public bool GameRunning;
        public string Message = "ready";
        public bool FailNextActiveVerify;
        private FontChoice thaiSelected = new FontChoice { Id = "default", Name = "Default Thai", IsDefault = true };
        private FontChoice englishSelected = new FontChoice { Id = "default", Name = "Default English", IsDefault = true };
        private FontChoice[] thai = { new FontChoice { Id = "default", Name = "Default Thai", IsDefault = true, CanDelete = false },
            new FontChoice { Id = "thai-custom", Name = "Thai Same Family", CanDelete = true },
            new FontChoice { Id = "thai-custom-2", Name = "Thai Same Family", CanDelete = true } };
        private FontChoice[] english = { new FontChoice { Id = "default", Name = "Default English", IsDefault = true, CanDelete = false },
            new FontChoice { Id = "english-custom", Name = "English Custom", CanDelete = true } };
        public string GuidePath { get { return null; } }
        public LauncherSnapshot Inspect() { Interlocked.Increment(ref InspectCount); return new LauncherSnapshot {
            Ready = true, Active = true, GameRunning = GameRunning, CanSelectFont = String.IsNullOrEmpty(FontLibraryError), CanRestore = true,
            FontIsDefault = thaiSelected.IsDefault, FontName = thaiSelected.Name,
            EnglishFontIsDefault = englishSelected.IsDefault, EnglishFontName = englishSelected.Name,
            FontId = thaiSelected.Id, EnglishFontId = englishSelected.Id,
            ThaiFonts = String.IsNullOrEmpty(FontLibraryError) ? thai : new FontChoice[0],
            EnglishFonts = String.IsNullOrEmpty(FontLibraryError) ? english : new FontChoice[0],
            FontLibraryError = FontLibraryError, GameVersion = "1.4.5", Message = Message }; }
        public void SetGameRoot(string path) { }
        public FontChoice ImportFont(string path, Action<OperationProgress> progress) { return ImportFont(path, FontRole.Thai, progress); }
        public FontChoice ImportFont(string path, FontRole role, Action<OperationProgress> progress) { return null; }
        public void SelectDefaultFont() { SelectDefaultFont(FontRole.Thai); }
        public void SelectDefaultFont(FontRole role) { SelectFont(role, "default"); }
        public FontChoice[] ListFonts(FontRole role) { return role == FontRole.Thai ? thai : english; }
        public void SelectFont(FontRole role, string id) {
            Interlocked.Increment(ref SelectFontCount); LastSelectedRole = role; LastSelectedId = id;
            FontChoice selected = null;
            foreach (FontChoice candidate in ListFonts(role)) if (candidate.Id == id) selected = candidate;
            if (selected == null) throw new InvalidOperationException("missing test font " + id);
            if (role == FontRole.Thai) thaiSelected = selected; else englishSelected = selected;
        }
        public void DeleteFont(FontRole role, string id) {
            Interlocked.Increment(ref DeleteFontCount); LastDeletedRole = role; LastDeletedId = id;
            if (role == FontRole.Thai) { thai = Array.FindAll(thai, item => item.Id != id); if (thaiSelected.Id == id) thaiSelected = thai[0]; }
            else { english = new[] { english[0] }; if (englishSelected.Id == id) englishSelected = english[0]; }
        }
        public void Translate(Action<OperationProgress> progress) {
            Interlocked.Increment(ref TranslateCount);
            if (FailNextActiveVerify) { FailNextActiveVerify = false; throw new InvalidOperationException("verification failed"); }
        }
        public void Restore(Action<OperationProgress> progress) { }
    }
}

