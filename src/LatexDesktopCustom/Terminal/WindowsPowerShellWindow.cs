using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace LatexDesktopCustom.Terminal;

/// <summary>Positions an owned Windows Terminal window over the left panel.</summary>
internal sealed class WindowsPowerShellWindow : IDisposable
{
    private readonly Window owner;
    private readonly FrameworkElement panel;
    private readonly string title = $"LatexDesktop-{Guid.NewGuid():N}";
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private DateTime deadline;
    private IntPtr terminal;
    private bool disposed;
    private NativeRect lastBounds;
    private IntPtr attachedWindow;
    private IntPtr candidateWindow;
    private DateTime candidateSince;
    public event Action? Ready;
    public event Action<string>? Failed;

    public WindowsPowerShellWindow(Window owner, FrameworkElement panel)
    {
        this.owner = owner;
        this.panel = panel;
        timer.Tick += FindWindow;
    }

    public void Start()
    {
        try
        {
            var launcher = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
            var shell = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var start = new ProcessStartInfo(launcher) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var argument in new[] { "--window", "new", "new-tab", "--profile", "Windows PowerShell", "--title", title,
                "--suppressApplicationTitle", "--startingDirectory", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                shell, "-NoLogo", "-NoExit", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes("nvim")) })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar Windows Terminal.");
            deadline = DateTime.UtcNow.AddSeconds(30);
            timer.Start();
        }
        catch (Exception ex) { Failed?.Invoke(ex.Message); }
    }

    private void FindWindow(object? sender, EventArgs args)
    {
        if (disposed) return;
        if (attachedWindow != IntPtr.Zero && IsWindow(attachedWindow))
        {
            // Settings reloads can restore window decorations. Reapply docking
            // without changing Terminal's opacity, colors or acrylic settings.
            RemoveFrame();
            UpdateBounds();
            return;
        }
        if (attachedWindow != IntPtr.Zero)
        {
            attachedWindow = terminal = candidateWindow = IntPtr.Zero;
            lastBounds = default;
            deadline = DateTime.UtcNow.AddSeconds(10);
            Failed?.Invoke("Reconectando Windows PowerShell…");
        }
        terminal = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            var name = new StringBuilder(512);
            var className = new StringBuilder(128);
            GetWindowText(window, name, name.Capacity);
            GetClassName(window, className, className.Capacity);
            if (className.ToString() != "CASCADIA_HOSTING_WINDOW_CLASS" || !name.ToString().Contains(title, StringComparison.Ordinal)) return true;
            terminal = window;
            return false;
        }, IntPtr.Zero);
        if (terminal == IntPtr.Zero)
        {
            if (DateTime.UtcNow >= deadline)
            {
                timer.Stop();
                Failed?.Invoke("No se encontró la ventana de Windows Terminal con el perfil Windows PowerShell.");
            }
            return;
        }
        // Wait for Terminal to finish constructing its composition surface
        // before modifying the frame or ownership.
        if (candidateWindow != terminal)
        {
            candidateWindow = terminal;
            candidateSince = DateTime.UtcNow;
            return;
        }
        if (DateTime.UtcNow - candidateSince < TimeSpan.FromSeconds(1)) return;
        var ownerHandle = new WindowInteropHelper(owner).Handle;
        // Keep Terminal top-level to preserve its native transparency. Do not SetParent.
        if (IntPtr.Size == 8) SetWindowLongPtr(terminal, -8, ownerHandle);
        else SetWindowLong(terminal, -8, ownerHandle.ToInt32());
        attachedWindow = terminal;
        RemoveFrame();
        UpdateBounds();
        ShowWindow(terminal, 4);
        BringToFront();
        Ready?.Invoke();
    }

    private void RemoveFrame()
    {
        var style = GetWindowLong(terminal, -16);
        var dockedStyle = style & ~0x00C40000; // Caption and resize frame only.
        if (style == dockedStyle) return;
        SetWindowLong(terminal, -16, dockedStyle);
        SetWindowPos(terminal, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
    }

    public void BringToFront()
    {
        if (!disposed && attachedWindow != IntPtr.Zero && IsWindow(attachedWindow)
            && owner.WindowState != WindowState.Minimized)
            SetWindowPos(attachedWindow, IntPtr.Zero, 0, 0, 0, 0, 0x0013);
    }

    public void UpdateBounds()
    {
        if (disposed || terminal == IntPtr.Zero || owner.WindowState == WindowState.Minimized) return;
        if (!IsWindow(terminal))
        {
            // The monitor reacquires a replacement HWND after a settings reload.
            return;
        }
        var origin = panel.PointToScreen(new Point(0, 0));
        var dpi = VisualTreeHelper.GetDpi(owner);
        var bounds = new NativeRect { Left = (int)Math.Round(origin.X), Top = (int)Math.Round(origin.Y),
            Right = Math.Max(1, (int)Math.Round(panel.ActualWidth * dpi.DpiScaleX)),
            Bottom = Math.Max(1, (int)Math.Round(panel.ActualHeight * dpi.DpiScaleY)) };
        if (bounds.Equals(lastBounds)) return;
        lastBounds = bounds;
        SetWindowPos(terminal, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, 0x0014);
    }

    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        // Never terminate Windows Terminal's shared process or other user sessions.
        if (terminal != IntPtr.Zero && IsWindow(terminal)) PostMessage(terminal, 0x0010, IntPtr.Zero, IntPtr.Zero);
        terminal = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumCallback(IntPtr hwnd, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr state);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int length);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
