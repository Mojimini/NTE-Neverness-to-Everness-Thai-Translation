using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Threading;

/// <summary>A single-surface, retained-mode progress indicator for launcher work.</summary>
public sealed class RetainedProgressControl : FrameworkElement
{
    private readonly DispatcherTimer animation;
    private ThemePalette palette = ThemePalette.For(LauncherTheme.Dark);
    private double value;
    private double target;
    private double shimmer;
    private double pulse;
    private bool indeterminate;
    private bool verifiedSuccess;
    private bool operationActive;
    private int lastAutomationPercent;

    public float Value { get { return (float)value; } }
    internal float TargetValue { get { return (float)target; } }
    public bool IsIndeterminate { get { return indeterminate; } }
    public bool IsVerifiedSuccess { get { return verifiedSuccess; } }

    public RetainedProgressControl()
    {
        Focusable = false;
        SnapsToDevicePixels = true;
        MinHeight = 52;
        Height = 56;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(this, "ความคืบหน้าการแปลเกม");
        AutomationProperties.SetHelpText(this, "แสดงความคืบหน้าของการแปลหรือคืนค่าเดิม");
        animation = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        animation.Tick += Animate;
        Loaded += (s, e) => ResumeAnimation();
        Unloaded += (s, e) => animation.Stop();
        IsVisibleChanged += (s, e) => {
            if (IsVisible) ResumeAnimation();
            else animation.Stop();
        };
    }

    public void ApplyTheme(ThemePalette value)
    {
        palette = value ?? ThemePalette.For(LauncherTheme.Dark);
        InvalidateVisual();
    }

    public void BeginTranslation()
    {
        operationActive = true;
        verifiedSuccess = false;
        indeterminate = false;
        value = 0;
        target = 0.06;
        UpdateAccessibilityText();
        ResumeAnimation();
        InvalidateVisual();
    }

    public float BeginRestore()
    {
        operationActive = true;
        verifiedSuccess = false;
        indeterminate = false;
        target = value;
        UpdateAccessibilityText();
        ResumeAnimation();
        InvalidateVisual();
        return (float)value;
    }

    public void ShowTranslationProgress(float fraction, bool hasStages)
    {
        operationActive = true;
        verifiedSuccess = false;
        indeterminate = !hasStages;
        if (hasStages) target = Clamp(fraction, 0, 0.94);
        else target = Math.Min(0.94, Math.Max(target, 0.08));
        UpdateAccessibilityText();
        ResumeAnimation();
        InvalidateVisual();
    }

    public void ShowRestoreProgress(float fraction, float startValue)
    {
        operationActive = true;
        verifiedSuccess = false;
        indeterminate = false;
        double start = Clamp(startValue, 0, 1);
        target = start * (1 - Clamp(fraction, 0, 1));
        UpdateAccessibilityText();
        ResumeAnimation();
        InvalidateVisual();
    }

    public void MarkTranslationVerified()
    {
        double oldValue = value;
        operationActive = false;
        indeterminate = false;
        verifiedSuccess = true;
        target = 1;
        UpdateAccessibilityText();
        NotifyValueChanged(oldValue);
        ResumeAnimation();
        InvalidateVisual();
    }

    public void MarkRestoreVerified()
    {
        double oldValue = value;
        operationActive = false;
        indeterminate = false;
        verifiedSuccess = false;
        target = 0;
        UpdateAccessibilityText();
        NotifyValueChanged(oldValue);
        ResumeAnimation();
        InvalidateVisual();
    }

    public void StopAtCurrentValue()
    {
        operationActive = false;
        indeterminate = false;
        verifiedSuccess = false;
        target = value;
        UpdateAccessibilityText();
        StopIfSettled();
        InvalidateVisual();
    }

