using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public static class WindowChrome
{
    private const int DwmwaUseImmersiveDarkMode19 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    public static void Apply(Form window, ThemePalette palette)
    {
        if (window == null || palette == null || !window.IsHandleCreated) return;
        try {
            int dark = palette.IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(window.Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(window.Handle, DwmwaUseImmersiveDarkMode19, ref dark, sizeof(int));
            int caption = ColorRef(palette.Window);
            int border = ColorRef(palette.Border);
            int text = ColorRef(palette.Text);
            DwmSetWindowAttribute(window.Handle, DwmwaCaptionColor, ref caption, sizeof(int));
            DwmSetWindowAttribute(window.Handle, DwmwaBorderColor, ref border, sizeof(int));
            DwmSetWindowAttribute(window.Handle, DwmwaTextColor, ref text, sizeof(int));
            window.Invalidate();
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        catch (ExternalException) { }
    }

    private static int ColorRef(Color color)
    {
        return color.R | (color.G << 8) | (color.B << 16);
    }
}
