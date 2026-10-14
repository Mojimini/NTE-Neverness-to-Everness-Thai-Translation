using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AntdUI;
using Label = System.Windows.Forms.Label;

/// <summary>Modern UI for the launcher service. All service calls run off the UI thread.</summary>
public sealed partial class ToggleWindow : Form
{
    private static readonly Color Blue = Color.FromArgb(22, 119, 255);
    private static readonly Color BlueInk = Color.FromArgb(23, 61, 112);
    private static readonly Color Page = Color.FromArgb(243, 247, 253);
    private static readonly Color CardBorder = Color.FromArgb(224, 232, 243);
    private static readonly Color Yellow = Color.FromArgb(255, 247, 204);
    private static readonly Color YellowInk = Color.FromArgb(143, 86, 0);
    private static readonly Color Green = Color.FromArgb(220, 252, 231);
    private static readonly Color GreenInk = Color.FromArgb(22, 101, 52);
    private static readonly Color Red = Color.FromArgb(254, 226, 226);
    private static readonly Color RedInk = Color.FromArgb(153, 27, 27);
    private static readonly Color Info = Color.FromArgb(231, 241, 255);

    private readonly string app;
    private readonly ILauncherService service;
    private readonly ThemeSettings themeSettings;
    private LauncherSnapshot snapshot;
    private LauncherTheme currentTheme;
    private ThemePalette palette;
    private bool busy;
    private bool initializingThemeToggle;
    private bool inspectStarted;
    private int inspectInFlight;
    private int refreshGeneration;
    private System.Windows.Forms.Timer refreshTimer;

    private AntdUI.Tabs tabs;
    private AntdUI.Button translateButton;
    private AntdUI.Button restoreButton;
    private AntdUI.Button browseButton;
    private AntdUI.Button fontImportButton;
    private AntdUI.Button fontDeleteButton;
    private AntdUI.Button englishFontImportButton;
    private AntdUI.Button englishFontDeleteButton;
    private AntdUI.Select thaiFontLibrary;
    private AntdUI.Select englishFontLibrary;
    private AntdUI.Panel fontLibraryCard;
    private TableLayoutPanel fontLibraryRows;
    private CheckBox themeToggle;
    private AntdUI.Input gameRoot;
    private Label statusLabel;
    private AntdUI.Panel statusBadge;
    private Label statusDetail;
    private Label progressText;
    private Label fontLibraryError;
    private Label mainGameHint;
    private AntdUI.Panel mainGameBadge;
    private Label mainFontHint;
    private AnimatedProgressControl operationProgress;
    private FontPreviewControl fontPreview;
    private bool refreshingFontLibraries;
    private bool resizingStatusArea;
    private bool resizingStatusDetail;
    partial void UpdatePaletteApplied();
    partial void UpdateAvailabilityApplied();

    public ToggleWindow(string app, ILauncherService service)
        : this(app, service, ThemeSettings.ForApplication(app))
    {
    }

    internal ToggleWindow(string app, ILauncherService service, ThemeSettings settings)
    {
        if (String.IsNullOrWhiteSpace(app)) throw new ArgumentException("กรุณาระบุตำแหน่งแอป", "app");
        if (service == null) throw new ArgumentNullException("service");
        this.app = Path.GetFullPath(app);
        this.service = service;
        themeSettings = settings ?? ThemeSettings.ForApplication(this.app);
        currentTheme = themeSettings.Load();
        palette = ThemePalette.For(currentTheme);

        Text = "NTE ภาษาไทย";
        StartPosition = FormStartPosition.CenterScreen;
        Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
        ClientSize = new Size(Math.Min(980, Math.Max(820, workArea.Width - 80)),
            Math.Min(800, Math.Max(620, workArea.Height - 80)));
        MinimumSize = new Size(820, 620);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Page;
        ForeColor = BlueInk;
        Font = new Font("Leelawadee UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
        KeyPreview = true;

        BuildSurface();
        SetTheme(currentTheme, false);
        SetStatus("loading", "กำลังตรวจสอบความพร้อม…");
        UpdateAvailability();
        HandleCreated += OnFormHandleCreated;
        FormClosing += OnFormClosingGuard;
        FormClosed += (s, e) => {
            if (refreshTimer != null) { refreshTimer.Stop(); refreshTimer.Dispose(); refreshTimer = null; }
        };
    }

    private void BuildSurface()
    {
        var shell = new TableLayoutPanel {
            Name = "UiShell", Dock = DockStyle.Fill, BackColor = Page,
            ColumnCount = 1, RowCount = 2, Padding = new Padding(20, 14, 20, 18)
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(shell);

        var header = new AntdUI.Panel {
            Name = "HeaderCard", Dock = DockStyle.Fill, Back = Color.White,
            BorderColor = CardBorder, BorderWidth = 1, BorderStyle = DashStyle.Solid,
            Radius = 18, Shadow = 4, ShadowOpacity = 8, ShadowOpacityHover = 14,
            ShadowOpacityAnimation = true, Padding = new Padding(14, 12, 18, 12),
            Margin = new Padding(6, 0, SystemInformation.VerticalScrollBarWidth + 8, 12)
        };
        var headerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 252));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var brand = new Label {
            Name = "BrandTitle", Text = "NTE  ภาษาไทย", AutoSize = false, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold), ForeColor = BlueInk, BackColor = Color.Transparent,
            AccessibleName = "NTE ภาษาไทย"
        };
        var headerRight = new FlowLayoutPanel {
            Name = "HeaderActions", Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty, Padding = Padding.Empty
        };
        var release = new Label {
            Name = "ReleaseBadge", Text = "NTE 1.4.5", AutoSize = false, Width = 145, Height = 34,
            TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(88, 108, 138), BackColor = Color.Transparent,
            AccessibleName = "รุ่นเกม NTE 1.4.5", Margin = new Padding(0, 4, 8, 0)
        };
        themeToggle = new CheckBox {
            Name = "ThemeToggle", Text = "สว่าง", AutoSize = false, Width = 86, Height = 34,
            Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleCenter, CheckAlign = ContentAlignment.MiddleCenter,
            TabStop = true, AccessibleName = "สลับธีมมืดหรือสว่าง",
            AccessibleDescription = "บันทึกธีมที่เลือกไว้สำหรับครั้งถัดไป", Margin = new Padding(0, 4, 0, 0)
        };
        themeToggle.CheckedChanged += (s, e) => {
            if (!initializingThemeToggle) SetTheme(themeToggle.Checked ? LauncherTheme.Light : LauncherTheme.Dark, true);
        };
        headerRight.Controls.Add(release);
        headerRight.Controls.Add(themeToggle);
        headerLayout.Controls.Add(brand, 0, 0);
        headerLayout.Controls.Add(headerRight, 1, 0);
        header.Controls.Add(headerLayout);
        shell.Controls.Add(header, 0, 0);

