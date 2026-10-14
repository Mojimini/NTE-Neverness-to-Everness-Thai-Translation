using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class WpfUiTests
{
    private static int assertions;

    [STAThread]
    private static int Main()
    {
        try {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            FontSelectionThemeAndScrollAreRetained(application);
            FontSelectorBodyAndKeyboardWork(application);
            DarkFontControlsAndScrollbarRender(application);
            InvalidFontPreviewsWarnForBothRolesAndFallback(application);
            ValidManagedFontPreviewsDoNotShowWarnings(application);
            TranslateScrollRestoreAndBusyState(application);
            OperationErrorSurvivesPollingAndStatusWraps(application);
            StartupUpdateAndOfflineCheckKeepExistingNotice(application);
            UpdateDownloadFailureKeepsWindowOpen(application);
            UpdateDownloadCancellationKeepsWindowOpen(application);
            UpdateHandoffFailureKeepsWindowOpen(application);
            application.Shutdown();
            Console.WriteLine("WPF_UI_PASS assertions=" + assertions);
            return 0;
        }
        catch (Exception ex) {
            Console.Error.WriteLine("WPF_UI_FAIL " + ex);
            return 1;
        }
    }

    private static void FontSelectionThemeAndScrollAreRetained(Application application)
    {
        WithWindow(application, new WpfLauncherServiceFake(), null, (window, service, root) => {
            PumpUntil(() => service.InspectCount > 0 && window.thaiLibrary.Items.Count >= 3, 5000, "initial font lists");
                        SelectAndWait(window,service,window.thaiLibrary,FontRole.Thai,"thai-b","exact Thai font ID selection");
            Assert(service.SelectedThaiId == "thai-b", "same-name Thai font selection uses the exact ID");
            SelectAndWait(window,service,window.englishLibrary,FontRole.English,"english-custom","exact English font ID selection");
            Assert(service.SelectedEnglishId == "english-custom", "English font selection uses the exact ID");

            SelectAndWait(window,service,window.thaiLibrary,FontRole.Thai,"default","restore default Thai selection");
            Assert(!window.deleteThaiButton.IsEnabled, "default Thai font cannot be deleted");
            SelectAndWait(window,service,window.englishLibrary,FontRole.English,"default","restore default English selection");
            Assert(!window.deleteEnglishButton.IsEnabled, "default English font cannot be deleted");
window.ShowPage(1);
            PumpUntil(() => window.scrollPages[1].ScrollableHeight > 0, 5000, "font page scroll range");
            window.scrollPages[1].ScrollToVerticalOffset(window.scrollPages[1].ScrollableHeight);
            PumpFor(120);
            double before = window.scrollPages[1].VerticalOffset;
            Assert(before > 0, "font library page has a usable scroll position");
            window.ShowPage(2);
            PumpFor(160);
            window.ShowPage(1);
            PumpFor(180);
            Assert(Math.Abs(window.scrollPages[1].VerticalOffset - before) < 3,
                "switching pages preserves the font page scroll offset");

            Color oldBackground = ((SolidColorBrush)window.Background).Color;
            Click(window.themeButton);
            PumpUntil(() => ((SolidColorBrush)window.Background).Color != oldBackground, 3000, "theme switch");
            double afterTheme = window.scrollPages[1].VerticalOffset;
            Assert(Math.Abs(afterTheme - before) < 3, "theme change preserves the current page and scroll position");
        });
    }

                private static void SelectAndWait(WpfLauncherWindow window,WpfLauncherServiceFake service,ComboBox combo,FontRole role,string id,string label)
    {
        int inspectBefore=service.InspectCount;
        SelectFont(combo,id);
        PumpUntil(()=>service.InspectCount>inspectBefore&&window.thaiLibrary.IsEnabled&&window.englishLibrary.IsEnabled
            &&(role==FontRole.Thai?service.SelectedThaiId:service.SelectedEnglishId)==id
            &&window.statusText.Text.StartsWith("เลือกฟอนต์แล้ว",StringComparison.Ordinal),5000,label+" completes in UI");
    }
private static void FontSelectorBodyAndKeyboardWork(Application application)
    {
        var service=new WpfLauncherServiceFake();
        WithWindow(application,service,null,(window,ignored,root)=>{
            PumpUntil(()=>service.InspectCount>0&&window.thaiLibrary.IsEnabled,5000,"font selector initial state");
            window.ShowPage(1);
            PumpUntil(()=>window.thaiLibrary.IsVisible&&window.thaiLibrary.ActualWidth>0,3000,"font selector visible for input");
            ToggleButton bodyToggle=window.thaiLibrary.Template.FindName("DropDownToggle",window.thaiLibrary) as ToggleButton;
            if(bodyToggle==null)throw new InvalidOperationException("full-body font selector toggle is missing");
            Rect buttonBounds=bodyToggle.TransformToAncestor(window.thaiLibrary).TransformBounds(new Rect(bodyToggle.RenderSize));
            Assert(buttonBounds.Left<=1&&buttonBounds.Top<=1&&buttonBounds.Right>=window.thaiLibrary.ActualWidth-1&&buttonBounds.Bottom>=window.thaiLibrary.ActualHeight-1,
                "font selector toggle covers the full body, including its non-arrow area");
            bodyToggle.IsChecked=true;
            PumpUntil(()=>window.thaiLibrary.IsDropDownOpen,3000,"full-body toggle opens font list");
            Assert(window.thaiLibrary.IsDropDownOpen,"full-body toggle opens the dropdown through its IsChecked binding");
            RaiseKey(window.thaiLibrary,Key.Down);
            RaiseKey(window.thaiLibrary,Key.Enter);
            PumpUntil(()=>service.SelectedThaiId=="thai-a",5000,"keyboard selects next Thai font");
            PumpUntil(()=>window.statusText.Text.StartsWith("เลือกฟอนต์แล้ว",StringComparison.Ordinal),3000,"keyboard font selection completes");
            Assert(service.SelectedThaiId=="thai-a"&&!window.thaiLibrary.IsDropDownOpen,
                "routed Down and Enter commit the selected font ID");
        });
    }

    private static void RaiseKey(UIElement target,Key key)
    {
        PresentationSource source=PresentationSource.FromVisual(target);
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,key){RoutedEvent=Keyboard.KeyDownEvent});
        PumpFor(40);
    }
