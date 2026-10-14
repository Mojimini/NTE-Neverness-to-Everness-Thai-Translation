using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Reflection;

internal static class RetainedProgressTests
{
    [STAThread]
    private static int Main()
    {
        try {
            VerifyTranslationNeverShowsUnverified100();
            VerifyThemePixelsAndVerifiedEndpoints();
            VerifyHighDpiUsesDipGeometry();
            VerifyRestoreReversesProgress();
            VerifyAnimatedTerminalStatesAutomationAndVisibility();
            Console.WriteLine("PASS: retained progress state caps, endpoints, restore, and dark/light pixels.");
            return 0;
        }
        catch (Exception ex) {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static void VerifyTranslationNeverShowsUnverified100()
    {
        var control = CreateControl(LauncherTheme.Dark);
        control.BeginTranslation();
        Equal(0f, control.Value, "translation begins at zero");
        if (control.TargetValue <= 0f || control.TargetValue > 0.06f)
            throw new InvalidOperationException("initial translation motion should remain within the small startup range");
        control.ShowTranslationProgress(0f, false);
        if (!control.IsIndeterminate || control.TargetValue < 0.06f || control.TargetValue > 0.08f)
            throw new InvalidOperationException("unknown-stage work should shimmer with only a small initial movement");
        control.ShowTranslationProgress(1f, true);
        if (control.Value > 0.94f) throw new InvalidOperationException("unverified translation exceeded the 94% cap");
        Equal(0.94f, control.TargetValue, "stage completion caps at 94 percent before verification");
        if (control.IsVerifiedSuccess) throw new InvalidOperationException("progress report alone marked translation verified");
        control.MarkTranslationVerified();
        Equal(1f, control.TargetValue, "verification releases the 100% target");
        if (control.Value >= 1f) throw new InvalidOperationException("verified translation must animate toward 100 rather than snap");
        if (!control.IsVerifiedSuccess) throw new InvalidOperationException("verified endpoint lacks success state");
    }

    private static void VerifyThemePixelsAndVerifiedEndpoints()
    {
        VerifyPalette(LauncherTheme.Dark);
        VerifyPalette(LauncherTheme.Light);
    }

    private static void VerifyPalette(LauncherTheme theme)
    {
        var control = CreateControl(theme);
        ThemePalette palette = ThemePalette.For(theme);
        control.SetExternalActive(false);
        BitmapSource idle = Render(control);
        AssertPixel(idle, 0, 0, ToMedia(palette.Card), theme + " outer surface uses the containing card color");
        AssertPixel(idle, 6, 27, ToMedia(palette.Track), theme + " idle track uses the theme track color");

        control.BeginTranslation();
        control.ShowTranslationProgress(1f, true);
        control.MarkTranslationVerified();
        control.SetExternalActive(true);
        BitmapSource success = Render(control);
        Color endpoint = Pixel(success, 414, 27);
        if (endpoint == ToMedia(palette.Track) || endpoint == Colors.White)
            throw new InvalidOperationException(theme + " verified endpoint was not painted as progress: " + endpoint
                + ", bounds=" + control.ActualWidth + "x" + control.ActualHeight + ", value=" + control.Value);
        Console.WriteLine("PIXEL theme=" + theme + " track=" + Pixel(idle, 6, 27) + " endpoint=" + endpoint);
    }

    private static void VerifyHighDpiUsesDipGeometry()
    {
        var control = CreateControl(LauncherTheme.Dark);
        control.SetExternalActive(false);
        control.Measure(new Size(420, 56));
        control.Arrange(new Rect(0, 0, 420, 56));
        if (Math.Abs(control.ActualWidth - 420) > 0.01 || Math.Abs(control.ActualHeight - 56) > 0.01)
            throw new InvalidOperationException("progress layout dimensions must remain in device-independent units");

        BitmapSource idle = Render(control, 630, 84, 144, 144);
        AssertPixel(idle, 0, 0, ToMedia(ThemePalette.For(LauncherTheme.Dark).Card), "150% DPI outer surface uses card color");
        control.SetExternalActive(true);
        BitmapSource active = Render(control, 630, 84, 144, 144);
        Color endpoint = Pixel(active, 620, 42);
        if (endpoint == ToMedia(ThemePalette.For(LauncherTheme.Dark).Track) || endpoint == Colors.White)
            throw new InvalidOperationException("150% DPI render did not fill the expected DIP-scaled endpoint: " + endpoint);
        Console.WriteLine("DPI 150% DIP bounds=" + control.ActualWidth + "x" + control.ActualHeight
            + " bitmap=" + idle.PixelWidth + "x" + idle.PixelHeight);
    }

    private static void VerifyRestoreReversesProgress()
    {
        var control = CreateControl(LauncherTheme.Dark);
        control.SetExternalActive(true);
        float start = control.BeginRestore();
        Equal(1f, start, "restore starts from installed state");
        control.ShowRestoreProgress(0.5f, start);
        if (control.TargetValue < 0.49f || control.TargetValue > 0.51f)
            throw new InvalidOperationException("half-complete restore should target 50%, got " + control.TargetValue);
        control.MarkRestoreVerified();
        Equal(0f, control.TargetValue, "verified restore targets zero");
        Equal(1f, control.Value, "verified restore animates from its current value");
        if (control.IsVerifiedSuccess) throw new InvalidOperationException("restored state is incorrectly shown as translation success");
    }

    private static void VerifyAnimatedTerminalStatesAutomationAndVisibility()
    {
        var control = CreateControl(LauncherTheme.Dark);
        var window = new Window { Width = 460, Height = 90, WindowStyle = WindowStyle.None, ShowInTaskbar = false,
            Content = control, Background = Brushes.Transparent };
        window.Show();
        try {
            PumpUntil(() => control.IsLoaded && control.IsVisible, 1500, "progress host loads");
            AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(control);
            if (peer == null) throw new InvalidOperationException("progress control must expose an automation peer");
            var range = peer.GetPattern(PatternInterface.RangeValue) as IRangeValueProvider;
            if (range == null) throw new InvalidOperationException("progress peer must expose the RangeValue pattern");
            if (!range.IsReadOnly || range.Minimum != 0 || range.Maximum != 100)
                throw new InvalidOperationException("progress automation range must be read-only from 0 to 100");

            control.SetExternalActive(true);
            Equal(100f, (float)range.Value, "automation reports the current active percentage");
            control.BeginRestore();
            control.ShowRestoreProgress(0.25f, 1f);
            control.MarkRestoreVerified();
            Equal(1f, control.Value, "restore completion does not snap the current progress value");
            PumpUntil(() => range.Value < 1, 1500, "restore animation begins moving");
            if (range.Value <= 0 || range.Value >= 100)
                throw new InvalidOperationException("automation value should track an intermediate restore percentage");
            PumpUntil(() => !AnimationTimerEnabled(control), 4000, "restore animation timer settles");
            Equal(0f, control.Value, "restore timer settles at the exact zero endpoint");
            Equal(0f, (float)range.Value, "automation reports the exact zero endpoint after restore");

            control.BeginTranslation();
            control.ShowTranslationProgress(1f, true);
            PumpUntil(() => control.Value > 0.2f, 2500, "translation progress animates before verification");
            if (control.Value >= 1f || range.Value >= 100)
                throw new InvalidOperationException("unverified progress must remain below the 100 percent endpoint");
            control.MarkTranslationVerified();
            float atVerification = control.Value;
            if (atVerification >= 1f) throw new InvalidOperationException("verified terminal update must preserve the current value");
            Equal(100f, control.TargetValue * 100, "verified translation targets 100 percent");
            PumpUntil(() => control.Value == 1f, 4000, "verified translation animates to exact 100");
            Equal(100f, (float)range.Value, "automation reports 100 percent after verified translation settles");

            control.BeginTranslation();
            control.ShowTranslationProgress(0.9f, true);
            PumpUntil(() => control.Value > 0.1f, 2500, "hidden-page progress animation starts");
            control.Visibility = Visibility.Collapsed;
            PumpFor(30);
            float hiddenValue = control.Value;
            control.ShowTranslationProgress(0.9f, true);
            PumpFor(180);
            if (Math.Abs(control.Value - hiddenValue) > 0.002f)
                throw new InvalidOperationException("progress callbacks must not restart the animation while hidden");
            control.Visibility = Visibility.Visible;
            PumpUntil(() => control.Value > hiddenValue + 0.01f, 1500, "animation resumes when the cached page becomes visible");
        }
        finally { window.Close(); }
    }

    private static void PumpUntil(Func<bool> condition, int timeoutMs, string label)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (s, e) => { if (condition() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        if (!condition()) throw new TimeoutException("Timed out waiting for " + label);
    }

    private static void PumpFor(int milliseconds)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (s, e) => { if (DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static bool AnimationTimerEnabled(RetainedProgressControl control)
    {
        FieldInfo field = typeof(RetainedProgressControl).GetField("animation", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new InvalidOperationException("progress animation timer field was not found");
        var timer = field.GetValue(control) as DispatcherTimer;
        if (timer == null) throw new InvalidOperationException("progress animation timer was not created");
        return timer.IsEnabled;
    }

    private static RetainedProgressControl CreateControl(LauncherTheme theme)
    {
        var control = new RetainedProgressControl();
        control.ApplyTheme(ThemePalette.For(theme));
        control.Measure(new Size(420, 56));
        control.Arrange(new Rect(0, 0, 420, 56));
        return control;
    }

    private static BitmapSource Render(FrameworkElement element)
    {
        return Render(element, 420, 56, 96, 96);
    }

    private static BitmapSource Render(FrameworkElement element, int pixelWidth, int pixelHeight, double dpiX, double dpiY)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpiX, dpiY, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    private static Color Pixel(BitmapSource bitmap, int x, int y)
    {
        byte[] pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static void AssertPixel(BitmapSource bitmap, int x, int y, Color expected, string message)
    {
        Color actual = Pixel(bitmap, x, y);
        if (actual.R != expected.R || actual.G != expected.G || actual.B != expected.B)
            throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
    }

    private static Color ToMedia(System.Drawing.Color color)
    {
        return Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    private static void Equal(float expected, float actual, string message)
    {
        if (Math.Abs(expected - actual) > 0.001f)
            throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
    }
}
