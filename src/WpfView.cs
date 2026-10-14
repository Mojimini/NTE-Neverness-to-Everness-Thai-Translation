using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

public sealed partial class WpfLauncherWindow : Window
{
    internal Button translateButton, restoreButton, browseButton, guideButton, themeButton;
    internal Button importThaiButton, importEnglishButton, deleteThaiButton, deleteEnglishButton;
    internal Button checkUpdatesButton, updateButton, cancelUpdateButton;
    internal ComboBox thaiLibrary, englishLibrary;
    internal TextBox gamePath;
    internal TextBlock gameHint, fontHint, statusText, statusDetail, phaseText, fontError;
    internal TextBlock releaseBadge, updateState, updateNoticeText, previewCaption;
    internal Border statusBadge, updateNotice, fontErrorBox;
    internal Run previewThai1, previewThai2, previewEnglish1, previewEnglish2;
    internal RetainedProgressControl operationProgress;
    internal ScrollViewer[] scrollPages;
    internal FrameworkElement[] pageRoots;
    internal int selectedPage;

    private ThemePalette viewPalette = ThemePalette.For(LauncherTheme.Dark);
    private Grid shell;
    private Button[] navigation;
    private readonly List<SmoothPageScroller> pageScrollers = new List<SmoothPageScroller>();
    private string currentStatus = "idle";
    private bool[] pageShown;