private static void DarkFontControlsAndScrollbarRender(Application application)
    {
        WithWindow(application, new WpfLauncherServiceFake(), null, (window, service, root) => {
            EnsureDarkTheme(window);
            window.ShowPage(1);
            PumpUntil(() => window.scrollPages[1].ScrollableHeight > 0 && window.thaiLibrary.IsVisible && window.englishLibrary.IsVisible,
                5000, "dark font controls and page scrollbar");
            window.scrollPages[1].ScrollToVerticalOffset(0);
            window.scrollPages[1].UpdateLayout();
            PumpFor(80);
            byte[] darkPixels; int width, height;
            var darkFrame = CaptureWindowFrame(window, out darkPixels, out width, out height);
            double thaiLight = LightRatioInTopBand(darkPixels,width,height,window,window.thaiLibrary);
            double englishLight = LightRatioInTopBand(darkPixels,width,height,window,window.englishLibrary);
            Assert(thaiLight < 0.05 && englishLight < 0.05,
                "dark font selector bodies render dark pixels in the actual WPF frame (TH="+thaiLight.ToString("0.000")+", EN="+englishLight.ToString("0.000")+")");
            ScrollBar darkRail = FindVerticalScrollBar(window.scrollPages[1]);
            if (darkRail == null) throw new InvalidOperationException("visible font-page vertical scrollbar was not found");
            double darkRailLight=LightRatioInRect(darkPixels,width,height,darkRail.TransformToAncestor(window).TransformBounds(new Rect(darkRail.RenderSize)),2);
            Assert(darkRailLight < 0.05, "dark font-page scrollbar rail renders dark pixels in the actual WPF frame (light="+darkRailLight.ToString("0.000")+")");
            double darkPopupLight=RenderComboPopupLightRatio(window.thaiLibrary);
            Assert(darkPopupLight < 0.08, "dark font dropdown popup renders its themed dark surface (light="+darkPopupLight.ToString("0.000")+")");

            window.thaiLibrary.Focus(); PumpFor(40);
            Border focusChrome=window.thaiLibrary.Template.FindName("comboChrome",window.thaiLibrary) as Border;
            Assert(focusChrome!=null&&focusChrome.BorderThickness.Left>=2,
                "keyboard focus gives the font selector an emphasized visible border");
            var focusBrush=focusChrome.BorderBrush as SolidColorBrush;
            System.Drawing.Color expectedSystemAccent=ThemePalette.For(LauncherTheme.Dark).Accent;
            Color expectedAccent=Color.FromRgb(expectedSystemAccent.R,expectedSystemAccent.G,expectedSystemAccent.B);
            Assert(focusBrush!=null&&focusBrush.Color.Equals(expectedAccent),
                "keyboard focus border uses the theme accent color");
            byte[] focusedPixels; int focusedWidth,focusedHeight;
            CaptureWindowFrame(window,out focusedPixels,out focusedWidth,out focusedHeight);
            Assert(BorderAccentAppears(focusedPixels,focusedWidth,focusedHeight,window,window.thaiLibrary,expectedAccent),
                "keyboard focus accent is present in the rendered selector border");

            Click(window.themeButton);
            PumpUntil(() => ((SolidColorBrush)window.Background).Color.R+((SolidColorBrush)window.Background).Color.G+((SolidColorBrush)window.Background).Color.B>300,
                3000,"light theme switch");
            PumpFor(60);
            byte[] lightPixels;
            var lightFrame=CaptureWindowFrame(window,out lightPixels,out width,out height);
            double thaiLightTheme=LightRatioInTopBand(lightPixels,width,height,window,window.thaiLibrary);
            double englishLightTheme=LightRatioInTopBand(lightPixels,width,height,window,window.englishLibrary);
            Assert(thaiLightTheme>0.8&&englishLightTheme>0.8,
                "light theme renders both font selector bodies with light pixels (TH="+thaiLightTheme.ToString("0.000")+", EN="+englishLightTheme.ToString("0.000")+")");
            ScrollBar lightRail=FindVerticalScrollBar(window.scrollPages[1]);
            Rect lightRailRect=lightRail.TransformToAncestor(window).TransformBounds(new Rect(lightRail.RenderSize));
            double lightRailLight=LightRatioInRect(lightPixels,width,height,lightRailRect,2);
            double lightRailDark=DarkRatioInRect(lightPixels,width,height,lightRailRect,2);
            Assert(lightRailLight>0.35&&lightRailDark<0.05,"light font-page scrollbar rail keeps a light themed track (light="+lightRailLight.ToString("0.000")+", dark="+lightRailDark.ToString("0.000")+")");
            double lightPopupLight=RenderComboPopupLightRatio(window.thaiLibrary);
            Assert(lightPopupLight>0.8,"light font dropdown popup renders its themed light surface (light="+lightPopupLight.ToString("0.000")+")");
        });
    }

    private static RenderTargetBitmap CaptureWindowFrame(Window window,out byte[] pixels,out int width,out int height)
    {
        width=Math.Max(1,(int)Math.Ceiling(window.ActualWidth)); height=Math.Max(1,(int)Math.Ceiling(window.ActualHeight));
        var frame=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); frame.Render(window);
        pixels=new byte[width*height*4]; frame.CopyPixels(pixels,width*4,0); return frame;
    }

    private static bool BorderAccentAppears(byte[] pixels,int width,int height,Window window,FrameworkElement element,Color expected)
    {
        Rect r=element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        int center=(int)Math.Round(r.Left+r.Width/2); int top=(int)Math.Floor(r.Top);
        for(int y=Math.Max(0,top);y<Math.Min(height,top+3);y++)for(int x=Math.Max(0,center-5);x<Math.Min(width,center+5);x++){
            int p=(y*width+x)*4;
            if(Math.Abs(pixels[p+2]-expected.R)<=12&&Math.Abs(pixels[p+1]-expected.G)<=12&&Math.Abs(pixels[p]-expected.B)<=12)return true;
        }
        return false;
    }
    private static double DarkRatioInRect(byte[] pixels,int width,int height,Rect area,int inset)
    {
        int left=Math.Max(0,(int)Math.Ceiling(area.Left+inset)),right=Math.Min(width,(int)Math.Floor(area.Right-inset));
        int top=Math.Max(0,(int)Math.Ceiling(area.Top+inset)),bottom=Math.Min(height,(int)Math.Floor(area.Bottom-inset));
        int dark=0,total=0;
        for(int y=top;y<bottom;y++)for(int x=left;x<right;x++){
            int p=(y*width+x)*4;byte b=pixels[p],g=pixels[p+1],r=pixels[p+2];total++;
            if(r<80&&g<80&&b<80)dark++;
        }
        return (double)dark/Math.Max(1,total);
    }
    private static double RenderComboPopupLightRatio(ComboBox combo)
    {
        combo.ApplyTemplate();
        Popup popup=combo.Template.FindName("PART_Popup",combo) as Popup;
        if(popup==null)throw new InvalidOperationException("themed ComboBox template has no PART_Popup");
        combo.IsDropDownOpen=true;
        PumpUntil(()=>popup.IsOpen&&popup.Child is FrameworkElement&&((FrameworkElement)popup.Child).ActualWidth>0&&((FrameworkElement)popup.Child).ActualHeight>0,
            3000,"font dropdown popup open");
        var child=(FrameworkElement)popup.Child; child.UpdateLayout();
        int width=Math.Max(1,(int)Math.Ceiling(child.ActualWidth)),height=Math.Max(1,(int)Math.Ceiling(child.ActualHeight));
        var frame=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); frame.Render(child);
        byte[] pixels=new byte[width*height*4]; frame.CopyPixels(pixels,width*4,0);
        combo.IsDropDownOpen=false; PumpFor(30);
        return LightRatioInRect(pixels,width,height,new Rect(0,0,width,height),3);
    }
