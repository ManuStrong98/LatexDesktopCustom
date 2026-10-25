using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace LatexDesktopCustom;

// Owned surfaces also receive input above the native Terminal window.
internal sealed class ResizeInputSurface : Window
{
    private readonly Window main;
    private readonly int direction;
    public ResizeInputSurface(Window main, int direction)
    {
        this.main = main;
        this.direction = direction;
        Owner = main;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        ShowInTaskbar = false;
        ShowActivated = false;
        Cursor = direction switch
        {
            1 or 2 => Cursors.SizeWE,
            3 or 6 => Cursors.SizeNS,
            4 or 8 => Cursors.SizeNWSE,
            _ => Cursors.SizeNESW
        };
        MouseLeftButtonDown += (_, e) =>
        {
            ReleaseCapture();
            SendMessage(new WindowInteropHelper(main).Handle, 0x0112, (IntPtr)(0xF000 + direction), IntPtr.Zero);
            e.Handled = true;
        };
    }

    public void UpdateBounds()
    {
        if (main.WindowState != WindowState.Normal) { Hide(); return; }
        const double edge = 6;
        const double corner = 12;
        var w = main.ActualWidth;
        var h = main.ActualHeight;
        var (x, y, width, height) = direction switch
        {
            1 => (0d, corner, edge, h - corner * 2),
            2 => (w - edge, corner, edge, h - corner * 2),
            3 => (corner, 0d, w - corner * 2, edge),
            4 => (0d, 0d, corner, corner),
            5 => (w - corner, 0d, corner, corner),
            6 => (corner, h - edge, w - corner * 2, edge),
            7 => (0d, h - corner, corner, corner),
            _ => (w - corner, h - corner, corner, corner)
        };
        var origin = main.PointToScreen(new Point(x, y));
        var dpi = VisualTreeHelper.GetDpi(main);
        Left = origin.X / dpi.DpiScaleX;
        Top = origin.Y / dpi.DpiScaleY;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        if (!IsVisible) Show();
    }

    public void BringToFront()
    {
        if (IsVisible) SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0013);
    }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