    internal void BuildView()
    {
        Title = "NTE ภาษาไทย";
        Width = Math.Min(980, Math.Max(820, SystemParameters.WorkArea.Width - 80));
        Height = Math.Min(800, Math.Max(620, SystemParameters.WorkArea.Height - 80));
        MinWidth = 820;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Leelawadee UI");
        FontSize = 14;
        Background = ToBrush(viewPalette.Window);
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        shell = new Grid { Background = ToBrush(viewPalette.Window) };
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(82) });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.Margin = new Thickness(18, 12, 18, 18);
        Content = shell;

        BuildHeader();
        BuildNavigation();
        BuildPages();
        ApplyViewTheme(viewPalette);
        ShowPage(0);
    }

    internal void ApplyViewTheme(ThemePalette value)
    {
        viewPalette = value ?? ThemePalette.For(LauncherTheme.Dark);
        if (shell == null) return;
        Background = ToBrush(viewPalette.Window);
        shell.Background = ToBrush(viewPalette.Window);
        foreach (Button button in FindLogical<Button>(shell))
        {
            bool primary = Equals(button.Tag, "primary");
            button.Style = MakeButtonStyle(primary);
        }
        foreach (ComboBox combo in FindLogical<ComboBox>(shell)) ApplyComboTheme(combo);
        ApplyThemeToElementsLogical(shell);
        if (operationProgress != null) operationProgress.ApplyTheme(viewPalette);
        SetStatusVisual(currentStatus, viewPalette);
        UpdateNavigationVisuals();
        InvalidateVisual();
    }

    internal void SetStatusVisual(string state, ThemePalette value)
    {
        if (!String.IsNullOrEmpty(state)) currentStatus = state;
        if (value != null) viewPalette = value;
        if (statusBadge == null) return;

        System.Drawing.Color background = viewPalette.StatusIdle;
        System.Drawing.Color foreground = viewPalette.Text;
        if (currentStatus == "busy" || currentStatus == "waiting") {
            background = viewPalette.StatusBusy;
            foreground = viewPalette.StatusBusyText;
        }
        else if (currentStatus == "success") {
            background = viewPalette.StatusSuccess;
            foreground = viewPalette.StatusSuccessText;
        }
        else if (currentStatus == "error") {
            background = viewPalette.StatusError;
            foreground = viewPalette.StatusErrorText;
        }
        else if (currentStatus == "info" || currentStatus == "font") {
            background = viewPalette.StatusInfo;
            foreground = viewPalette.StatusInfoText;
        }
        statusBadge.Background = ToBrush(background);
        if (statusText != null) statusText.Foreground = ToBrush(foreground);
    }

    internal void ShowPage(int index)
    {
        if (pageRoots == null || pageRoots.Length == 0) return;
        selectedPage = Math.Max(0, Math.Min(pageRoots.Length - 1, index));
        for (int i = 0; i < pageRoots.Length; i++) {
            pageRoots[i].Visibility = i == selectedPage ? Visibility.Visible : Visibility.Collapsed;
            if (i == selectedPage && pageShown != null && !pageShown[i]) {
                scrollPages[i].ScrollToVerticalOffset(0);
                pageShown[i] = true;
            }
        }
        UpdateNavigationVisuals();
    }

    private void BuildHeader()
    {
        Border header = CardSurface();
        header.Padding = new Thickness(20, 12, 18, 12);
        header.Margin = new Thickness(6, 0, 6, 10);
        Grid content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock brand = new TextBlock {
            Name = "BrandTitle", Text = "NTE ภาษาไทย • v" + ReleaseVersion.Current, FontSize = 22, FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center, Foreground = ToBrush(viewPalette.Text)
        };
        StackPanel headerRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        releaseBadge = new TextBlock {
            Name = "ReleaseBadge", Text = "NTE 1.4.5", FontSize = 13,
            Foreground = ToBrush(viewPalette.Muted), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        AutomationProperties.SetName(releaseBadge, "รุ่นเกม NTE 1.4.5");
        themeButton = MakeButton("สว่าง", "ThemeToggle", false);
        themeButton.Width = 100;
        themeButton.Height = 38;
        themeButton.ToolTip = "สลับธีมมืดหรือสว่าง";
        headerRight.Children.Add(releaseBadge);
        headerRight.Children.Add(themeButton);
        content.Children.Add(brand);
        Grid.SetColumn(headerRight, 1);
        content.Children.Add(headerRight);
        header.Child = content;
        Grid.SetRow(header, 0);
        shell.Children.Add(header);
    }

    private void BuildNavigation()
    {
        StackPanel nav = new StackPanel {
            Name = "MainNavigation", Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center
        };
        navigation = new[] {
            MakeButton("แปลเกม", "NavTranslate", false),
            MakeButton("ฟอนต์", "NavFonts", false),
            MakeButton("สถานะ", "NavStatus", false)
        };
        navigation[0].Click += (s, e) => ShowPage(0);
        navigation[1].Click += (s, e) => ShowPage(1);
        navigation[2].Click += (s, e) => ShowPage(2);
        for (int i = 0; i < navigation.Length; i++) {
            navigation[i].Margin = new Thickness(0, 0, 8, 0);
            navigation[i].MinWidth = 96;
            nav.Children.Add(navigation[i]);
        }
        Grid.SetRow(nav, 1);
        shell.Children.Add(nav);
    }

    private void BuildPages()
    {
        scrollPages = new ScrollViewer[3];
        pageRoots = new FrameworkElement[3];
        scrollPages[0] = NewScrollPage("TranslatePage");
        scrollPages[1] = NewScrollPage("FontPage");
        scrollPages[2] = NewScrollPage("StatusPage");
        pageRoots[0] = scrollPages[0];
        pageRoots[1] = scrollPages[1];
        pageRoots[2] = scrollPages[2];
        BuildTranslatePage((StackPanel)scrollPages[0].Content);
        BuildFontPage((StackPanel)scrollPages[1].Content);
        BuildStatusPage((StackPanel)scrollPages[2].Content);
        for (int i = 0; i < scrollPages.Length; i++) {
            Grid.SetRow(scrollPages[i], 2);
            shell.Children.Add(scrollPages[i]);
            var scroller = new SmoothPageScroller(scrollPages[i]);
            pageScrollers.Add(scroller);
        }
        pageShown = new bool[scrollPages.Length];
    }

    private ScrollViewer NewScrollPage(string name)
    {
        var content = new StackPanel { Name = name + "Content", Background = ToBrush(viewPalette.Page) };
        var scroll = new ScrollViewer {
            Name = name + "Scroll", Content = content, Background = ToBrush(viewPalette.Page),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false, Padding = new Thickness(4, 2, 12, 24),
            Focusable = true, PanningMode = PanningMode.VerticalOnly
        };
        scroll.Resources[SystemColors.WindowBrushKey] = ToBrush(viewPalette.Card);
        scroll.Resources[SystemColors.WindowTextBrushKey] = ToBrush(viewPalette.Text);
        scroll.Resources[SystemColors.HighlightBrushKey] = ToBrush(viewPalette.StatusInfo);
        scroll.Resources[SystemColors.HighlightTextBrushKey] = ToBrush(viewPalette.Text);
        scroll.Resources["NteScrollTrackBrush"] = ToBrush(viewPalette.Track);
        scroll.Resources["NteScrollThumbBrush"] = ToBrush(viewPalette.Muted);
        scroll.Resources["NteScrollThumbHoverBrush"] = ToBrush(viewPalette.Accent);
        Style scrollbarStyle = MakeScrollBarStyle();
        scroll.Resources[typeof(ScrollBar)] = scrollbarStyle;
        scroll.Resources["NteScrollBarTheme"] = scrollbarStyle;
        scroll.Template = MakeScrollViewerTemplate();
        return scroll;
    }

    private void BuildTranslatePage(Panel page)
    {
        statusBadge = new Border {
            Name = "StatusBadge", CornerRadius = new CornerRadius(16), Background = ToBrush(viewPalette.StatusIdle),
            Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(6, 2, 6, 14),
            ToolTip = "สถานะคำแปล"
        };
        statusText = new TextBlock {
            Name = "ReadOnlyStatus", Text = "กำลังตรวจสอบความพร้อม…", FontSize = 16, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Foreground = ToBrush(viewPalette.Text), VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetLiveSetting(statusText, AutomationLiveSetting.Polite);
        statusBadge.Child = statusText;
        page.Children.Add(statusBadge);

        Border gameCard = CardSurface();
        StackPanel gameContent = CardContent(gameCard, "เกมและตำแหน่งติดตั้ง");
        gameHint = new TextBlock {
            Name = "MainGameHint", Text = "ตรวจสอบสถานะเกมก่อนเริ่ม", TextWrapping = TextWrapping.Wrap,
            Foreground = ToBrush(viewPalette.StatusInfoText), Margin = new Thickness(0, 0, 0, 10)
        };
        Border hintBadge = new Border { Name = "MainGameBadge", CornerRadius = new CornerRadius(12), Background = ToBrush(viewPalette.StatusInfo),
            Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 12), Child = gameHint };
        gameContent.Children.Add(hintBadge);
        DockPanel pathRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 14) };
        browseButton = MakeButton("เลือกโฟลเดอร์เกม", "BrowseGameRoot", false);
        browseButton.MinWidth = 156;
        DockPanel.SetDock(browseButton, Dock.Right);
        gamePath = new TextBox {
            Name = "GameRoot", IsReadOnly = true, Height = 40, Padding = new Thickness(12, 8, 12, 8),
            VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
            ToolTip = "ตำแหน่งเกม"
        };
        AutomationProperties.SetName(gamePath, "ตำแหน่งเกม");
        pathRow.Children.Add(browseButton);
        pathRow.Children.Add(gamePath);
        gameContent.Children.Add(pathRow);

        StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        translateButton = MakeButton("แปลเกม", "EnableTranslation", true);
        translateButton.FontSize = 18;
        translateButton.FontWeight = FontWeights.Bold;
        translateButton.MinWidth = 220;
        translateButton.Height = 54;
        restoreButton = MakeButton("คืนค่าเดิม", "RestoreOriginal", false);
        restoreButton.MinWidth = 150;
        restoreButton.Height = 48;
        actions.Children.Add(translateButton);
        actions.Children.Add(restoreButton);
        gameContent.Children.Add(actions);
        guideButton = MakeButton("วิธีใช้", "OpenGuide", false);
        guideButton.Width = 110;
        guideButton.HorizontalAlignment = HorizontalAlignment.Left;
        guideButton.Margin = new Thickness(0, 10, 0, 0);
        gameContent.Children.Add(guideButton);
        page.Children.Add(gameCard);

        Border progressCard = CardSurface();
        StackPanel progressContent = CardContent(progressCard, "ความคืบหน้า");
        phaseText = new TextBlock { Name = "ProgressText", Text = "เมื่อเริ่มงาน จะแสดงขั้นตอนที่กำลังทำ",
            Foreground = ToBrush(viewPalette.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        operationProgress = new RetainedProgressControl { Name = "OperationProgress", Margin = new Thickness(0, 2, 0, 0) };
        progressContent.Children.Add(phaseText);
        progressContent.Children.Add(operationProgress);
        page.Children.Add(progressCard);

        Border fontSummary = CardSurface();
        StackPanel fontContent = CardContent(fontSummary, "ฟอนต์ที่เลือก");
        fontHint = new TextBlock { Name = "FontHint", Text = "ฟอนต์ไทยและอังกฤษจะแสดงที่หน้าฟอนต์",
            Foreground = ToBrush(viewPalette.Text), TextWrapping = TextWrapping.Wrap };
        fontContent.Children.Add(fontHint);
        page.Children.Add(fontSummary);
    }

    private void BuildFontPage(Panel page)
    {
        Border intro = CardSurface();
        StackPanel introContent = CardContent(intro, "เลือกฟอนต์สำหรับเกม");
        TextBlock explanation = BodyText("เพิ่มไฟล์ .ttf หรือเลือกฟอนต์จากคลัง ฟอนต์จะถูกนำไปใช้เมื่อกดแปลเกม", false);
        introContent.Children.Add(explanation);
        fontError = new TextBlock { Name = "FontLibraryError", Text = String.Empty, TextWrapping = TextWrapping.Wrap,
            Foreground = ToBrush(viewPalette.StatusErrorText), Margin = new Thickness(0, 10, 0, 0) };
        fontErrorBox = new Border { Name = "FontLibraryErrorBox", CornerRadius = new CornerRadius(12),
            Background = ToBrush(viewPalette.StatusError), Padding = new Thickness(12), Margin = new Thickness(0, 10, 0, 0),
            Child = fontError, Visibility = Visibility.Collapsed };
        introContent.Children.Add(fontErrorBox);
        page.Children.Add(intro);

        Border thaiCard = CardSurface();
        StackPanel thai = CardContent(thaiCard, "ฟอนต์ภาษาไทย");
        thaiLibrary = NewFontCombo("ThaiFontLibrary");
        thai.Children.Add(thaiLibrary);
        StackPanel thaiActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        importThaiButton = MakeButton("เพิ่มฟอนต์ไทย", "FontImport", true);
        deleteThaiButton = MakeButton("ลบออกจากคลัง", "FontDelete", false);
        thaiActions.Children.Add(importThaiButton);
        thaiActions.Children.Add(deleteThaiButton);
        thai.Children.Add(thaiActions);
        page.Children.Add(thaiCard);

        Border englishCard = CardSurface();
        StackPanel english = CardContent(englishCard, "ฟอนต์ภาษาอังกฤษ");
        englishLibrary = NewFontCombo("EnglishFontLibrary");
        english.Children.Add(englishLibrary);
        StackPanel englishActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        importEnglishButton = MakeButton("เพิ่มฟอนต์อังกฤษ", "EnglishFontImport", true);
        deleteEnglishButton = MakeButton("ลบออกจากคลัง", "EnglishFontDelete", false);
        englishActions.Children.Add(importEnglishButton);
        englishActions.Children.Add(deleteEnglishButton);
        english.Children.Add(englishActions);
        page.Children.Add(englishCard);

        Border previewCard = CardSurface();
        StackPanel previewContent = CardContent(previewCard, "ตัวอย่างฟอนต์ไทยและอังกฤษ");
        previewCaption = new TextBlock { Name = "FontPreviewCaption", Text = "ตัวอย่างใช้ฟอนต์ส่วนตัว ไม่ติดตั้งฟอนต์ลง Windows",
            Foreground = ToBrush(viewPalette.Muted), FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8) };
        previewContent.Children.Add(previewCaption);
        TextBlock mixedOne = new TextBlock { Name = "MixedPreviewThaiEnglish", FontSize = 17, TextWrapping = TextWrapping.Wrap,
            Foreground = ToBrush(viewPalette.Text), Margin = new Thickness(0, 0, 0, 5) };
        previewThai1 = new Run("สวัสดี • ");
        previewEnglish1 = new Run("NTE Thai");
        mixedOne.Inlines.Add(previewThai1);
        mixedOne.Inlines.Add(previewEnglish1);
        TextBlock mixedTwo = new TextBlock { Name = "MixedPreviewEnglishThai", FontSize = 17, TextWrapping = TextWrapping.Wrap,
            Foreground = ToBrush(viewPalette.Text) };
        previewEnglish2 = new Run("A journey begins");
        previewThai2 = new Run(" • ยินดีต้อนรับ");
        mixedTwo.Inlines.Add(previewEnglish2);
        mixedTwo.Inlines.Add(previewThai2);
        previewContent.Children.Add(mixedOne);
        previewContent.Children.Add(mixedTwo);
        page.Children.Add(previewCard);
    }

    private void BuildStatusPage(Panel page)
    {
        Border detailCard = CardSurface();
        StackPanel detail = CardContent(detailCard, "สถานะและรายละเอียด");
        statusDetail = new TextBlock { Name = "StatusDetail", Text = "กำลังตรวจสอบ…",
            TextWrapping = TextWrapping.Wrap, Foreground = ToBrush(viewPalette.Text),
            MinHeight = 56, Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetLiveSetting(statusDetail, AutomationLiveSetting.Polite);
        detail.Children.Add(statusDetail);
        TextBlock restoreHelp = BodyText("หลังเล่นเกมสามารถปิดแอปได้ คืนค่าเดิมจำเป็นเมื่อจะถอนคำแปล เปลี่ยนฟอนต์ หรืออัปเดตเกม", false);
        detail.Children.Add(restoreHelp);
        page.Children.Add(detailCard);

        Border updateCard = CardSurface();
        StackPanel updates = CardContent(updateCard, "อัปเดตแอป");
        updateState = new TextBlock { Name = "UpdateState", Text = "แอปปัจจุบัน v" + ReleaseVersion.Current + " • ตรวจสอบเวอร์ชันอัปเดตได้ที่นี่",
            Foreground = ToBrush(viewPalette.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        updates.Children.Add(updateState);
        StackPanel updateButtons = new StackPanel { Orientation = Orientation.Horizontal };
        checkUpdatesButton = MakeButton("ตรวจหาอัปเดต", "CheckUpdates", false);
        updateButton = MakeButton("ดาวน์โหลดและติดตั้ง", "InstallUpdate", true);
        cancelUpdateButton = MakeButton("ยกเลิก", "CancelUpdate", false);
        updateButtons.Children.Add(checkUpdatesButton);
        updateButtons.Children.Add(updateButton);
        updateButtons.Children.Add(cancelUpdateButton);
        updates.Children.Add(updateButtons);
        updateNoticeText = new TextBlock { Name = "UpdateNoticeText", TextWrapping = TextWrapping.Wrap,
            Foreground = ToBrush(viewPalette.StatusInfoText) };
        updateNotice = new Border { Name = "UpdateNotice", CornerRadius = new CornerRadius(12),
            Background = ToBrush(viewPalette.StatusInfo), Padding = new Thickness(12), Margin = new Thickness(0, 10, 0, 0),
            Child = updateNoticeText, Visibility = Visibility.Collapsed };
        updates.Children.Add(updateNotice);
        page.Children.Add(updateCard);
    }

    private ComboBox NewFontCombo(string name)
    {
        return new ComboBox { Name = name, Height = 42, MinWidth = 320, Padding = new Thickness(10, 6, 10, 6),
            VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 0),
            IsEditable = false, StaysOpenOnEdit = true,
            ToolTip = name == "ThaiFontLibrary" ? "คลังฟอนต์ภาษาไทย" : "คลังฟอนต์ภาษาอังกฤษ" };
    }

    private Border CardSurface()
    {
        return new Border { CornerRadius = new CornerRadius(18), Background = ToBrush(viewPalette.Card),
            BorderBrush = ToBrush(viewPalette.Border), BorderThickness = new Thickness(1),
            Padding = new Thickness(22, 18, 22, 18), Margin = new Thickness(6, 0, 6, 14), Tag = "card" };
    }

    private StackPanel CardContent(Border card, string heading)
    {
        var content = new StackPanel();
        var title = new TextBlock { Text = heading, FontSize = 22, FontWeight = FontWeights.SemiBold,
            Foreground = ToBrush(viewPalette.Text), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        content.Children.Add(title);
        card.Child = content;
        return content;
    }

    private TextBlock BodyText(string text, bool muted)
    {
        return new TextBlock { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap,
            Tag = muted ? "muted" : "body", Foreground = ToBrush(muted ? viewPalette.Muted : viewPalette.Text),
            Margin = new Thickness(0, 0, 0, 6) };
    }

    private Button MakeButton(string text, string name, bool primary)
    {
        return new Button { Name = name, Content = text, Tag = primary ? "primary" : "secondary",
            Style = MakeButtonStyle(primary), MinHeight = 42, Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 2, 10, 2), FontSize = 14, FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand, Focusable = true, HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center };
    }

    private Style MakeButtonStyle(bool primary)
    {
        System.Drawing.Color background = primary ? viewPalette.Accent : viewPalette.Card;
        System.Drawing.Color foreground = primary ? System.Drawing.Color.White : viewPalette.Text;
        System.Drawing.Color hover = primary ? viewPalette.AccentHover : viewPalette.StatusInfo;
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, ToBrush(background)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, ToBrush(foreground)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, ToBrush(viewPalette.Border)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 8, 16, 8)));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(Control.TemplateProperty, ButtonTemplate()));
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Control.BackgroundProperty, ToBrush(hover)));
        style.Triggers.Add(over);
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Control.BackgroundProperty, ToBrush(viewPalette.AccentHover)));
        style.Triggers.Add(pressed);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.52));
        style.Triggers.Add(disabled);
        return style;
    }

    private ControlTemplate ButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "buttonChrome";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        presenter.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("ContentTemplate") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        presenter.SetBinding(TextElement.ForegroundProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, ToBrush(viewPalette.Accent), "buttonChrome"));
        focused.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "buttonChrome"));
        template.Triggers.Add(focused);
        return template;
    }

    private void ApplyComboTheme(ComboBox combo)
    {
        combo.Background = ToBrush(viewPalette.Card);
        combo.Foreground = ToBrush(viewPalette.Text);
        combo.BorderBrush = ToBrush(viewPalette.Border);
        combo.BorderThickness = new Thickness(1);
        combo.Resources[SystemColors.WindowBrushKey] = ToBrush(viewPalette.Card);
        combo.Resources[SystemColors.WindowTextBrushKey] = ToBrush(viewPalette.Text);
        combo.Resources[SystemColors.HighlightBrushKey] = ToBrush(viewPalette.StatusInfo);
        combo.Resources[SystemColors.HighlightTextBrushKey] = ToBrush(viewPalette.Text);
        combo.Style = MakeComboStyle();
        combo.ItemContainerStyle = new Style(typeof(ComboBoxItem));
        combo.ItemContainerStyle.Setters.Add(new Setter(Control.BackgroundProperty, ToBrush(viewPalette.Card)));
        combo.ItemContainerStyle.Setters.Add(new Setter(Control.ForegroundProperty, ToBrush(viewPalette.Text)));
        combo.ItemContainerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));
        combo.ItemContainerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        combo.ItemContainerStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        combo.ItemContainerStyle.Setters.Add(new Setter(Control.TemplateProperty, MakeComboItemTemplate()));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, ToBrush(viewPalette.StatusInfo)));
        combo.ItemContainerStyle.Triggers.Add(hover);
        var selected = new Trigger { Property = Selector.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, ToBrush(viewPalette.StatusInfo)));
        combo.ItemContainerStyle.Triggers.Add(selected);
    }

    private Style MakeComboStyle()
    {
        var style = new Style(typeof(ComboBox));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(Control.TemplateProperty, MakeComboTemplate()));
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, ToBrush(viewPalette.Accent)));
        focus.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
        style.Triggers.Add(focus);
        return style;
    }

    private ControlTemplate MakeComboTemplate()
    {
        string xaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ComboBox}'>
 <Grid>
  <Border x:Name='comboChrome' CornerRadius='10' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'/>
  <ToggleButton x:Name='DropDownToggle' HorizontalAlignment='Stretch' Focusable='False' Background='Transparent' BorderThickness='0'
   IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
   <ToggleButton.Template><ControlTemplate TargetType='{x:Type ToggleButton}'>
    <Border x:Name='toggleChrome' Background='{TemplateBinding Background}' CornerRadius='9'><ContentPresenter/></Border>
    <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='toggleChrome' Property='Background' Value='" + Hex(viewPalette.StatusInfo) + @"'/></Trigger></ControlTemplate.Triggers>
   </ControlTemplate></ToggleButton.Template>
  </ToggleButton>
  <ContentPresenter x:Name='PART_SelectionBoxItem' IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' HorizontalAlignment='Left' VerticalAlignment='Center' Margin='{TemplateBinding Padding}' TextElement.Foreground='{TemplateBinding Foreground}'/>
  <Path Data='M 0 0 L 5 5 L 10 0 Z' Fill='" + Hex(viewPalette.Muted) + @"' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,13,0' IsHitTestVisible='False'/>
  <Popup x:Name='PART_Popup' Placement='Bottom' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'
   PlacementTarget='{Binding RelativeSource={RelativeSource TemplatedParent}}'
   IsOpen='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}}'>
   <Border CornerRadius='10' Background='" + Hex(viewPalette.Card) + @"' BorderBrush='" + Hex(viewPalette.Border) + @"' BorderThickness='1' Padding='4'
    MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='280'>
    <ScrollViewer x:Name='DropDownScrollViewer' CanContentScroll='True' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'>
     <ScrollViewer.Resources><Style TargetType='{x:Type ScrollBar}'><Setter Property='Width' Value='14'/><Setter Property='MinWidth' Value='14'/><Setter Property='Background' Value='" + Hex(viewPalette.Track) + @"'/><Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='{x:Type ScrollBar}'><Grid Background='" + Hex(viewPalette.Track) + @"'><Track x:Name='PART_Track' IsDirectionReversed='True'>
       <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Focusable='False' Background='Transparent' BorderThickness='0'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
       <Track.Thumb><Thumb MinHeight='24' BorderThickness='0'><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border x:Name='popupThumb' CornerRadius='7' Background='" + Hex(viewPalette.Muted) + @"'/><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='popupThumb' Property='Background' Value='" + Hex(viewPalette.Accent) + @"'/></Trigger><Trigger Property='IsDragging' Value='True'><Setter TargetName='popupThumb' Property='Background' Value='" + Hex(viewPalette.Accent) + @"'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
       <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Focusable='False' Background='Transparent' BorderThickness='0'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
      </Track></Grid></ControlTemplate>
     </Setter.Value></Setter></Style></ScrollViewer.Resources>
     <ItemsPresenter SnapsToDevicePixels='True'/>
    </ScrollViewer>
   </Border>
  </Popup>
 </Grid>