private static double LightRatioInTopBand(byte[] pixels,int width,int height,Window window,FrameworkElement element)
    {
        Rect r=element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        int left=Math.Max(0,(int)Math.Ceiling(r.Left+6)),right=Math.Min(width,(int)Math.Floor(r.Right-18));
        int top=Math.Max(0,(int)Math.Ceiling(r.Top+3)),bottom=Math.Min(height,(int)Math.Floor(r.Top+7));
        return LightRatioInRect(pixels,width,height,new Rect(left,top,right-left,bottom-top),0);
    }

    private static double LightRatioInRect(byte[] pixels,int width,int height,Rect area,int inset)
    {
        int left=Math.Max(0,(int)Math.Ceiling(area.Left+inset)),right=Math.Min(width,(int)Math.Floor(area.Right-inset));
        int top=Math.Max(0,(int)Math.Ceiling(area.Top+inset)),bottom=Math.Min(height,(int)Math.Floor(area.Bottom-inset));
        int light=0,total=0;
        for(int y=top;y<bottom;y++)for(int x=left;x<right;x++){
            int p=(y*width+x)*4;byte b=pixels[p],g=pixels[p+1],r=pixels[p+2];total++;
            if(r>185&&g>185&&b>185)light++;
        }
        return (double)light/Math.Max(1,total);
    }
