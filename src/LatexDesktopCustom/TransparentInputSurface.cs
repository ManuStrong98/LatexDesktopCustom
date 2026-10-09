using System.Windows;
using System.Windows.Media;

namespace LatexDesktopCustom;

internal sealed class TransparentInputSurface : Window
{
    private readonly Window mainWindow;
    private readonly FrameworkElement panel;

    public TransparentInputSurface(Window mainWindow, FrameworkElement panel)
    {
        this.mainWindow = mainWindow;
        this.panel = panel;
        Owner = mainWindow;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        // Zero alpha lets clicks through. The minimum alpha captures them
        // with a visually imperceptible tint (1/255).
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        ShowInTaskbar = false;
        ShowActivated = false;
        MouseDown += (_, _) => mainWindow.Activate();
    }

    public void UpdateBounds()
    {
        if (mainWindow.WindowState == WindowState.Minimized || !panel.IsVisible) return;
        var origin = panel.PointToScreen(new Point(0, 0));
        var dpi = VisualTreeHelper.GetDpi(mainWindow);
        var left = origin.X / dpi.DpiScaleX;
        var top = origin.Y / dpi.DpiScaleY;
        if (Left != left) Left = left;
        if (Top != top) Top = top;
        if (Width != panel.ActualWidth) Width = Math.Max(1, panel.ActualWidth);
        if (Height != panel.ActualHeight) Height = Math.Max(1, panel.ActualHeight);
    }
}