</ControlTemplate>";
        var template = (ControlTemplate)XamlReader.Parse(xaml);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, ToBrush(viewPalette.Accent), "comboChrome"));
        focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "comboChrome"));
        template.Triggers.Add(focus);
        return template;
    }

    private ControlTemplate MakeComboItemTemplate()
    {
        string xaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ComboBoxItem}'>
 <Border x:Name='itemChrome' CornerRadius='6' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'>
  <ContentPresenter HorizontalAlignment='Stretch' VerticalAlignment='Center' Content='{TemplateBinding Content}' RecognizesAccessKey='True'/>
 </Border>
 <ControlTemplate.Triggers>
  <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='itemChrome' Property='Background' Value='" + Hex(viewPalette.StatusInfo) + @"'/></Trigger>
  <Trigger Property='IsSelected' Value='True'><Setter TargetName='itemChrome' Property='Background' Value='" + Hex(viewPalette.StatusInfo) + @"'/></Trigger>
  <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='itemChrome' Property='BorderBrush' Value='" + Hex(viewPalette.Accent) + @"'/><Setter TargetName='itemChrome' Property='BorderThickness' Value='1'/></Trigger>
 </ControlTemplate.Triggers>
</ControlTemplate>";
        return (ControlTemplate)XamlReader.Parse(xaml);
    }

    private Style MakeScrollBarStyle()
    {
        string xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>
 <Setter Property='Width' Value='14'/><Setter Property='MinWidth' Value='14'/>
 <Setter Property='Background' Value='{DynamicResource NteScrollTrackBrush}'/>
 <Setter Property='Template'>
  <Setter.Value><ControlTemplate TargetType='{x:Type ScrollBar}'>
   <Grid Background='{DynamicResource NteScrollTrackBrush}'>
    <Track x:Name='PART_Track' IsDirectionReversed='True'>
     <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Focusable='False' Background='Transparent' BorderThickness='0'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
     <Track.Thumb><Thumb MinHeight='24' BorderThickness='0'><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border x:Name='thumbChrome' CornerRadius='7' Background='{DynamicResource NteScrollThumbBrush}'/><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='thumbChrome' Property='Background' Value='{DynamicResource NteScrollThumbHoverBrush}'/></Trigger><Trigger Property='IsDragging' Value='True'><Setter TargetName='thumbChrome' Property='Background' Value='{DynamicResource NteScrollThumbHoverBrush}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
     <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Focusable='False' Background='Transparent' BorderThickness='0'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
    </Track>
   </Grid>
  </ControlTemplate></Setter.Value>
 </Setter>
</Style>";
        return (Style)XamlReader.Parse(xaml);
    }

    private ControlTemplate MakeScrollViewerTemplate()
    {
        string xaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollViewer}'>
 <Grid Background='{TemplateBinding Background}'>
  <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
  <Grid.RowDefinitions><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
  <ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Grid.Row='0' Grid.Column='0'
   Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}'
   CanContentScroll='{TemplateBinding CanContentScroll}' Margin='{TemplateBinding Padding}'/>
  <ScrollBar x:Name='PART_VerticalScrollBar' Style='{DynamicResource NteScrollBarTheme}' Grid.Row='0' Grid.Column='1' Orientation='Vertical'
   Maximum='{TemplateBinding ScrollableHeight}' ViewportSize='{TemplateBinding ViewportHeight}'
   Value='{Binding VerticalOffset, RelativeSource={RelativeSource TemplatedParent}, Mode=OneWay}'
   Visibility='{TemplateBinding ComputedVerticalScrollBarVisibility}'/>
  <ScrollBar x:Name='PART_HorizontalScrollBar' Style='{DynamicResource NteScrollBarTheme}' Grid.Row='1' Grid.Column='0' Orientation='Horizontal'
   Maximum='{TemplateBinding ScrollableWidth}' ViewportSize='{TemplateBinding ViewportWidth}'
   Value='{Binding HorizontalOffset, RelativeSource={RelativeSource TemplatedParent}, Mode=OneWay}'
   Visibility='{TemplateBinding ComputedHorizontalScrollBarVisibility}'/>
  <Border Grid.Row='1' Grid.Column='1' Background='{DynamicResource NteScrollTrackBrush}'/>
 </Grid>
