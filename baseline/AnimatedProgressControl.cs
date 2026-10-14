using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

public sealed class AnimatedProgressControl : Control
{
    private readonly Timer animation;
    private float currentValue;
    private float targetValue;
    private float shimmerPosition;
    private float floatPhase;
    private bool indeterminate;
    private bool verifiedSuccess;
    private int lastAccessiblePercent = -1;
    private ThemePalette palette = ThemePalette.For(LauncherTheme.Dark);

    public float Value { get { return currentValue; } }
    public float TargetValue { get { return targetValue; } }
    public bool IsIndeterminate { get { return indeterminate; } }
    public bool IsVerifiedSuccess { get { return verifiedSuccess; } }

    public AnimatedProgressControl()
    {
        DoubleBuffered = true;
        Height = 56;
        MinimumSize = new Size(200, 50);
        AccessibleRole = AccessibleRole.ProgressBar;
        AccessibleName = "ความคืบหน้าการแปลเกม";
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.SupportsTransparentBackColor, true);
        animation = new Timer { Interval = 16 };
        animation.Tick += Animate;
    }

    protected override AccessibleObject CreateAccessibilityInstance()
    {
        return new ProgressAccessibleObject(this);
    }

    private sealed class ProgressAccessibleObject : ControlAccessibleObject
    {
        private readonly AnimatedProgressControl owner;
        public ProgressAccessibleObject(AnimatedProgressControl owner) : base(owner) { this.owner = owner; }
        public override string Value
        {
            get { return String.Format("{0}%", (int)Math.Round(owner.currentValue * 100f)); }
            set { }
        }
    }

    private void NotifyAccessibleValueChanged()
    {
        int percent = (int)Math.Round(currentValue * 100f);
        if (percent == lastAccessiblePercent) return;
        lastAccessiblePercent = percent;
        AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
    }

    public void ApplyTheme(ThemePalette value)
    {
        palette = value ?? ThemePalette.For(LauncherTheme.Dark);
        BackColor = Color.Transparent;
        Invalidate();
    }

    public void BeginTranslation()
    {
        verifiedSuccess = false;
        indeterminate = false;
        currentValue = 0;
        targetValue = 0;
        NotifyAccessibleValueChanged();
        AccessibleDescription = "กำลังเริ่มแปลเกม";
        StartAnimation();
    }

    public float BeginRestore()
    {
        verifiedSuccess = false;
        indeterminate = false;
        targetValue = currentValue;
        AccessibleDescription = "กำลังคืนค่าเดิม";
        StartAnimation();
        return currentValue;
    }

    public void ShowTranslationProgress(float fraction, bool hasStages)
    {
        verifiedSuccess = false;
        indeterminate = !hasStages;
        if (hasStages) targetValue = Clamp(fraction, 0f, 0.99f);
        AccessibleDescription = hasStages ? String.Format("ความคืบหน้า {0:0}%", targetValue * 100f) : "กำลังดำเนินการ";
        StartAnimation();
    }

    public void ShowRestoreProgress(float fraction, float startValue)
    {
        verifiedSuccess = false;
        indeterminate = false;
        targetValue = Clamp(startValue, 0f, 1f) * (1f - Clamp(fraction, 0f, 1f));
        AccessibleDescription = String.Format("กำลังคืนค่า {0:0}%", (1f - Clamp(fraction, 0f, 1f)) * 100f);
        StartAnimation();
    }

    public void MarkTranslationVerified()
    {
        indeterminate = false;
        verifiedSuccess = true;
        targetValue = 1f;
        AccessibleDescription = "แปลเกมเสร็จแล้ว 100%";
        StartAnimation();
    }

    public void SetExternalActive(bool active)
    {
        indeterminate = false;
        verifiedSuccess = active;
        currentValue = targetValue = active ? 1f : 0f;
        NotifyAccessibleValueChanged();
        AccessibleDescription = active ? "ติดตั้งคำแปลแล้ว 100%" : "ยังไม่ได้เริ่มแปลเกม";
        if (animation != null) animation.Stop();
        Invalidate();
    }

    public void MarkRestoreVerified()
    {
        indeterminate = false;
        verifiedSuccess = false;
        targetValue = 0f;
        AccessibleDescription = "คืนค่าเดิมแล้ว";
        StartAnimation();
    }

    public void StopAtCurrentValue()
    {
        indeterminate = false;
        verifiedSuccess = false;
        targetValue = currentValue;
        if (animation != null) animation.Stop();
        Invalidate();
    }

    private static float Clamp(float value, float min, float max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private void StartAnimation()
    {
        if (!animation.Enabled) animation.Start();
        Invalidate();
    }

    private void Animate(object sender, EventArgs e)
    {
        float delta = targetValue - currentValue;
        if (Math.Abs(delta) > 0.002f) currentValue += delta * 0.24f;
        else currentValue = targetValue;
        NotifyAccessibleValueChanged();
        if (indeterminate) shimmerPosition = (shimmerPosition + 0.035f) % 1.4f;
        if (verifiedSuccess) floatPhase += 0.06f;
        Invalidate();
        if (!indeterminate && !verifiedSuccess && Math.Abs(targetValue - currentValue) < 0.002f) {
            currentValue = targetValue;
            animation.Stop();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width <= 0 || Height <= 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float dpi = DeviceDpi / 96f;
        float inset = 1f * dpi;
        float trackHeight = Math.Min(22f * dpi, Math.Max(14f * dpi, Height - 32f * dpi));
        RectangleF track = new RectangleF(inset, (Height - trackHeight) / 2f,
            Math.Max(1f, Width - inset * 2f), trackHeight);
        float radius = Math.Min(track.Height / 2f, 10f * dpi);
        using (GraphicsPath trackPath = RoundedRect(track, radius))
        using (SolidBrush trackBrush = new SolidBrush(palette.Track)) e.Graphics.FillPath(trackBrush, trackPath);

        float fillWidth = track.Width * (indeterminate ? 0.32f : Clamp(currentValue, 0f, 1f));
        if (fillWidth > 1f) {
            RectangleF fillRect = new RectangleF(track.X, track.Y, Math.Min(track.Width, fillWidth), track.Height);
            float ratio = indeterminate ? Math.Max(0.18f, currentValue) : currentValue;
            using (GraphicsPath fillPath = RoundedRect(fillRect, radius))
            using (LinearGradientBrush fillBrush = new LinearGradientBrush(fillRect,
                ProgressColor(Math.Max(0f, ratio - 0.16f)), ProgressColor(ratio), LinearGradientMode.Horizontal)) {
                e.Graphics.FillPath(fillBrush, fillPath);
            }
        }
        if (indeterminate) {
            float shimmerWidth = Math.Max(22f * dpi, track.Width * 0.18f);
            float x = track.X + (shimmerPosition * track.Width) - shimmerWidth;
            RectangleF shimmer = new RectangleF(x, track.Y, shimmerWidth, track.Height);
            using (GraphicsPath shimmerPath = RoundedRect(shimmer, radius))
            using (SolidBrush shimmerBrush = new SolidBrush(Color.FromArgb(90, Color.White))) e.Graphics.FillPath(shimmerBrush, shimmerPath);
        }

        string caption = indeterminate ? "กำลังทำงาน" : String.Format("{0:0}%", currentValue * 100f);
        using (Font captionFont = new Font("Leelawadee UI", 8f, FontStyle.Bold, GraphicsUnit.Point))
        using (SolidBrush captionBrush = new SolidBrush(palette.IsDark ? Color.FromArgb(237, 243, 250) : Color.FromArgb(32, 53, 78))) {
            SizeF textSize = e.Graphics.MeasureString(caption, captionFont);
            float floatY = verifiedSuccess ? (float)Math.Sin(floatPhase) * 0.8f * dpi : 0f;
            e.Graphics.DrawString(caption, captionFont, captionBrush,
                new PointF(Math.Max(2f, (Width - textSize.Width) / 2f), Math.Max(0f, (Height - textSize.Height) / 2f + floatY)));
        }
    }

    private static Color ProgressColor(float ratio)
    {
        ratio = Clamp(ratio, 0f, 1f);
        Color blue = Color.FromArgb(96, 146, 196);
        Color amber = Color.FromArgb(225, 170, 92);
        Color mint = Color.FromArgb(85, 202, 166);
        if (ratio <= 0.55f) return Mix(blue, amber, ratio / 0.55f);
        return Mix(amber, mint, (ratio - 0.55f) / 0.45f);
    }

    private static Color Mix(Color a, Color b, float ratio)
    {
        ratio = Clamp(ratio, 0f, 1f);
        return Color.FromArgb((int)(a.R + (b.R - a.R) * ratio),
            (int)(a.G + (b.G - a.G) * ratio), (int)(a.B + (b.B - a.B) * ratio));
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        float diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
        var path = new GraphicsPath();
        if (diameter <= 1f) { path.AddRectangle(rect); return path; }
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180f, 90f);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270f, 90f);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && animation != null) { animation.Stop(); animation.Dispose(); }
        base.Dispose(disposing);
    }
}