        tabs = new DoubleBufferedTabs {
            Name = "MainTabs", Dock = DockStyle.Fill, BackColor = Page,
            ForeColor = BlueInk, Alignment = TabAlignment.Top, Type = AntdUI.TabType.Card,
            Style = new AntdUI.Tabs.StyleCard {
                Radius = 12, Border = 1, BorderColor = CardBorder,
                BorderActive = Blue, Fill = Color.White, FillHover = Info,
                FillActive = Color.White, Gap = 8, Closable = false
            },
            EnableSwitch = true, EnablePageScrolling = true, TabStop = true,
            ItemSize = 46, Padding = new Padding(0, 8, 0, 0)
        };
        var translatePage = NewTabPage("แปลเกม", "TranslatePage");
        var fontPage = NewTabPage("ฟอนต์", "FontPage");
        var statusPage = NewTabPage("สถานะ", "StatusPage");
        BuildTranslatePage(translatePage);
        BuildFontPage(fontPage);
        BuildStatusPage(statusPage);
        tabs.Pages.Add(translatePage);
        tabs.Pages.Add(fontPage);
        tabs.Pages.Add(statusPage);
        tabs.SelectedIndex = 0;
        shell.Controls.Add(tabs, 0, 1);
    }

    private static AntdUI.TabPage NewTabPage(string title, string name)
    {
        return new AntdUI.TabPage {
            Text = title, Name = name, Dock = DockStyle.Fill, AutoScroll = false,
            BackColor = Page, Padding = new Padding(0, 6, 0, 4), TabStop = true,
            AccessibleName = title
        };
    }

    private sealed class DoubleBufferedTabs : AntdUI.Tabs
    {
        private bool preparingPages;
        public DoubleBufferedTabs()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (preparingPages || Pages == null || DisplayRectangle.Width < 100) return;
            preparingPages = true;
            try {
                // AntdUI keeps inactive pages off screen and schedules selection.
                // Give them the final viewport size before the first reveal so
                // no intermediate one-pixel layout is ever painted.
                foreach (AntdUI.TabPage page in Pages) {
                    if (!page.Showed && page.Size != DisplayRectangle.Size)
                        page.Size = DisplayRectangle.Size;
                    page.PerformLayout();
                }
            }
            finally { preparingPages = false; }
        }
    }

    // Compositing buffers descendant HWNDs together; buffering only the scroll
    // parent's background cannot protect transparent labels/cards during movement.
    protected override CreateParams CreateParams {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x02000000; return cp; }
    }

    private static SmoothStack NewStack(Control page)
    {
        var stack = new SmoothStack {
            Name = page.Name + "Stack", Dock = DockStyle.Fill,
            Vertical = true, AutoScroll = true, Back = Page, BackColor = Page,
            Padding = new Padding(3, 2, SystemInformation.VerticalScrollBarWidth + 5, 12),
            Margin = Padding.Empty, TabStop = false
        };
        page.Controls.Add(stack);
        return stack;
    }

    private sealed class SmoothStack : AntdUI.StackPanel
    {
        private readonly System.Windows.Forms.Timer scrollTimer;
        private float targetY;
        private bool settingPosition;
        private bool adjustingRail;

        public SmoothStack()
        {
            DoubleBuffered = true;
            scrollTimer = new System.Windows.Forms.Timer { Interval = 15 };
            scrollTimer.Tick += AnimateScroll;
            VisibleChanged += (s, e) => { if (Visible) PerformLayout(); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (YScroll != null) {
                YScroll.AccessibleName = "เลื่อนหน้า";
                YScroll.TabStop = true;
                YScroll.ValueChanged += (s, a) => {
                    if (!settingPosition) { targetY = YScroll.Value; scrollTimer.Stop(); }
                };
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (adjustingRail || !Visible || YScroll == null) return;
            bool needsRail = YScroll.Maximum > YScroll.Height && YScroll.Height > 0;
            int right = needsRail ? 5 : SystemInformation.VerticalScrollBarWidth + 5;
            if (Padding.Right == right) return;
            adjustingRail = true;
            try { Padding = new Padding(3, 2, right, 12); }
            finally { adjustingRail = false; }
        }

        public void AttachWheelHandlers(Control control)
        {
            control.MouseWheel += OnSmoothWheel;
            control.ControlAdded += (s, e) => AttachWheelHandlers(e.Control);
            control.Enter += (s, e) => RevealControl(control);
            foreach (Control child in control.Controls) AttachWheelHandlers(child);
        }

        private void RevealControl(Control control)
        {
            if (YScroll == null || !Visible || !control.Visible) return;
            Control card = control;
            while (card.Parent != null && card.Parent.Name != "__IN__") card = card.Parent;
            if (card.Parent == null) return;
            Rectangle view = card.Parent.ClientRectangle;
            Rectangle bounds = card.Parent.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            YScroll.SetValue(view, bounds);
        }

        private void OnSmoothWheel(object sender, MouseEventArgs e)
        {
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
            if (e.Delta == 0 || YScroll == null || !YScroll.Visible) return;
            int max = Math.Max(0, YScroll.Maximum - YScroll.Height);
            float current = YScroll.Value;
            if (!scrollTimer.Enabled) targetY = current;
            targetY = Math.Max(0, Math.Min(max, targetY - (e.Delta / 120f) * 88f));
            scrollTimer.Start();
        }

        private void AnimateScroll(object sender, EventArgs e)
        {
            if (YScroll == null || !Visible) { scrollTimer.Stop(); return; }
            targetY = Math.Max(0, Math.Min(Math.Max(0, YScroll.Maximum - YScroll.Height), targetY));
            float remaining = targetY - YScroll.Value;
            int next = Math.Abs(remaining) < 1.25f ? (int)Math.Round(targetY)
                : (int)Math.Round(YScroll.Value + remaining * 0.32f);
            settingPosition = true;
            try {
                YScroll.Value = next;
                if (Math.Abs(targetY - next) < 1.25f) {
                    YScroll.Value = (int)Math.Round(targetY);
                    scrollTimer.Stop();
                }
            }
            finally { settingPosition = false; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { scrollTimer.Stop(); scrollTimer.Dispose(); }
            base.Dispose(disposing);
        }
    }

    private static AntdUI.Panel NewCard(SmoothStack stack, string name, int height)
    {
        var card = new AntdUI.Panel {
            Name = name, Width = Math.Max(500, stack.ClientSize.Width - 30), Height = height,
            Back = Color.White, BorderColor = CardBorder, BorderWidth = 1,
            BorderStyle = DashStyle.Solid, Radius = 18, Shadow = 4,
            ShadowOpacity = 8, ShadowOpacityHover = 14, ShadowOpacityAnimation = true,
            Padding = new Padding(14), Margin = new Padding(0, 0, 0, 10),
            AccessibleRole = AccessibleRole.Grouping
        };
        stack.Add(card);
        card.BringToFront();
        stack.AttachWheelHandlers(card);
        return card;
    }

    private static TableLayoutPanel CardRows(AntdUI.Panel card, params int[] heights)
    {
        var rows = new TableLayoutPanel {
            Name = card.Name + "Rows", Dock = DockStyle.Fill, ColumnCount = 1,
            RowCount = heights.Length, BackColor = Color.Transparent, Margin = Padding.Empty,
            Padding = Padding.Empty, GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < heights.Length; i++) {
            rows.RowStyles.Add(new RowStyle(SizeType.Absolute, heights[i]));
        }
        card.Controls.Add(rows);
        return rows;
    }

    private static Label Heading(string text, float size = 16f)
    {
        return new Label { Name = "HeadingLabel", Text = text, AutoSize = false, Dock = DockStyle.Fill, BackColor = Color.Transparent,
            Font = new Font("Leelawadee UI", size, FontStyle.Bold), ForeColor = BlueInk,
            TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
    }

    private static Label Body(string text, int height = 42)
    {
        return new Label { Text = text, AutoSize = false, Dock = DockStyle.Fill, BackColor = Color.Transparent,
            Font = new Font("Leelawadee UI", 10.5f), ForeColor = Color.FromArgb(91, 108, 132),
            TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
    }

    private void BuildTranslatePage(AntdUI.TabPage page)
    {
        var stack = NewStack(page);
        var action = NewCard(stack, "TranslationCard", 420);
        var actionRows = CardRows(action, 34, 24, 32, 28, 64, 56, 24, 18, 48, 42);
        actionRows.Controls.Add(Heading("ภาษาไทยในเกมของคุณ", 18f), 0, 0);
        actionRows.Controls.Add(Body("เริ่มแปลเกมและติดตามสถานะได้จากหน้านี้", 28), 0, 1);
        mainGameBadge = new AntdUI.Panel { Name = "MainGameBadge", Dock = DockStyle.Fill, Back = Info,
            BackColor = Color.Transparent, BorderColor = CardBorder, BorderWidth = 1,
            BorderStyle = DashStyle.Solid, Radius = 12, Shadow = 0, Padding = new Padding(9, 0, 9, 0),
            Margin = Padding.Empty, AccessibleRole = AccessibleRole.Grouping };
        mainGameHint = new Label { Name = "MainGameHint", Dock = DockStyle.Fill, AutoSize = false,
            Text = "เกมปิดอยู่  •  ภาษาเกม: English", TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent, Padding = Padding.Empty, Margin = Padding.Empty,
            AccessibleName = "สถานะเกมและภาษา" };
        mainGameBadge.Controls.Add(mainGameHint);
        actionRows.Controls.Add(mainGameBadge, 0, 2);
        mainFontHint = new Label { Name = "MainFontHint", Dock = DockStyle.Fill, AutoSize = false,
            Text = "TH: Default     EN: Default", TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty, AccessibleName = "ฟอนต์ภาษาไทยและอังกฤษที่เลือก" };
        actionRows.Controls.Add(mainFontHint, 0, 3);
        translateButton = NewPrimaryButton("แปลเกม", "EnableTranslation", 72);
        translateButton.AccessibleName = "แปลเกม";
        translateButton.AccessibleDescription = "เริ่มแปลเกมด้วยฟอนต์ที่เลือก";
        translateButton.Dock = DockStyle.Fill;
        translateButton.Margin = new Padding(0, 2, 0, 2);
        translateButton.Click += (s, e) => RunTranslation();
        actionRows.Controls.Add(translateButton, 0, 4);
        operationProgress = new AnimatedProgressControl {
            Name = "OperationProgress", Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 1),
            AccessibleName = "ความคืบหน้าการแปลเกม", AccessibleDescription = "แสดงความคืบหน้าจากขั้นตอนจริง"
        };
        actionRows.Controls.Add(operationProgress, 0, 5);
        progressText = new Label { Name = "ProgressText", Dock = DockStyle.Fill, BackColor = Color.Transparent,
            Text = "ความคืบหน้าจะแสดงเมื่อเริ่มแปล", ForeColor = Color.FromArgb(91, 108, 132),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        actionRows.Controls.Add(progressText, 0, 6);
        statusBadge = new AntdUI.Panel {
            Name = "StatusBadge", Dock = DockStyle.Fill, Height = 58, Back = Color.White,
            BackColor = Color.Transparent, BorderColor = CardBorder, BorderWidth = 1,
            BorderStyle = DashStyle.Solid, Radius = 18, Shadow = 4, ShadowOpacity = 8,
            ShadowOpacityHover = 14, Padding = new Padding(12, 4, 12, 4), Margin = Padding.Empty
        };
        statusLabel = new Label {
            Name = "ReadOnlyStatus", Dock = DockStyle.Fill, Height = 58, AutoSize = false,
            BorderStyle = BorderStyle.None, Padding = Padding.Empty, BackColor = Color.Transparent,
            Font = new Font("Leelawadee UI", 10.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = false, TabStop = true,
            AccessibleName = "สถานะการทำงาน", AccessibleDescription = "สถานะการแปลเกม"
        };
        statusBadge.Controls.Add(statusLabel);
        statusBadge.SizeChanged += (s, e) => ResizeStatusArea();
        actionRows.Controls.Add(Heading("สถานะการทำงาน", 12f), 0, 7);
        actionRows.Controls.Add(statusBadge, 0, 8);
        var actionButtons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
        actionButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actionButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        restoreButton = NewSecondaryButton("คืนค่าเดิม", "RestoreOriginal");
        restoreButton.AccessibleDescription = "ลบไฟล์คำแปลและคืนค่าเดิม";
        restoreButton.Click += (s, e) => RunRestore();
        var guide = NewSecondaryButton("วิธีใช้", "OpenGuide");
        guide.Click += (s, e) => OpenGuide();
        actionButtons.Controls.Add(restoreButton, 0, 0);
        actionButtons.Controls.Add(guide, 1, 0);
        actionRows.Controls.Add(actionButtons, 0, 9);

        var rootCard = NewCard(stack, "GameRootCard", 156);
        var rootRows = CardRows(rootCard, 26, 28, 48);
        rootRows.Controls.Add(Heading("ตำแหน่งเกม"), 0, 0);
        rootRows.Controls.Add(Body("เลือกโฟลเดอร์ที่ติดตั้งเกมไว้", 28), 0, 1);
        var rootRow = new TableLayoutPanel { Name = "GameRootRow", Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
        rootRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rootRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
        gameRoot = new AntdUI.Input {
            Name = "GameRoot", Dock = DockStyle.Fill, ReadOnly = true, Radius = 12, BorderWidth = 1, WaveSize = 0,
            BackColor = Color.FromArgb(248, 250, 253), ForeColor = BlueInk, TabStop = true,
            AccessibleName = "ตำแหน่งเกม", AccessibleDescription = "ตำแหน่งโฟลเดอร์เกมที่บันทึกไว้"
        };
        browseButton = NewSecondaryButton("เลือกโฟลเดอร์", "GameRootBrowse");
        gameRoot.Margin = new Padding(0, 0, 12, 0);
        browseButton.Margin = Padding.Empty;
        rootRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        browseButton.Click += (s, e) => BrowseGameRoot();
        rootRow.Controls.Add(gameRoot, 0, 0);
        rootRow.Controls.Add(browseButton, 1, 0);
        rootRows.Controls.Add(rootRow, 0, 2);
    }

    private void BuildFontPage(AntdUI.TabPage page)
    {
        var stack = NewStack(page);
        fontLibraryCard = NewCard(stack, "FontLibraryCard", 402);
        fontLibraryRows = CardRows(fontLibraryCard, 26, 0, 26, 42, 42, 44, 28, 42, 44, 28, 42);
        fontLibraryRows.Controls.Add(Heading("คลังฟอนต์", 16f), 0, 0);
        fontLibraryError = new Label {
            Name = "FontLibraryError", Dock = DockStyle.Fill, AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Visible = false,
            BackColor = Color.Transparent, ForeColor = RedInk, Font = new Font("Leelawadee UI", 9.5f, FontStyle.Bold),
            AccessibleName = "ปัญหาคลังฟอนต์", AccessibleRole = AccessibleRole.Alert, Margin = Padding.Empty
        };
        fontLibraryRows.Controls.Add(fontLibraryError, 0, 1);
        fontLibraryRows.Controls.Add(Body("ค่าเดิมของเกมถูกป้องกันไว้ เลือกฟอนต์ในคลังหรือเพิ่มไฟล์ TTF", 26), 0, 2);
        fontLibraryRows.Controls.Add(Heading("ภาษาไทย", 12f), 0, 3);
        thaiFontLibrary = NewFontLibrary("ThaiFontLibrary", "เลือกฟอนต์ภาษาไทย");
        thaiFontLibrary.SelectedIndexChanged += (s, e) => OnFontLibrarySelectionChanged(FontRole.Thai);
        fontLibraryRows.Controls.Add(thaiFontLibrary, 0, 4);
        var thaiActions = NewButtonRow("ThaiFontActions");
        fontImportButton = NewPrimaryButton("เพิ่มฟอนต์ TTF", "FontImport", 40);
        fontImportButton.AccessibleDescription = "เพิ่มไฟล์ฟอนต์ TrueType ภาษาไทยลงในคลัง";
        fontImportButton.Click += (s, e) => BrowseFont(FontRole.Thai);
        fontDeleteButton = NewSecondaryButton("ลบออกจากคลัง", "FontDelete");
        fontDeleteButton.Click += (s, e) => DeleteSelectedFont(FontRole.Thai);
        thaiActions.Controls.Add(fontImportButton, 0, 0);
        thaiActions.Controls.Add(fontDeleteButton, 1, 0);
        fontLibraryRows.Controls.Add(thaiActions, 0, 5);
        fontLibraryRows.Controls.Add(Heading("English", 12f), 0, 6);
        englishFontLibrary = NewFontLibrary("EnglishFontLibrary", "Choose an English font");
        englishFontLibrary.SelectedIndexChanged += (s, e) => OnFontLibrarySelectionChanged(FontRole.English);
        fontLibraryRows.Controls.Add(englishFontLibrary, 0, 7);
        var englishActions = NewButtonRow("EnglishFontActions");
        englishFontImportButton = NewPrimaryButton("Add TTF", "EnglishFontImport", 40);
        englishFontImportButton.AccessibleDescription = "Add a TrueType font for English text";
        englishFontImportButton.Click += (s, e) => BrowseFont(FontRole.English);
        englishFontDeleteButton = NewSecondaryButton("Remove from library", "EnglishFontDelete");
        englishFontDeleteButton.Click += (s, e) => DeleteSelectedFont(FontRole.English);
        englishActions.Controls.Add(englishFontImportButton, 0, 0);
        englishActions.Controls.Add(englishFontDeleteButton, 1, 0);
        fontLibraryRows.Controls.Add(englishActions, 0, 8);
        fontLibraryRows.Controls.Add(Body("เปลี่ยนฟอนต์ได้เมื่อคืนค่าเกมเดิมแล้ว", 28), 0, 9);
        fontLibraryRows.Controls.Add(Body("ตัวอย่างด้านล่างไม่ติดตั้งฟอนต์ลง Windows", 28), 0, 10);

        var previewCard = NewCard(stack, "FontPreviewCard", 250);
        var previewRows = CardRows(previewCard, 26, 28, 140);
        previewRows.Controls.Add(Heading("ตัวอย่างภาษาไทยและ English"), 0, 0);
        previewRows.Controls.Add(Body("แต่ละบรรทัดแสดงด้วยฟอนต์ที่เลือกไว้", 28), 0, 1);
        fontPreview = new FontPreviewControl {
            Name = "FontPreview", Dock = DockStyle.Fill,
            AccessibleName = "ตัวอย่างฟอนต์ไทยและอังกฤษ", AccessibleDescription = "สองบรรทัดนี้ใช้ฟอนต์ไทยและอังกฤษแยกกัน"
        };
        previewRows.Controls.Add(fontPreview, 0, 2);

    }

    private AntdUI.Select NewFontLibrary(string name, string accessibleName)
    {
        return new AntdUI.Select {
            Name = name, Dock = DockStyle.Fill, List = true,
            MaxCount = 6, Radius = 12, DropDownRadius = 12, WaveSize = 0,
            BorderWidth = 1, WheelModifyEnabled = false,
            Font = new Font("Leelawadee UI", 10.5f), Margin = new Padding(4, 2, 4, 2),
            TabStop = true, AccessibleName = accessibleName
        };
    }

    private static string FontChoiceDisplayText(FontChoice choice)
    {
        if (choice == null) return String.Empty;
        return choice.Name + ((choice.IsDefault || String.Equals(choice.Id, "default", StringComparison.Ordinal))
            ? "  •  ค่าเดิม" : String.Empty);
    }

    private static TableLayoutPanel NewButtonRow(string name)
    {
        var row = new TableLayoutPanel { Name = name, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = Color.Transparent, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        return row;
    }

    private void SetTheme(LauncherTheme value, bool persist)
    {
        int selectedTab = tabs == null ? 0 : tabs.SelectedIndex;
        currentTheme = value;
        palette = ThemePalette.For(value);
        AntdUI.Config.Mode = value == LauncherTheme.Dark ? AntdUI.TMode.Dark : AntdUI.TMode.Light;
        AntdUI.Style.SetPrimary(palette.Accent);
        AntdUI.Style.Set(AntdUI.Colour.BgElevated, palette.Card, "Select");
        AntdUI.Style.Set(AntdUI.Colour.Text, palette.Text, "Select");
        AntdUI.Style.Set(AntdUI.Colour.TextBase, palette.Text, "Select");
        AntdUI.Style.Set(AntdUI.Colour.TextQuaternary, palette.Muted, "Select");
        AntdUI.Style.Set(AntdUI.Colour.FillTertiary, palette.StatusInfo, "Select");
        AntdUI.Style.Set(AntdUI.Colour.PrimaryBg, palette.StatusInfo, "Select");
        AntdUI.Style.Set(AntdUI.Colour.BorderColor, palette.Border, "Select");
        AntdUI.Style.Set(AntdUI.Colour.TextBase, palette.Muted, "ScrollBar");
        BackColor = palette.Window;
        ForeColor = palette.Text;
        SuspendLayout();
        try {
            ApplyThemeTo(this);
            if (tabs != null) {
                tabs.TabMenuVisible = true;
                if (tabs.SelectedIndex != selectedTab && selectedTab >= 0 && selectedTab < tabs.Pages.Count)
                    tabs.SelectedIndex = selectedTab;
            }
        }
        finally { ResumeLayout(true); }
        if (tabs != null) { tabs.Invalidate(true); if (tabs.IsHandleCreated) tabs.Update(); }
        initializingThemeToggle = true;
        if (themeToggle != null) {
            themeToggle.Checked = value == LauncherTheme.Light;
            themeToggle.Text = value == LauncherTheme.Light ? "มืด" : "สว่าง";
        }
        initializingThemeToggle = false;
        if (operationProgress != null) operationProgress.ApplyTheme(palette);
        if (fontPreview != null) fontPreview.ApplyTheme(palette);
        if (statusLabel != null) SetStatus(statusLabel.Tag as string ?? "idle", statusLabel.Text);
        if (IsHandleCreated) WindowChrome.Apply(this, palette);
        UpdatePaletteApplied();
        if (persist) {
            try { themeSettings.Save(value); }
            catch (Exception ex) { SetStatus("error", "บันทึกธีมไม่ได้\r\n" + ex.Message); }
        }
    }

    private void ApplyThemeTo(Control control)
    {
        if (control == null) return;
        if (control is Form) {
            control.BackColor = palette.Window;
            control.ForeColor = palette.Text;
        }
        else if (control is AntdUI.Panel) {
            var panel = (AntdUI.Panel)control;
            panel.Back = control.Name == "MainGameBadge" ? palette.StatusInfo : palette.Card;
            panel.BackColor = Color.Transparent;
            panel.BorderColor = palette.Border;
        }
        else if (control is AntdUI.Tabs) {
            var themedTabs = (AntdUI.Tabs)control;
            themedTabs.BackColor = palette.Page;
            themedTabs.ForeColor = palette.Text;
            themedTabs.Style = new AntdUI.Tabs.StyleCard {
                Radius = 12, Border = 1, BorderColor = palette.Border,
                BorderActive = palette.Accent, Fill = palette.Card, FillHover = palette.StatusInfo,
                FillActive = palette.Card, Gap = 8, Closable = false
            };
        }
        else if (control is AntdUI.TabPage) {
            control.BackColor = palette.Page;
            control.ForeColor = palette.Text;
        }
        else if (control is AntdUI.Button) {
            var button = (AntdUI.Button)control;
            bool primary = button.Name == "EnableTranslation" || button.Name == "FontImport" || button.Name == "EnglishFontImport" || button.Name == "AppUpdateButton";
            button.BackColor = primary ? palette.Accent : palette.Card;
            button.DefaultBack = primary ? palette.Accent : palette.Card;
            button.ForeColor = primary ? Color.White : palette.Text;
            button.BackHover = primary ? palette.AccentHover : palette.StatusInfo;
            button.BackActive = primary ? palette.AccentHover : palette.StatusInfo;
            button.ForeHover = primary ? Color.White : palette.Accent;
            button.DefaultBorderColor = palette.Border;
        }
        else if (control is AntdUI.Input) {
            var input = (AntdUI.Input)control;
            input.BackColor = palette.Card;
            input.ForeColor = palette.Text;
            input.BorderColor = palette.Border;
            input.BorderHover = palette.Accent;
            input.BorderActive = palette.Accent;
            input.SelectionColor = palette.StatusInfo;
        }
        else if (control is SmoothStack) {
            var stack = (SmoothStack)control;
            stack.Back = palette.Page;
            stack.BackColor = palette.Page;
        }
        else if (control is AntdUI.YScrollBar) {
            control.BackColor = palette.Page;
            control.ForeColor = palette.Muted;
        }
        else if (control is CheckBox) {
            ((CheckBox)control).FlatAppearance.BorderColor = palette.Border;
            control.BackColor = palette.Card;
            control.ForeColor = palette.Text;
        }
        else if (control is Label) {
            control.BackColor = Color.Transparent;
            control.ForeColor = (control.Name == "BrandTitle" || control.Name == "HeadingLabel"
                || control.Name == "FontSummary" || control.Name == "EnglishFontSummary")
                ? palette.Text : palette.Muted;
            if (control.Name == "MainGameHint") {
                control.BackColor = Color.Transparent;
                control.ForeColor = palette.StatusInfoText;
            }
            else if (control.Name == "FontLibraryError") control.ForeColor = palette.StatusErrorText;
            else if (control.Name == "MainFontHint") control.ForeColor = palette.Text;
        }
        else if (control is TableLayoutPanel || control is FlowLayoutPanel) {
            control.BackColor = control.Name == "UiShell" || control.Name.EndsWith("Stack", StringComparison.Ordinal)
                ? palette.Page : Color.Transparent;
        }
        else {
            control.BackColor = palette.Page;
            control.ForeColor = palette.Text;
        }
        foreach (Control child in control.Controls) ApplyThemeTo(child);
    }

    private void BuildStatusPage(AntdUI.TabPage page)
    {
        var stack = NewStack(page);
        var current = NewCard(stack, "StatusOverviewCard", 188);
        var currentRows = CardRows(current, 26, 32, 78);
        currentRows.Controls.Add(Heading("สถานะโปรแกรม"), 0, 0);
        currentRows.Controls.Add(Body("ตำแหน่งเกมและผลการทำงานล่าสุด", 32), 0, 1);
        statusDetail = new Label { Name = "StatusDetail", Dock = DockStyle.Fill, BackColor = Color.Transparent,
            AutoSize = false, ForeColor = Color.FromArgb(91, 108, 132), TextAlign = ContentAlignment.TopLeft,
            AccessibleName = "รายละเอียดสถานะ" };
        currentRows.Controls.Add(statusDetail, 0, 2);
        statusDetail.SizeChanged += (s, e) => ResizeStatusDetail();
        statusDetail.TextChanged += (s, e) => ResizeStatusDetail();
        statusDetail.HandleCreated += (s, e) => ResizeStatusDetail();

        var steps = NewCard(stack, "StatusStepsCard", 214);
        var stepRows = CardRows(steps, 26, 128);
        stepRows.Controls.Add(Heading("วิธีใช้งาน"), 0, 0);
        stepRows.Controls.Add(Body("1. เลือกตำแหน่งเกม\r\n2. เลือกฟอนต์\r\n3. ปิดเกมแล้วกดแปล\r\n4. หลังเล่น ปิดโปรแกรมได้โดยคงคำแปลไว้\r\n5. คืนค่าเดิมเมื่อเปลี่ยนฟอนต์หรืออัปเดตเกม", 128), 0, 1);

        var guideCard = NewCard(stack, "StatusGuideCard", 162);
        var guideRows = CardRows(guideCard, 26, 38, 42);
        guideRows.Controls.Add(Heading("คำแนะนำ"), 0, 0);
        guideRows.Controls.Add(Body("อ่านวิธีใช้ที่มาพร้อมโปรแกรม", 38), 0, 1);
        var guide = NewSecondaryButton("เปิดวิธีใช้", "OpenGuideStatus");
        guide.Dock = DockStyle.Fill;
        guide.Click += (s, e) => OpenGuide();
        guideRows.Controls.Add(guide, 0, 2);
    }

    private static AntdUI.Button NewPrimaryButton(string text, string name, int height)
    {
        return new AntdUI.Button {
            Name = name, Text = text, Dock = DockStyle.Top, Height = height,
            Type = AntdUI.TTypeMini.Primary, BackColor = Blue, ForeColor = Color.White,
            ForeHover = Color.White, BackHover = Color.FromArgb(64, 143, 255),
            BackActive = Color.FromArgb(9, 88, 217), BorderWidth = 0, Radius = 14,
            WaveSize = 5, TextMultiLine = false, AutoEllipsis = true,
            Font = new Font("Leelawadee UI", 12f, FontStyle.Bold),
            Margin = new Padding(0, 3, 0, 3), TabStop = true, AccessibleRole = AccessibleRole.PushButton
        };
    }

    private static AntdUI.Button NewSecondaryButton(string text, string name)
    {
        return new AntdUI.Button {
            Name = name, Text = text, Dock = DockStyle.Fill, Height = 42,
            Type = AntdUI.TTypeMini.Default, BackColor = Color.White,
            ForeColor = BlueInk, BackHover = Info, ForeHover = Blue,
            BorderWidth = 1, DefaultBorderColor = CardBorder, Radius = 12,
            WaveSize = 4, Font = new Font("Leelawadee UI", 10.5f, FontStyle.Bold),
            Margin = new Padding(4), TabStop = true, AccessibleRole = AccessibleRole.PushButton
        };
    }

    private void OnFormHandleCreated(object sender, EventArgs e)
    {
        if (inspectStarted) return;
        inspectStarted = true;
        WindowChrome.Apply(this, palette);
        refreshTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        refreshTimer.Tick += (s, args) => RefreshSnapshotAsync(false);
        refreshTimer.Start();
        RefreshSnapshotAsync(true);
    }

    private void RefreshSnapshotAsync(bool initial)
    {
        if (busy || IsDisposed || Disposing || Interlocked.CompareExchange(ref inspectInFlight, 1, 0) != 0) return;
        int generation = Volatile.Read(ref refreshGeneration);
        Task.Run(() => {
            LauncherSnapshot current = null;
            Exception failure = null;
            try { current = service.Inspect(); }
            catch (Exception ex) { failure = ex; }
            finally { Interlocked.Exchange(ref inspectInFlight, 0); }
            if (Volatile.Read(ref refreshGeneration) != generation) return;
            if (failure != null) PostToUi(() => {
                if (Volatile.Read(ref refreshGeneration) == generation && !busy) SetStatus("error", UserMessage(failure));
            });
            else PostToUi(() => {
                if (Volatile.Read(ref refreshGeneration) == generation && !busy) ApplySnapshot(current, initial, true);
            });
        });
    }

    private void OnFormClosingGuard(object sender, FormClosingEventArgs e)
    {
        if (!busy) return;
        e.Cancel = true;
        SetStatus("busy", "กำลังทำงาน กรุณารอให้เสร็จก่อนปิดหน้าต่าง");
    }

    private void RunTranslation()
    {
        string kind = snapshot != null && snapshot.Active ? "verify" : "translate";
        RunServiceOperation(kind, progress => service.Translate(progress));
    }

    private void RefreshFontLibraries(LauncherSnapshot current)
    {
        if (current == null) return;
        refreshingFontLibraries = true;
        try {
            RefreshFontLibrary(thaiFontLibrary, current.ThaiFonts, current.FontName, current.FontIsDefault, current.FontId);
            RefreshFontLibrary(englishFontLibrary, current.EnglishFonts, current.EnglishFontName, current.EnglishFontIsDefault, current.EnglishFontId);
        }
        finally { refreshingFontLibraries = false; }
    }

    private static void RefreshFontLibrary(AntdUI.Select library, FontChoice[] choices, string selectedName,
        bool selectedDefault, string selectedId)
    {
        if (library == null) return;
        int choiceCount = 0;
        if (choices != null) foreach (FontChoice choice in choices) if (choice != null) choiceCount++;
        bool contentsUnchanged = library.Items.Count == choiceCount;
        if (contentsUnchanged) {
            int currentIndex = 0;
            foreach (FontChoice choice in choices) {
                if (choice == null) continue;
                var item = library.Items[currentIndex++] as AntdUI.SelectItem;
                var existing = item == null ? null : item.Tag as FontChoice;
                if (existing == null || !String.Equals(existing.Id, choice.Id, StringComparison.Ordinal)
                    || !String.Equals(existing.Name, choice.Name, StringComparison.Ordinal)
                    || existing.IsDefault != choice.IsDefault || existing.CanDelete != choice.CanDelete) {
                    contentsUnchanged = false;
                    break;
                }
            }
        }
        FontChoice selected = library.SelectedValue as FontChoice;
        bool selectionUnchanged = selected != null && (String.IsNullOrEmpty(selectedId)
            ? (selected.IsDefault || String.Equals(selected.Id, "default", StringComparison.Ordinal)) == selectedDefault
                && String.Equals(selected.Name, selectedName, StringComparison.Ordinal)
            : String.Equals(selected.Id, selectedId, StringComparison.Ordinal));
        if (contentsUnchanged && selectionUnchanged) return;

        library.SuspendLayout();
        try {
            library.Items.Clear();
            int selectedIndex = -1;
            if (choices != null) {
                foreach (FontChoice choice in choices) {
                    if (choice == null) continue;
                    int index = library.Items.Count;
                    library.Items.Add(new AntdUI.SelectItem(FontChoiceDisplayText(choice), choice));
                    bool isDefault = choice.IsDefault || String.Equals(choice.Id, "default", StringComparison.Ordinal);
                    bool isSelected = String.IsNullOrEmpty(selectedId)
                        ? isDefault == selectedDefault && String.Equals(choice.Name, selectedName, StringComparison.Ordinal)
                        : String.Equals(choice.Id, selectedId, StringComparison.Ordinal);
                    if (selectedIndex < 0 && isSelected) selectedIndex = index;
                }
            }
            if (selectedIndex < 0 && library.Items.Count > 0) selectedIndex = 0;
            library.SelectedIndex = selectedIndex;
        }
        finally { library.ResumeLayout(false); library.Invalidate(); }
    }

    private static void SetFontLibraryAvailability(AntdUI.Select library, AntdUI.Button deleteButton, bool canSelect)
    {
        if (library != null) library.Enabled = canSelect && library.Items.Count > 0;
        if (deleteButton != null) {
            FontChoice choice = library == null ? null : library.SelectedValue as FontChoice;
            deleteButton.Enabled = canSelect && choice != null && choice.CanDelete
                && !choice.IsDefault && !String.Equals(choice.Id, "default", StringComparison.Ordinal);
        }
    }

    private T FindControl<T>(string name) where T : Control
    {
        Control[] found = Controls.Find(name, true);
        return found.Length == 0 ? null : found[0] as T;
    }

    private void RunRestore()
    {
        RunServiceOperation("restore", progress => service.Restore(progress));
    }

    private void OnFontLibrarySelectionChanged(FontRole role)
    {
        if (refreshingFontLibraries || busy || snapshot == null || !snapshot.CanSelectFont) return;
        AntdUI.Select library = role == FontRole.Thai ? thaiFontLibrary : englishFontLibrary;
        FontChoice choice = library == null ? null : library.SelectedValue as FontChoice;
        if (choice == null) return;
        string currentName = role == FontRole.Thai ? snapshot.FontName : snapshot.EnglishFontName;
        string currentId = role == FontRole.Thai ? snapshot.FontId : snapshot.EnglishFontId;
        bool currentDefault = role == FontRole.Thai ? snapshot.FontIsDefault : snapshot.EnglishFontIsDefault;
        if (!String.IsNullOrEmpty(currentId)) {
            if (String.Equals(choice.Id, currentId, StringComparison.Ordinal)) return;
        }
        else if ((choice.IsDefault || String.Equals(choice.Id, "default", StringComparison.Ordinal)) == currentDefault
            && String.Equals(choice.Name, currentName, StringComparison.Ordinal)) return;
        string kind = role == FontRole.Thai ? "font-select-thai" : "font-select-english";
        RunServiceOperation(kind, progress => service.SelectFont(role, choice.Id));
    }

    private void DeleteSelectedFont(FontRole role)
    {
        if (busy || snapshot == null || !snapshot.CanSelectFont) return;
        AntdUI.Select library = role == FontRole.Thai ? thaiFontLibrary : englishFontLibrary;
        FontChoice choice = library == null ? null : library.SelectedValue as FontChoice;
        if (choice == null || !choice.CanDelete || choice.IsDefault) return;
        string kind = role == FontRole.Thai ? "font-delete-thai" : "font-delete-english";
        RunServiceOperation(kind, progress => service.DeleteFont(role, choice.Id));
    }

    internal bool ImportFontPath(string path) { return ImportFontPath(path, FontRole.Thai); }

    internal bool ImportFontPath(string path, FontRole role)
    {
        if (busy || snapshot == null || !snapshot.CanSelectFont) return false;
        if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)
            || !String.Equals(Path.GetExtension(path), ".ttf", StringComparison.OrdinalIgnoreCase)) {
            SetStatus("error", "เลือกไฟล์ฟอนต์ TrueType (.ttf) ที่มีอยู่");
            return false;
        }
        string fullPath = Path.GetFullPath(path);
        string kind = role == FontRole.Thai ? "font-thai" : "font-english";
        RunServiceOperation(kind, progress => service.ImportFont(fullPath, role, progress));
        return true;
    }

    private void RunServiceOperation(string kind, Action<Action<OperationProgress>> operation)
    {
        if (busy || snapshot == null || snapshot.GameRunning) return;
        if ((kind == "translate" || kind == "verify") && !snapshot.Ready) return;
        if (kind == "restore" && !snapshot.CanRestore) return;
        if (kind.StartsWith("font", StringComparison.Ordinal) && !snapshot.CanSelectFont) return;

        busy = true;
        Interlocked.Increment(ref refreshGeneration);
        float restoreStartValue = 0f;
        if (kind == "translate") operationProgress.BeginTranslation();
        else if (kind == "restore") restoreStartValue = operationProgress.BeginRestore();
        progressText.Text = "กำลังเริ่มขั้นตอน…";
        SetStatus("busy", BusyMessage(kind));
        UpdateAvailability();

        Task.Run(() => {
            LauncherSnapshot next = null;
            Exception failure = null;
            try {
                operation(progress => PostToUi(() => ApplyProgress(progress, kind, restoreStartValue)));
            }
            catch (Exception ex) { failure = ex; }
            try { next = service.Inspect(); }
            catch (Exception ex) { if (failure == null) failure = ex; }
            PostToUi(() => {
                busy = false;
                if (next != null) ApplySnapshot(next, false, false);
                UpdateAvailability();
                if (failure != null) {
                    if (kind == "translate" || kind == "verify" || kind == "restore") operationProgress.StopAtCurrentValue();
                    SetStatus("error", UserMessage(failure));
                    return;
                }
                if ((kind == "translate" || kind == "verify") && next != null && next.Active) {
                    operationProgress.MarkTranslationVerified();
                    SetStatus("success", "แปลเสร็จแล้ว");
                    progressText.Text = "ตรวจสอบและติดตั้งคำแปลเรียบร้อยแล้ว";
                }
                else if (kind == "restore" && next != null && !next.Active && !next.CanRestore) {
                    operationProgress.MarkRestoreVerified();
                    SetStatus("idle", "คืนค่าเดิมแล้ว");
                }
                else if (next != null && next.Active) {
                    SetStatus("success", "เปิดคำแปลอยู่");
                }
                else if (kind == "restore") {
                    operationProgress.StopAtCurrentValue();
                    SetStatus("error", "ยังคืนค่าไฟล์เดิมไม่ครบ");
                }
                else if (kind.StartsWith("font", StringComparison.Ordinal)) {
                    SetStatus("font", "เลือกฟอนต์แล้ว • กดแปลเกมเมื่อต้องการเริ่ม");
                }
                else if (kind == "root") {
                    SetStatus("idle", "บันทึกตำแหน่งเกมแล้ว");
                }
                else {
                    SetStatus("info", "ทำรายการเสร็จแล้ว แต่ยังตรวจสอบสถานะคำแปลไม่ได้");
                }
                UpdateAvailability();
            });
        });
    }

    private void ApplyProgress(OperationProgress progress, string kind, float restoreStartValue)
    {
        if (progress == null) return;
        string text = String.IsNullOrWhiteSpace(progress.Text) ? BusyMessage(kind) : progress.Text;
        SetStatus("busy", text);
        progressText.Text = text;
        if (kind == "verify") return;
        if (progress.TotalStages <= 0 || progress.CompletedStages < 0) {
            if (kind == "translate") operationProgress.ShowTranslationProgress(0f, false);
            return;
        }
        float fraction = Math.Max(0f, Math.Min(1f, (float)progress.CompletedStages / progress.TotalStages));
        if (kind == "translate") operationProgress.ShowTranslationProgress(fraction, true);
        else if (kind == "restore") operationProgress.ShowRestoreProgress(fraction, restoreStartValue);
    }

    private void ApplySnapshot(LauncherSnapshot current, bool initial, bool syncExternalProgress)
    {
        bool hadSnapshot = snapshot != null;
        bool previousActive = hadSnapshot && snapshot.Active;
        bool previousGameRunning = hadSnapshot && snapshot.GameRunning;
        snapshot = current;
        if (snapshot == null) {
            SetStatus("error", "ตรวจสอบสถานะไม่ได้");
            UpdateAvailability();
            return;
        }
        if (gameRoot != null) gameRoot.Text = snapshot.GameRoot ?? String.Empty;
        Label release = FindControl<Label>("ReleaseBadge");
        if (release != null) {
            string version = String.IsNullOrWhiteSpace(snapshot.GameVersion) ? "1.4.5" : snapshot.GameVersion.Trim();
            release.Text = "NTE " + version;
            release.AccessibleName = "รุ่นเกม NTE " + version;
        }
        RefreshFontLibraries(snapshot);
        if (fontLibraryError != null) {
            bool hasLibraryError = !String.IsNullOrWhiteSpace(snapshot.FontLibraryError);
            fontLibraryError.Text = hasLibraryError ? "โหลดคลังฟอนต์ไม่ได้: " + snapshot.FontLibraryError : String.Empty;
            fontLibraryError.AccessibleDescription = hasLibraryError ? snapshot.FontLibraryError : String.Empty;
            fontLibraryError.Visible = hasLibraryError;
            if (fontLibraryRows != null && fontLibraryRows.RowStyles.Count > 1)
                fontLibraryRows.RowStyles[1].Height = hasLibraryError ? 42f : 0f;
            if (fontLibraryCard != null) fontLibraryCard.Height = hasLibraryError ? 444 : 402;
        }
        if (mainGameHint != null)
            mainGameHint.Text = (snapshot.GameRunning ? "เกมกำลังเปิดอยู่" : "เกมปิดอยู่") + "  •  ภาษาเกม: English";
        if (mainFontHint != null) {
            string thai = String.IsNullOrWhiteSpace(snapshot.FontName) ? "Default" : snapshot.FontName;
            string english = String.IsNullOrWhiteSpace(snapshot.EnglishFontName) ? "Default" : snapshot.EnglishFontName;
            mainFontHint.Text = "TH: " + thai + "     EN: " + english;
        }
        if (fontPreview != null) fontPreview.SetFontFiles(snapshot.FontPreviewPath, snapshot.EnglishFontPreviewPath);
        if (statusDetail != null && (statusLabel == null || (statusLabel.Tag as string) != "error")) {
            string state = snapshot.Active ? "ติดตั้งคำแปลแล้ว" : snapshot.Ready ? "พร้อมใช้งาน" : "ยังไม่พร้อม";
            statusDetail.Text = state + (String.IsNullOrWhiteSpace(snapshot.Message) ? String.Empty : Environment.NewLine + snapshot.Message);
            ResizeStatusDetail();
        }
        if (initial) {
            operationProgress.SetExternalActive(snapshot.Active);
            if (snapshot.GameRunning) SetStatus("waiting", "กรุณาปิดเกมก่อนทำรายการ");
            else if (snapshot.Active) SetStatus("success", "เปิดคำแปลอยู่");
            else SetStatus("idle", String.IsNullOrWhiteSpace(snapshot.Message) ? "พร้อมใช้งาน" : snapshot.Message);
            progressText.Text = "เมื่อเริ่มแปลเกม จะแสดงขั้นตอนและผลที่นี่";
        }
        else if (syncExternalProgress && hadSnapshot && !busy
            && (statusLabel.Tag as string) != "busy" && (statusLabel.Tag as string) != "error"
            && previousActive != snapshot.Active) {
            operationProgress.SetExternalActive(snapshot.Active);
        }
        if (!initial && syncExternalProgress && hadSnapshot && !busy
            && (statusLabel.Tag as string) != "error" && (statusLabel.Tag as string) != "busy") {
            if (snapshot.GameRunning && !previousGameRunning)
                SetStatus("waiting", "กรุณาปิดเกมก่อนทำรายการ");
            else if (!snapshot.GameRunning && previousGameRunning && (statusLabel.Tag as string) == "waiting")
                SetStatus(snapshot.Active ? "success" : "idle", snapshot.Active ? "เปิดคำแปลอยู่"
                    : String.IsNullOrWhiteSpace(snapshot.Message) ? "พร้อมใช้งาน" : snapshot.Message);
        }
        UpdateAvailability();
    }

    private void UpdateAvailability()
    {
        bool has = snapshot != null;
        bool gameStopped = has && !snapshot.GameRunning;
        if (translateButton != null) {
            translateButton.Enabled = !busy && has && gameStopped && snapshot.Ready;
            translateButton.Loading = busy;
            translateButton.Text = has && snapshot.Active ? "ตรวจอีกครั้ง" : "แปลเกม";
            translateButton.AccessibleName = has && snapshot.Active ? "ตรวจคำแปลอีกครั้ง" : "แปลเกม";
            translateButton.AccessibleDescription = has && snapshot.Active
                ? "ตรวจสอบคำแปลที่ติดตั้งอยู่โดยไม่ติดตั้งซ้ำ" : "เริ่มแปลเกมด้วยฟอนต์ที่เลือก";
        }
        if (restoreButton != null) restoreButton.Enabled = !busy && has && gameStopped && snapshot.CanRestore;
        if (browseButton != null) browseButton.Enabled = !busy && has && gameStopped && !snapshot.Active && !snapshot.CanRestore;
        bool canSelect = !busy && has && gameStopped && snapshot.CanSelectFont
            && String.IsNullOrWhiteSpace(snapshot.FontLibraryError);
        if (fontImportButton != null) fontImportButton.Enabled = canSelect;
        if (englishFontImportButton != null) englishFontImportButton.Enabled = canSelect;
        SetFontLibraryAvailability(thaiFontLibrary, fontDeleteButton, canSelect);
        SetFontLibraryAvailability(englishFontLibrary, englishFontDeleteButton, canSelect);
        UpdateAvailabilityApplied();
    }

    private void SetStatus(string state, string text)
    {
        if (statusLabel == null) return;
        statusLabel.Tag = state;
        statusLabel.Text = text ?? String.Empty;
        ResizeStatusArea();
        if (statusBadge != null) {
            statusBadge.Back = state == "busy" || state == "waiting" ? palette.StatusBusy : state == "success" ? palette.StatusSuccess
                : state == "error" ? palette.StatusError : state == "font" ? palette.StatusInfo : palette.Card;
            statusBadge.BackColor = Color.Transparent;
            statusBadge.BorderColor = palette.Border;
        }
        switch (state) {
            case "busy": case "waiting": statusLabel.BackColor = Color.Transparent; statusLabel.ForeColor = palette.StatusBusyText; break;
            case "success": statusLabel.BackColor = Color.Transparent; statusLabel.ForeColor = palette.StatusSuccessText; break;
            case "error": statusLabel.BackColor = Color.Transparent; statusLabel.ForeColor = palette.StatusErrorText; break;
            case "font": statusLabel.BackColor = Color.Transparent; statusLabel.ForeColor = palette.StatusInfoText; break;
            case "loading": statusLabel.BackColor = Color.Transparent; statusLabel.ForeColor = palette.Accent; break;
            default: statusLabel.BackColor = Color.Transparent; statusLabel.ForeColor = palette.Text; break;
        }
        if (statusDetail != null && state != "loading") statusDetail.Text = text ?? String.Empty;
    }

    private void ResizeStatusArea()
    {
        if (resizingStatusArea || statusLabel == null || statusBadge == null || !statusBadge.IsHandleCreated) return;
        var rows = statusBadge.Parent as TableLayoutPanel;
        var card = rows == null ? null : rows.Parent as AntdUI.Panel;
        if (rows == null || card == null || rows.RowStyles.Count <= 8) return;

        int textWidth = Math.Max(100, statusBadge.ClientSize.Width - statusBadge.Padding.Horizontal - 4);
        Size measured = TextRenderer.MeasureText(statusLabel.Text ?? String.Empty, statusLabel.Font,
            new Size(textWidth, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        int rowHeight = Math.Max(48, measured.Height + statusBadge.Padding.Vertical + 12);
        RowStyle statusRow = rows.RowStyles[8];
        int oldHeight = (int)Math.Round(statusRow.Height);
        if (rowHeight == oldHeight) return;

        resizingStatusArea = true;
        try {
            statusRow.Height = rowHeight;
            card.Height += rowHeight - oldHeight;
            rows.PerformLayout();
        }
        finally { resizingStatusArea = false; }
    }

    private void ResizeStatusDetail()
    {
        if (resizingStatusDetail || statusDetail == null || !statusDetail.IsHandleCreated) return;
        var rows = statusDetail.Parent as TableLayoutPanel;
        var card = rows == null ? null : rows.Parent as AntdUI.Panel;
        if (rows == null || card == null || rows.RowStyles.Count <= 2) return;

        int textWidth = Math.Max(100, statusDetail.ClientSize.Width);
        Size measured = TextRenderer.MeasureText(statusDetail.Text ?? String.Empty, statusDetail.Font,
            new Size(textWidth, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        int rowHeight = Math.Max(78, measured.Height + 4);
        RowStyle detailRow = rows.RowStyles[2];
        int oldHeight = (int)Math.Round(detailRow.Height);
        if (rowHeight == oldHeight) return;

        resizingStatusDetail = true;
        try {
            detailRow.Height = rowHeight;
            card.Height += rowHeight - oldHeight;
            rows.PerformLayout();
        }
        finally { resizingStatusDetail = false; }
    }

    private void BrowseGameRoot()
    {
        if (busy || snapshot == null || snapshot.Active || snapshot.CanRestore || snapshot.GameRunning) return;
        using (var picker = new System.Windows.Forms.FolderBrowserDialog {
            Description = "เลือกโฟลเดอร์ Neverness to Everness",
            SelectedPath = snapshot.GameRoot ?? String.Empty,
            ShowNewFolderButton = false
        }) {
            if (picker.ShowDialog(this) == DialogResult.OK) {
                string selectedPath = picker.SelectedPath;
                RunServiceOperation("root", progress => service.SetGameRoot(selectedPath));
            }
        }
    }

    private void BrowseFont(FontRole role)
    {
        if (busy || snapshot == null || !snapshot.CanSelectFont) return;
        using (var picker = new OpenFileDialog {
            Title = role == FontRole.Thai ? "เลือกฟอนต์ไทย TrueType" : "Choose an English TrueType font",
            Filter = "TrueType Font (*.ttf)|*.ttf",
            CheckFileExists = true,
            Multiselect = false
        }) {
            if (picker.ShowDialog(this) == DialogResult.OK) ImportFontPath(picker.FileName, role);
        }
    }

    private void OpenGuide()
    {
        string path = service.GuidePath;
        if (!String.IsNullOrWhiteSpace(path) && File.Exists(path))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        else
            SetStatus("error", "ไม่พบไฟล์วิธีใช้ที่มาพร้อมโปรแกรม");
    }

    private void PostToUi(Action action)
    {
        if (action == null || IsDisposed || Disposing) return;
        if (!IsHandleCreated) return;
        try {
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }
        catch (InvalidOperationException) { }
    }

    private static string UserMessage(Exception error)
    {
        if (error == null) return "ทำรายการไม่สำเร็จ";
        string message = (error.Message ?? String.Empty).Trim();
        return String.IsNullOrEmpty(message) ? "ทำรายการไม่สำเร็จ" : "ทำรายการไม่สำเร็จ\r\n" + message;
    }

    private static string BusyMessage(string kind)
    {
        if (kind == "translate") return "กำลังทำการแปล…";
        if (kind == "verify") return "กำลังตรวจสอบคำแปล…";
        if (kind == "restore") return "กำลังคืนค่าเดิม…";
        if (kind.StartsWith("font", StringComparison.Ordinal)) return "กำลังจัดการฟอนต์…";
        if (kind == "root") return "กำลังบันทึกตำแหน่งเกม…";
        return "กำลังทำงาน…";
    }

    private sealed class FontPreviewControl : Control
    {
        private PrivateFontCollection thaiCollection;
        private PrivateFontCollection englishCollection;
        private Font thaiFont;
        private Font englishFont;
        private string thaiPath;
        private string englishPath;
        private string thaiName = "Thai";
        private string englishName = "English";
        private string thaiError;
        private string englishError;
        private ThemePalette currentPalette = ThemePalette.For(LauncherTheme.Dark);

        public FontPreviewControl()
        {
            DoubleBuffered = true;
            BackColor = currentPalette.Card;
            ForeColor = currentPalette.Text;
            Font = new Font("Leelawadee UI", 13f, FontStyle.Regular);
            TabStop = false;
        }

        public void ApplyTheme(ThemePalette value)
        {
            currentPalette = value ?? ThemePalette.For(LauncherTheme.Dark);
            BackColor = currentPalette.Card;
            ForeColor = currentPalette.Text;
            Invalidate();
        }

        public void SetFontFiles(string thai, string english)
        {
            if (String.Equals(thaiPath, thai, StringComparison.OrdinalIgnoreCase)
                && String.Equals(englishPath, english, StringComparison.OrdinalIgnoreCase)) return;
            thaiPath = thai;
            englishPath = english;
            DisposeFonts();
            thaiError = LoadPrivateFont(thaiPath, out thaiCollection, out thaiFont, out thaiName);
            englishError = LoadPrivateFont(englishPath, out englishCollection, out englishFont, out englishName);
            Tag = (thaiFont == null ? String.Empty : thaiPath) + "|" + (englishFont == null ? String.Empty : englishPath);
            Invalidate();
        }

        private static string LoadPrivateFont(string path, out PrivateFontCollection collection, out Font font, out string name)
        {
            collection = null;
            font = null;
            name = String.Empty;
            try {
                if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "ไม่พบไฟล์ฟอนต์ตัวอย่าง";
                collection = new PrivateFontCollection();
                collection.AddFontFile(path);
                if (collection.Families.Length == 0) return "อ่านแบบอักษรจากไฟล์นี้ไม่ได้";
                name = collection.Families[0].Name;
                font = new Font(collection.Families[0], 15f, FontStyle.Regular, GraphicsUnit.Point);
                return null;
            }
            catch {
                if (collection != null) { collection.Dispose(); collection = null; }
                return "แสดงตัวอย่างฟอนต์นี้ไม่ได้";
            }
        }

        private void DisposeFonts()
        {
            if (thaiFont != null) { thaiFont.Dispose(); thaiFont = null; }
            if (englishFont != null) { englishFont.Dispose(); englishFont = null; }
            if (thaiCollection != null) { thaiCollection.Dispose(); thaiCollection = null; }
            if (englishCollection != null) { englishCollection.Dispose(); englishCollection = null; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = DeviceDpi / 96f;
            float horizontalPadding = 14f * scale;
            float lineHeightThai = (thaiFont ?? Font).GetHeight(e.Graphics) + 6f * scale;
            float lineHeightEnglish = (englishFont ?? Font).GetHeight(e.Graphics) + 6f * scale;
            float firstY = Math.Max(6f * scale, (Height - lineHeightThai - lineHeightEnglish) / 2f - 3f * scale);
            float secondY = firstY + lineHeightThai + 8f * scale;
            DrawMixed(e.Graphics, firstY, thaiFont, englishFont, thaiError, englishError,
                "ภาษาไทย • ", "NTE Thai", scale);
            DrawMixed(e.Graphics, secondY, englishFont, thaiFont, englishError, thaiError,
                "Neverness to Everness  ", "สวัสดี", scale);
            using (var captionFont = new Font("Leelawadee UI", 8f, FontStyle.Regular, GraphicsUnit.Point))
            using (var brush = new SolidBrush(currentPalette.Muted)) {
                string caption = "TH: " + (String.IsNullOrWhiteSpace(thaiName) ? "Default" : thaiName)
                    + "     EN: " + (String.IsNullOrWhiteSpace(englishName) ? "Default" : englishName);
                e.Graphics.DrawString(caption, captionFont, brush, new PointF(horizontalPadding, Math.Max(0, Height - captionFont.GetHeight(e.Graphics) - 2f * scale)));
            }
        }

        private void DrawMixed(Graphics graphics, float y, Font firstFont, Font secondFont,
            string firstError, string secondError, string firstText, string secondText, float scale)
        {
            float x = 3f * scale;
            if (firstFont == null) {
                using (var brush = new SolidBrush(currentPalette.StatusErrorText))
                    graphics.DrawString(firstError ?? "ฟอนต์เริ่มต้น", Font, brush, new PointF(x, y));
                x += graphics.MeasureString(firstError ?? "ฟอนต์เริ่มต้น", Font).Width + 4f * scale;
            }
            else {
                using (var brush = new SolidBrush(currentPalette.Text)) graphics.DrawString(firstText, firstFont, brush, new PointF(x, y));
                x += graphics.MeasureString(firstText, firstFont).Width;
            }
            if (secondFont == null) {
                using (var brush = new SolidBrush(currentPalette.StatusErrorText))
                    graphics.DrawString(secondError ?? "Default font", Font, brush, new PointF(x, y));
            }
            else using (var brush = new SolidBrush(currentPalette.Text))
                graphics.DrawString(secondText, secondFont, brush, new PointF(x, y));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) {
                DisposeFonts();
            }
            base.Dispose(disposing);
        }
    }
}