</ControlTemplate>";
        return (ControlTemplate)XamlReader.Parse(xaml);
    }

    private static string Hex(System.Drawing.Color color)
    {
        return "#" + color.A.ToString("X2") + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
    }

    private void UpdateNavigationVisuals()
    {
        if (navigation == null) return;
        for (int i = 0; i < navigation.Length; i++) {
            bool selected = i == selectedPage;
            navigation[i].Background = ToBrush(selected ? viewPalette.StatusInfo : viewPalette.Page);
            navigation[i].Foreground = ToBrush(selected ? viewPalette.Text : viewPalette.Muted);
            navigation[i].BorderBrush = ToBrush(selected ? viewPalette.Accent : viewPalette.Page);
            navigation[i].FontWeight = selected ? FontWeights.Bold : FontWeights.SemiBold;
            navigation[i].Tag = selected ? "nav-selected" : "nav";
        }
    }

    private static IEnumerable<T> FindLogical<T>(DependencyObject root) where T : DependencyObject
    {
        var found = new List<T>();
        if (root == null) return found;
        T typed = root as T;
        if (typed != null) found.Add(typed);
        foreach (object item in LogicalTreeHelper.GetChildren(root)) {
            DependencyObject child = item as DependencyObject;
            if (child != null) found.AddRange(FindLogical<T>(child));
        }
        return found;
    }

    private void ApplyThemeToElementsLogical(DependencyObject root)
    {
        ApplyThemeNode(root);
        foreach (object item in LogicalTreeHelper.GetChildren(root)) {
            DependencyObject child = item as DependencyObject;
            if (child != null) ApplyThemeToElementsLogical(child);
        }
    }

    private void ApplyThemeNode(DependencyObject root)
    {
        Border border = root as Border;
        if (border != null) {
            if (Equals(border.Tag, "card")) {
                border.Background = ToBrush(viewPalette.Card);
                border.BorderBrush = ToBrush(viewPalette.Border);
            }
            else if (border.Name == "MainGameBadge" || border.Name == "UpdateNotice") border.Background = ToBrush(viewPalette.StatusInfo);
            else if (border.Name == "FontLibraryErrorBox") border.Background = ToBrush(viewPalette.StatusError);
            else if (border.Name == "FontPreview") border.Background = ToBrush(viewPalette.Page);
        }
        TextBox input = root as TextBox;
        if (input != null) {
            input.Background = ToBrush(viewPalette.Card);
            input.Foreground = ToBrush(viewPalette.Text);
            input.BorderBrush = ToBrush(viewPalette.Border);
            input.CaretBrush = ToBrush(viewPalette.Text);
        }
        TextBlock text = root as TextBlock;
        if (text != null) {
            if (text.Name == "MainGameHint" || text.Name == "UpdateNoticeText") text.Foreground = ToBrush(viewPalette.StatusInfoText);
            else if (text.Name == "FontLibraryError") text.Foreground = ToBrush(viewPalette.StatusErrorText);
            else if (text.Name == "ReleaseBadge" || text.Name == "ProgressText" || text.Name == "UpdateState"
                || text.Name == "FontPreviewCaption" || Equals(text.Tag, "muted")) text.Foreground = ToBrush(viewPalette.Muted);
            else if (text.Name != "ReadOnlyStatus") text.Foreground = ToBrush(viewPalette.Text);
        }
        ScrollViewer scroll = root as ScrollViewer;
        if (scroll != null) {
            scroll.Background = ToBrush(viewPalette.Page);
            scroll.Resources[SystemColors.WindowBrushKey] = ToBrush(viewPalette.Card);
            scroll.Resources[SystemColors.WindowTextBrushKey] = ToBrush(viewPalette.Text);
            scroll.Resources[SystemColors.HighlightBrushKey] = ToBrush(viewPalette.StatusInfo);
            scroll.Resources[SystemColors.HighlightTextBrushKey] = ToBrush(viewPalette.Text);
            scroll.Resources[SystemColors.ControlBrushKey] = ToBrush(viewPalette.Track);
            scroll.Resources[SystemColors.ControlTextBrushKey] = ToBrush(viewPalette.Muted);
            scroll.Resources["NteScrollTrackBrush"] = ToBrush(viewPalette.Track);
            scroll.Resources["NteScrollThumbBrush"] = ToBrush(viewPalette.Muted);
            scroll.Resources["NteScrollThumbHoverBrush"] = ToBrush(viewPalette.Accent);
        }
        Panel panel = root as Panel;
        if (panel != null && (panel.Name.EndsWith("Content", StringComparison.Ordinal) || panel.Name == "MainNavigation"))
            panel.Background = ToBrush(viewPalette.Page);
    }

    private static SolidColorBrush ToBrush(System.Drawing.Color color)
    {
        var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private sealed class SmoothPageScroller
    {
        private readonly ScrollViewer scroll;
        private readonly DispatcherTimer timer;
        private double target;

        public SmoothPageScroller(ScrollViewer scroll)
        {
            this.scroll = scroll;
            target = scroll.VerticalOffset;
            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(12) };
            timer.Tick += Tick;
            scroll.PreviewMouseWheel += OnPreviewMouseWheel;
            scroll.ScrollChanged += (s, e) => {
                if (!timer.IsEnabled) target = scroll.VerticalOffset;
                else target = Clamp(target, 0, scroll.ScrollableHeight);
            };
            scroll.Unloaded += (s, e) => timer.Stop();
            scroll.Loaded += (s, e) => { if (Math.Abs(target - scroll.VerticalOffset) > 1) timer.Start(); };
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ComboBox combo = FindAncestorCombo(e.OriginalSource as DependencyObject);
            if (combo != null && combo.IsDropDownOpen) return;
            e.Handled = true;
            target = Clamp(target - (e.Delta / 120.0) * 58.0, 0, scroll.ScrollableHeight);
            if (!timer.IsEnabled) timer.Start();
        }

        private void Tick(object sender, EventArgs e)
        {
            double current = scroll.VerticalOffset;
            double delta = target - current;
            if (Math.Abs(delta) <= 0.8) {
                scroll.ScrollToVerticalOffset(target);
                timer.Stop();
                return;
            }
            scroll.ScrollToVerticalOffset(current + delta * 0.34);
        }

        private static ComboBox FindAncestorCombo(DependencyObject source)
        {
            DependencyObject current = source;
            while (current != null) {
                ComboBox combo = current as ComboBox;
                if (combo != null) return combo;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