    public void SetExternalActive(bool active)
    {
        double oldValue = value;
        operationActive = false;
        indeterminate = false;
        verifiedSuccess = active;
        value = target = active ? 1 : 0;
        UpdateAccessibilityText();
        NotifyValueChanged(oldValue);
        if (active) ResumeAnimation(); else StopIfSettled();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = Double.IsInfinity(availableSize.Width) ? 420 : availableSize.Width;
        return new Size(Math.Max(160, width), 56);
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new ProgressAutomationPeer(this);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        double width = ActualWidth;
        double height = ActualHeight;
        if (width < 2 || height < 2) return;

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        drawingContext.DrawRectangle(Brush(palette.Card), null, new Rect(0, 0, width, height));

        double inset = 1;
        double trackHeight = Math.Min(22, Math.Max(14, height - 30));
        Rect track = new Rect(inset, (height - trackHeight) / 2, Math.Max(1, width - 2 * inset), trackHeight);
        double radius = trackHeight / 2;
        drawingContext.DrawRoundedRectangle(Brush(palette.Track), null, track, radius, radius);

        double fraction = indeterminate ? 0.3 : Clamp(value, 0, 1);
        double fillWidth = track.Width * fraction;
        if (fillWidth > 0.5)
        {
            Rect fill = new Rect(track.X, track.Y, Math.Min(track.Width, fillWidth), track.Height);
            Color from = ProgressColor(Math.Max(0, fraction - 0.16));
            Color to = ProgressColor(fraction);
            var gradient = new LinearGradientBrush(from, to, new Point(0, 0.5), new Point(1, 0.5));
            gradient.Freeze();
            drawingContext.DrawRoundedRectangle(gradient, null, fill, radius, radius);
        }

        if (indeterminate)
        {
            double shimmerWidth = Math.Max(24, track.Width * 0.18);
            double travel = track.Width + shimmerWidth;
            double x = track.X + (shimmer * travel) - shimmerWidth;
            Rect shimmerRect = new Rect(x, track.Y, shimmerWidth, track.Height);
            var shimmerBrush = new LinearGradientBrush();
            shimmerBrush.StartPoint = new Point(0, 0.5);
            shimmerBrush.EndPoint = new Point(1, 0.5);
            shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
            shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(112, 255, 255, 255), 0.5));
            shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            shimmerBrush.Freeze();
            drawingContext.PushClip(new RectangleGeometry(track, radius, radius));
            drawingContext.DrawRoundedRectangle(shimmerBrush, null, shimmerRect, radius, radius);
            drawingContext.Pop();
        }

        string caption = indeterminate ? "กำลังทำงาน" : String.Format(CultureInfo.InvariantCulture, "{0:0}%", value * 100);
        double captionSize = 11;
        var typeface = new Typeface(new FontFamily("Leelawadee UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var text = new FormattedText(caption, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            typeface, captionSize, Brush(palette.Text), pixelsPerDip);
        double floatY = verifiedSuccess ? Math.Sin(pulse) * 0.7 : 0;
        drawingContext.DrawText(text, new Point(Math.Max(2, (width - text.WidthIncludingTrailingWhitespace) / 2),
            Math.Max(0, (height - text.Height) / 2 + floatY)));
    }

    private void Animate(object sender, EventArgs e)
    {
        double oldValue = value;
        double delta = target - value;
        if (Math.Abs(delta) > 0.002) value += delta * 0.24;
        else value = target;
        if (Math.Abs(target - value) <= 0.002) value = target;
        if (indeterminate) shimmer = (shimmer + 0.035) % 1.0;
        if (verifiedSuccess && value >= 0.995) pulse += 0.06;
        if (Math.Abs(value - oldValue) > 0.00001) {
            UpdateAccessibilityText();
            NotifyValueChanged(oldValue);
        }
        InvalidateVisual();
        if (!indeterminate && !verifiedSuccess && Math.Abs(target - value) < 0.002)
            animation.Stop();
    }

    private void ResumeAnimation()
    {
        if (IsLoaded && IsVisible && (indeterminate || verifiedSuccess || Math.Abs(target - value) > 0.002))
            animation.Start();
    }

    private void StopIfSettled()
    {
        if (!indeterminate && !verifiedSuccess && Math.Abs(target - value) < 0.002) animation.Stop();
        else ResumeAnimation();
    }

    private void UpdateAccessibilityText()
    {
        AutomationProperties.SetItemStatus(this, indeterminate ? "กำลังทำงาน" :
            String.Format(CultureInfo.InvariantCulture, "{0:0}%", value * 100));
        AutomationProperties.SetHelpText(this, verifiedSuccess ? "ติดตั้งคำแปลแล้ว 100%" :
            operationActive ? (indeterminate ? "กำลังดำเนินการ" : "กำลังดำเนินการ " + (int)(target * 100) + "%") :
            (value <= 0 ? "ยังไม่ได้เริ่มแปลเกม" : "หยุดที่ความคืบหน้าปัจจุบัน"));
    }

    private void NotifyValueChanged(double oldValue)
    {
        int percent = (int)Math.Round(value * 100, MidpointRounding.AwayFromZero);
        if (percent == lastAutomationPercent) return;
        lastAutomationPercent = percent;
        ProgressAutomationPeer peer = UIElementAutomationPeer.FromElement(this) as ProgressAutomationPeer;
        if (peer != null) peer.RaiseValueChange(oldValue * 100, value * 100);
    }

    private sealed class ProgressAutomationPeer : FrameworkElementAutomationPeer, IRangeValueProvider
    {
        public ProgressAutomationPeer(RetainedProgressControl owner) : base(owner) { }
        public bool IsReadOnly { get { return true; } }
        public double LargeChange { get { return Double.NaN; } }
        public double SmallChange { get { return Double.NaN; } }
        public double Maximum { get { return 100; } }
        public double Minimum { get { return 0; } }
        public double Value { get { return ((RetainedProgressControl)Owner).Value * 100; } }

        public void SetValue(double value)
        {
            throw new InvalidOperationException("Progress is read-only.");
        }

        public override object GetPattern(PatternInterface patternInterface)
        {
            return patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);
        }

        protected override AutomationControlType GetAutomationControlTypeCore()
        {
            return AutomationControlType.ProgressBar;
        }

        protected override string GetClassNameCore()
        {
            return "ProgressBar";
        }

        internal void RaiseValueChange(double oldValue, double newValue)
        {
            RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, oldValue, newValue);
        }
    }

    private Color ProgressColor(double amount)
    {
        double fraction = Clamp(amount, 0, 1);
        Color blue = Color.FromRgb(96, 146, 196);
        Color amber = Color.FromRgb(225, 170, 92);
        Color mint = Color.FromRgb(85, 202, 166);
        return fraction <= 0.55 ? Mix(blue, amber, fraction / 0.55) : Mix(amber, mint, (fraction - 0.55) / 0.45);
    }

    private static Color Mix(Color start, Color end, double ratio)
    {
        double t = Clamp(ratio, 0, 1);
        return Color.FromRgb((byte)(start.R + (end.R - start.R) * t),
            (byte)(start.G + (end.G - start.G) * t), (byte)(start.B + (end.B - start.B) * t));
    }

    private static SolidColorBrush Brush(System.Drawing.Color color)
    {
        var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static double Clamp(double amount, double min, double max)
    {
        return Math.Max(min, Math.Min(max, amount));
    }
}