private static void ValidManagedFontPreviewsDoNotShowWarnings(Application application)
    {
        string fonts = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "runtime", "Fonts"));
        if (!Directory.Exists(fonts)) fonts = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "repo", "runtime", "Fonts"));
        string thaiPath = Path.Combine(fonts, "Default", "current.ttf");
        string englishPath = Path.Combine(fonts, "Bundled", "Monocraft", "preview.ttf");
        Assert(File.Exists(thaiPath) && File.Exists(englishPath), "managed default and Monocraft preview TTFs are available to the local fixture");
        var service = new WpfLauncherServiceFake { ThaiFontPreviewPath = thaiPath, EnglishFontPreviewPath = englishPath };
        WithWindow(application, service, null, (window, ignored, root) => {
            PumpUntil(() => service.InspectCount > 0 && window.previewCaption.Text.StartsWith("TH:", StringComparison.Ordinal),
                5000, "managed font preview paths loaded");
            Assert(window.fontErrorBox.Visibility == Visibility.Collapsed && String.IsNullOrEmpty(window.fontError.Text),
                "valid managed Thai and Monocraft previews show no fallback warning");
            Assert(FontFamilyMatchesPath(window.previewThai1.FontFamily, thaiPath),
                "default Thai preview resolves to its managed font file");
            Assert(FontFamilyMatchesPath(window.previewEnglish1.FontFamily, englishPath),
                "Monocraft English preview resolves to its managed font file");
        });
    }

    private static void InvalidFontPreviewsWarnForBothRolesAndFallback(Application application)
    {
        string invalidPath = Path.Combine(Path.GetTempPath(), "nte-invalid-preview-" + Guid.NewGuid().ToString("N") + ".ttf");
        File.WriteAllText(invalidPath, "not a TrueType font");
        var service = new WpfLauncherServiceFake { ThaiFontPreviewPath = invalidPath, EnglishFontPreviewPath = invalidPath };
        try {
            WithWindow(application, service, null, (window, ignored, root) => {
                PumpUntil(() => service.InspectCount > 0 && window.fontErrorBox.Visibility == Visibility.Visible, 5000, "invalid preview warnings");
                Assert(window.fontError.Text.Contains("ภาษาไทย") && window.fontError.Text.Contains("English"),
                    "preview warning identifies both invalid font roles");
                Assert(window.previewCaption.Text.Contains("TH:") && window.previewCaption.Text.Contains("EN:")
                    && CountOccurrences(window.previewCaption.Text, "(ตัวอย่างใช้ฟอนต์หน้าต่าง)") == 2,
                    "each invalid preview caption explicitly reports the window-font fallback");
            });
        }
        finally { if (File.Exists(invalidPath)) File.Delete(invalidPath); }
    }

    private static void TranslateScrollRestoreAndBusyState(Application application)
    {
        var service = new WpfLauncherServiceFake { BlockTranslation = true, EmitRapidProgress = true };
        WithWindow(application, service, null, (window, ignored, root) => {
            PumpUntil(() => service.InspectCount > 0, 5000, "initial snapshot");
            window.ShowPage(0);
            PumpUntil(() => window.scrollPages[0].ScrollableHeight > 0, 5000, "translation page scroll range");
            EnsureDarkTheme(window);
            AssertNoNativeChildWindowsOrWpfHosts(window);
            window.scrollPages[0].ScrollToVerticalOffset(100);
            Click(window.translateButton);
            PumpUntil(() => service.RapidReportsCompleted.IsSet, 5000, "6000 translation progress reports");
            PumpFor(150);
            window.scrollPages[0].ScrollToVerticalOffset(165);
            PumpFor(180);
            Assert(Math.Abs(window.scrollPages[0].VerticalOffset - 165) < 4,
                "scrolling remains responsive while high-volume progress is coalesced");
            Assert(!window.translateButton.IsEnabled && !window.importThaiButton.IsEnabled && !window.importEnglishButton.IsEnabled
                && !window.deleteThaiButton.IsEnabled && !window.restoreButton.IsEnabled,
                "font and restore mutations are disabled while Translate is busy");
            Assert(service.ProgressCallbacks == 6000, "fake Translate emitted the complete 6000-report burst");
            Assert(window.phaseText.Text == "download chunk 5999", "latest progress phase is shown during the busy operation");
            Assert(window.operationProgress.Value < 0.995f, "indeterminate progress never appears verified before operation completion");
            CaptureBusyScrollFrames(window);
            window.Close();
            Assert(window.IsVisible && service.TranslationCalls == 1,
                "closing is blocked while Translate is in progress");
            service.ReleaseTranslation.Set();
            PumpUntil(() => service.Active && window.statusText.Text.Contains("เสร็จแล้ว"), 7000, "translation terminal success");
            PumpUntil(() => window.operationProgress.Value >= 0.995f, 3000, "verified 100 percent progress");
            Assert(window.operationProgress.Value >= 0.995f, "successful Translate reaches verified 100 percent");

            Click(window.restoreButton);
            PumpUntil(() => !window.restoreButton.IsEnabled && window.statusText.Text.Contains("คืนค่าเดิมแล้ว"), 7000, "restore terminal success");
            PumpUntil(() => window.operationProgress.Value <= 0.005f, 3000, "restored zero progress");
            Assert(window.operationProgress.Value <= 0.005f, "successful Restore returns progress to zero");
        });
    }

    private static void OperationErrorSurvivesPollingAndStatusWraps(Application application)
    {
        var service = new WpfLauncherServiceFake { Message = new string('ก', 120) + " long English status detail for wrapping", FailNextTranslate = true };
        WithWindow(application, service, null, (window, ignored, root) => {
            PumpUntil(() => service.InspectCount > 0, 5000, "long status snapshot");
            window.ShowPage(2);
            PumpUntil(() => window.statusDetail.ActualHeight > 45, 3000, "wrapped status detail");
            Assert(window.statusDetail.TextWrapping == TextWrapping.Wrap && window.statusDetail.ActualHeight > 45,
                "status detail wraps long Thai and English text without clipping");

            window.ShowPage(0);
            Click(window.translateButton);
            PumpUntil(() => service.TranslationCalls == 1 && window.statusText.Text.Contains("ตรวจสอบ"),
                5000, "operation error state");
            string failure = window.statusText.Text;
            PumpUntil(() => service.InspectCount >= 3 && window.statusText.Text == failure, 7000, "periodic snapshot after failure");
            Assert(window.statusText.Text == failure && window.statusText.Text.Contains("ตรวจสอบ"),
                "periodic polling does not overwrite the operation error");
        });
    }

    private static void StartupUpdateAndOfflineCheckKeepExistingNotice(Application application)
    {
        var releases = new WpfReleaseDeliveryFake();
        WithWindow(application, new WpfLauncherServiceFake(), releases, (window, service, root) => {
            PumpUntil(() => releases.CheckCalls > 0 && window.updateNotice.Visibility == Visibility.Visible, 6000, "startup update notice");
            Assert(window.updateNoticeText.Text.Contains("1.2.0"), "startup update check shows the available version");
            string gameStatus = window.statusText.Text;
            releases.FailNextCheck = true;
            Click(window.checkUpdatesButton);
            PumpUntil(() => releases.CheckCalls >= 2 && window.updateState.Text.Contains("ไม่สำเร็จ"), 5000, "offline update check");
            Assert(window.updateNotice.Visibility == Visibility.Visible,
                "offline update check keeps the previously discovered update visible");
            Assert(window.statusText.Text == gameStatus,
                "offline update check does not replace the game translation status");
        });
    }

    private static void UpdateDownloadCancellationKeepsWindowOpen(Application application)
    {
        var releases = new WpfReleaseDeliveryFake { BlockDownload = true };
        WithWindow(application, new WpfLauncherServiceFake(), releases, (window, service, root) => {
            PumpUntil(() => window.updateNotice.Visibility == Visibility.Visible, 6000, "available update");
            Click(window.updateButton);
            PumpUntil(() => releases.DownloadStarted.IsSet && window.cancelUpdateButton.IsEnabled, 5000, "update download cancellation control");
            Assert(!window.translateButton.IsEnabled, "update download blocks concurrent game changes");
            Click(window.cancelUpdateButton);
            PumpUntil(() => window.updateState.Text.Contains("ยกเลิก"), 5000, "update download cancellation result");
            Assert(window.IsVisible && window.updateButton.IsEnabled,
                "canceled update leaves launcher open and allows retry");
        });
    }

    private static void UpdateDownloadFailureKeepsWindowOpen(Application application)
    {
        var releases = new WpfReleaseDeliveryFake { FailNextDownload = true };
        WithWindow(application, new WpfLauncherServiceFake(), releases, (window, service, root) => {
            PumpUntil(() => window.updateNotice.Visibility == Visibility.Visible, 6000, "available update");
            Click(window.updateButton);
            PumpUntil(() => window.updateState.Text.Contains("ดาวน์โหลดอัปเดตไม่สำเร็จ"), 5000, "update download failure");
            Assert(window.IsVisible && window.updateButton.IsEnabled,
                "failed update download keeps the window open and permits retry");
        });
    }

    private static void UpdateHandoffFailureKeepsWindowOpen(Application application)
    {
        var releases = new WpfReleaseDeliveryFake();
        var handoffEntered = new ManualResetEventSlim(false);
        var releaseHandoff = new ManualResetEventSlim(false);
        try {
            WithWindow(application, new WpfLauncherServiceFake(), releases,
                (window, service, root) => {
                    PumpUntil(() => window.updateNotice.Visibility == Visibility.Visible, 6000, "available update");
                    string gameStatus = window.statusText.Text;
                    Click(window.updateButton);
                    PumpUntil(() => handoffEntered.IsSet, 7000, "installer handoff callback entered");
                    window.Close();
                    Assert(window.IsVisible && window.statusText.Text == gameStatus,
                        "close during installer handoff keeps the window open without changing game status");
                    Assert(!window.updateButton.IsEnabled,
                        "installer handoff remains busy and blocks another update action");
                    releaseHandoff.Set();
                    PumpUntil(() => window.updateState.Text.Contains("เริ่มติดตั้งอัปเดตไม่ได้"), 7000, "update helper handoff failure");
                    Assert(window.IsVisible && window.updateButton.IsEnabled,
                        "thrown handoff error leaves the window open with retry available");
                    Assert(window.statusText.Text == gameStatus,
                        "handoff failure is shown in update state without replacing game status");
                }, (path, release) => {
                    handoffEntered.Set();
                    releaseHandoff.Wait();
                    throw new IOException("fixture handoff rejection");
                });
        }
        finally { releaseHandoff.Set(); }
    }

    private static void SelectFont(ComboBox combo, string id)
    {
        foreach (object item in combo.Items) {
            var entry = item as ComboBoxItem;
            var choice = entry == null ? null : entry.Tag as FontChoice;
            if (choice != null && choice.Id == id) { combo.SelectedItem = entry; return; }
        }
        throw new InvalidOperationException("font ID not found in selector: " + id);
    }

        private static bool FontFamilyMatchesPath(FontFamily family, string path)
    {
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        GlyphTypeface glyph;
        if (!typeface.TryGetGlyphTypeface(out glyph)) return false;
        string resolved = Path.GetFullPath(glyph.FontUri.LocalPath), expected = Path.GetFullPath(path);
        Console.WriteLine("FONT_PREVIEW expected=" + expected + " resolved=" + resolved + " family=" + family.Source);
        return String.Equals(resolved, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0, start = 0;
        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0) { count++; start += value.Length; }
        return count;
    }

    private static void Click(Button button)
    {
        if (button == null) throw new InvalidOperationException("expected button is missing");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void EnsureDarkTheme(WpfLauncherWindow window)
    {
        var brush = window.Background as SolidColorBrush;
        if (brush != null && brush.Color.R + brush.Color.G + brush.Color.B > 300) {
            Click(window.themeButton);
            PumpUntil(() => {
                var current = window.Background as SolidColorBrush;
                return current != null && current.Color.R + current.Color.G + current.Color.B < 180;
            }, 3000, "high-contrast dark rendering theme");
        }
    }

    private static void CaptureBusyScrollFrames(WpfLauncherWindow window)
    {
        ScrollViewer viewport = window.scrollPages[0];
        int width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        for (int i = 0; i < 10; i++) {
            viewport.ScrollToVerticalOffset(i % 2 == 0 ? 35 : Math.Min(145, viewport.ScrollableHeight));
            viewport.UpdateLayout();
            PumpFor(24);
            var frame = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            frame.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));
            string capturePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wpf_busy_scroll_" + i + ".png");
            using (var capture = new FileStream(capturePath, FileMode.Create, FileAccess.Write, FileShare.Read))
                encoder.Save(capture);
            byte[] pixels = new byte[width * height * 4];
            frame.CopyPixels(pixels, width * 4, 0);
            Rect viewportRect = viewport.TransformToAncestor(window).TransformBounds(new Rect(viewport.RenderSize));
            ScrollBar rail = FindVerticalScrollBar(viewport);
            if (rail == null) throw new InvalidOperationException("vertical scroll rail visual was not found");
            Rect railRect = rail.TransformToAncestor(window).TransformBounds(new Rect(rail.RenderSize));
            int left = Math.Max(0, (int)Math.Floor(viewportRect.Left));
            int top = Math.Max(0, (int)Math.Floor(viewportRect.Top));
            int right = Math.Min(width, (int)Math.Ceiling(viewportRect.Right));
            int bottom = Math.Min(height, (int)Math.Ceiling(viewportRect.Bottom));
            int bright = 0, dark = 0, transparent = 0, sampled = 0;
            for (int y = top; y < bottom; y++) for (int x = left; x < right; x++) {
                int p = (y * width + x) * 4;
                byte blue = pixels[p], green = pixels[p + 1], red = pixels[p + 2], alpha = pixels[p + 3];
                sampled++;
                if (alpha < 250) transparent++;
                if (red > 248 && green > 248 && blue > 248) bright++;
                if (red < 75 && green < 85 && blue < 110) dark++;
            }
            double brightRatio = (double)bright / sampled;
            double darkRatio = (double)dark / sampled;
            double transparentRatio = (double)transparent / sampled;
            int railLeft = Math.Max(0, (int)Math.Floor(railRect.Left));
            int railTop = Math.Max(0, (int)Math.Floor(railRect.Top));
            int railRight = Math.Min(width, (int)Math.Ceiling(railRect.Right));
            int railBottom = Math.Min(height, (int)Math.Ceiling(railRect.Bottom));
            int railLight = 0, railPixels = 0;
            for (int y = railTop; y < railBottom; y++) for (int x = railLeft; x < railRight; x++) {
                int p = (y * width + x) * 4;
                if (pixels[p + 2] > 220 && pixels[p + 1] > 220 && pixels[p] > 220) railLight++;
                railPixels++;
            }
            double railLightRatio = (double)railLight / Math.Max(1, railPixels);
            Console.WriteLine("FRAME busy-scroll index=" + i + " white=" + brightRatio.ToString("0.000")
                + " dark=" + darkRatio.ToString("0.000") + " transparent=" + transparentRatio.ToString("0.000")
                + " railLight=" + railLightRatio.ToString("0.000") + " railPixels=" + railPixels + " rail=" + railLeft + "," + railTop + "," + railRight + "," + railBottom
                + " viewport=" + left + "," + top + "," + right + "," + bottom);
            Assert(brightRatio < 0.65 && darkRatio > 0.05 && transparentRatio < 0.01 && railPixels > 0 && railLightRatio == 0,
                "busy scroll frame " + i + " retains dark page content without a blank white surface");
        }
    }

    private static void AssertNoNativeChildWindowsOrWpfHosts(WpfLauncherWindow window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        int childWindows = 0;
        EnumChildWindows(hwnd, (child, state) => { childWindows++; return true; }, IntPtr.Zero);
        Assert(childWindows == 0, "retained WPF window has no native child HWNDs while no modal dialog is open");
        Assert(!ContainsHwndHost(window), "retained WPF visual tree contains no WindowsFormsHost or ElementHost");
    }

    private static bool ContainsHwndHost(DependencyObject node)
    {
        if (node is HwndHost) return true;
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++) if (ContainsHwndHost(VisualTreeHelper.GetChild(node, i))) return true;
        return false;
    }

        private static ScrollBar FindVerticalScrollBar(DependencyObject node)
    {
        var bar = node as ScrollBar;
        if (bar != null && bar.Orientation == Orientation.Vertical && bar.IsVisible && bar.ActualWidth > 0 && bar.ActualHeight > 0) return bar;
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++) {
            ScrollBar found = FindVerticalScrollBar(VisualTreeHelper.GetChild(node, i));
            if (found != null) return found;
        }
        return null;
    }
    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumChildWindows(IntPtr hwndParent, EnumChildProc callback, IntPtr lParam);

    private static void WithWindow(Application application, WpfLauncherServiceFake service,
        WpfReleaseDeliveryFake releases, Action<WpfLauncherWindow, WpfLauncherServiceFake, string> body,
        Action<string, ReleaseInfo> install = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "nte-wpf-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var window = new WpfLauncherWindow(root, service, releases, install ?? ((path, release) => { }));
        window.Width = 900;
        window.Height = 660;
        try {
            window.Show();
            PumpFor(60);
            body(window, service, root);
        }
        finally {
            service.ReleaseTranslation.Set();
            if (window.cancelUpdateButton != null && window.cancelUpdateButton.IsEnabled)
                Click(window.cancelUpdateButton);
            PumpFor(100);
            if (window.IsVisible) window.Close();
            PumpFor(30);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void PumpUntil(Func<bool> condition, int timeoutMs, string label)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (s, e) => {
            if (condition()) frame.Continue = false;
            else if (DateTime.UtcNow >= deadline) frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        if (!condition()) throw new TimeoutException("Timed out waiting for " + label);
    }

    private static void PumpFor(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (s, e) => frame.Continue = false;
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static void Assert(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("ASSERT: " + description);
        assertions++;
        Console.WriteLine("PASS " + description);
    }
}
