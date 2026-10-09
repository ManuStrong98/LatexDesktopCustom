using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace LatexDesktopCustom;

/// <summary>Keeps the divider above the separately owned panel windows.</summary>
internal sealed class SplitterInputSurface : Window
{
    private readonly Window mainWindow;
    private readonly Grid layout;
    private readonly FrameworkElement divider;
    private readonly Thumb thumb;
    private double initialLeft;
    private double initialMouseX;
    private double totalWidth;

    public SplitterInputSurface(Window mainWindow, Grid layout, FrameworkElement divider)
    {
        this.mainWindow = mainWindow;
        this.layout = layout;
        this.divider = divider;
        Owner = mainWindow;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = Brushes.Transparent;
        thumb = new Thumb { Background = new SolidColorBrush(Color.FromRgb(176, 176, 176)), Cursor = Cursors.SizeWE };
        Content = thumb;
        thumb.DragStarted += (_, _) =>
        {
            initialLeft = layout.ColumnDefinitions[0].ActualWidth;
            totalWidth = initialLeft + layout.ColumnDefinitions[2].ActualWidth;
            GetCursorPos(out var point);
            initialMouseX = point.X;
        };
        thumb.DragDelta += (_, _) =>
        {
            GetCursorPos(out var point);
            var dpi = VisualTreeHelper.GetDpi(mainWindow);
            var leftMinimum = layout.ColumnDefinitions[0].MinWidth;
            var rightMinimum = layout.ColumnDefinitions[2].MinWidth;
            if (totalWidth < leftMinimum + rightMinimum) return;
            var left = Math.Clamp(initialLeft + (point.X - initialMouseX) / dpi.DpiScaleX, leftMinimum, totalWidth - rightMinimum);
            layout.ColumnDefinitions[0].Width = new GridLength(left, GridUnitType.Star);
            layout.ColumnDefinitions[2].Width = new GridLength(totalWidth - left, GridUnitType.Star);
        };
    }

    public void UpdateBounds()
    {
        if (mainWindow.WindowState == WindowState.Minimized) return;
        var point = divider.PointToScreen(new Point());
        var dpi = VisualTreeHelper.GetDpi(mainWindow);
        if (Left != point.X / dpi.DpiScaleX) Left = point.X / dpi.DpiScaleX;
        if (Top != point.Y / dpi.DpiScaleY) Top = point.Y / dpi.DpiScaleY;
        if (Width != divider.ActualWidth) Width = Math.Max(1, divider.ActualWidth);
        if (Height != divider.ActualHeight) Height = Math.Max(1, divider.ActualHeight);
    }

    public void BringToFront()
    {
        if (IsVisible && mainWindow.WindowState != WindowState.Minimized)
            SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0013);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
