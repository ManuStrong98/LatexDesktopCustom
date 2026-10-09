using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;

namespace LatexDesktopCustom.Terminal;

/// <summary>Hosts a new Windows Terminal window using the Windows PowerShell profile.</summary>
public sealed class PowerShellWindowHost : HwndHost
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int GwlStyle = -16;
    private readonly DispatcherTimer startupTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly string windowTitle = $"LatexDesktop-{Guid.NewGuid():N}";
    private Process? consoleProcess;
    private IntPtr container;
    private IntPtr consoleWindow;
    private DateTime startupDeadline;

    public event Action<string>? StartupFailed;

    public PowerShellWindowHost()
    {
        Focusable = true;
        startupTimer.Tick += FindConsoleWindow;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        container = CreateWindowEx(0, "static", "", WsChild | WsVisible | 0x02000000,
            0, 0, 1, 1, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (container == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var shell = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            var host = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
            var start = new ProcessStartInfo(host)
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                WindowStyle = ProcessWindowStyle.Hidden
            };
            start.ArgumentList.Add("--window");
            start.ArgumentList.Add("new");
            start.ArgumentList.Add("new-tab");
            start.ArgumentList.Add("--profile");
            start.ArgumentList.Add("Windows PowerShell");
            start.ArgumentList.Add("--title");
            start.ArgumentList.Add(windowTitle);
            start.ArgumentList.Add("--suppressApplicationTitle");
            start.ArgumentList.Add("--startingDirectory");
            start.ArgumentList.Add(start.WorkingDirectory);
            start.ArgumentList.Add(shell);
            start.ArgumentList.Add("-NoLogo");
            start.ArgumentList.Add("-NoExit");
            start.ArgumentList.Add("-EncodedCommand");
            start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes("nvim")));
            consoleProcess = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar PowerShell.");
            startupDeadline = DateTime.UtcNow.AddSeconds(20);
            startupTimer.Start();
        }
        catch (Exception ex)
        {
            Dispatcher.BeginInvoke(() => StartupFailed?.Invoke($"No se pudo abrir PowerShell: {ex.Message}"));
        }
        return new HandleRef(this, container);
    }

    private void FindConsoleWindow(object? sender, EventArgs e)
    {
        EnumWindows((window, _) =>
        {
            var title = new StringBuilder(512);
            var className = new StringBuilder(128);
            GetWindowText(window, title, title.Capacity);
            GetClassName(window, className, className.Capacity);
            if (className.ToString() == "CASCADIA_HOSTING_WINDOW_CLASS" && title.ToString().Contains(windowTitle, StringComparison.Ordinal))
            {
                consoleWindow = window;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (consoleWindow != IntPtr.Zero)
        {
            startupTimer.Stop();
            // Remove caption, frame, popup style and scroll bars before parenting.
            var style = GetWindowLong(consoleWindow, GwlStyle);
            SetWindowLong(consoleWindow, GwlStyle, (style & ~unchecked((int)0x80F70000)) | WsChild | WsVisible);
            SetLastError(0);
            var previousParent = SetParent(consoleWindow, container);
            var error = Marshal.GetLastWin32Error();
            if (previousParent == IntPtr.Zero && error != 0)
            {
                StartupFailed?.Invoke($"No se pudo integrar la consola: {new Win32Exception(error).Message}");
                return;
            }
            SetWindowPos(consoleWindow, IntPtr.Zero, 0, 0, 0, 0, 0x0027);
            ResizeConsole();
            ShowWindow(consoleWindow, 4);
            SetFocus(consoleWindow);
        }
        else if (DateTime.UtcNow >= startupDeadline)
        {
            startupTimer.Stop();
            StartupFailed?.Invoke("No se encontró Windows Terminal con el perfil Windows PowerShell.");
        }
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        ResizeConsole();
    }

    private void ResizeConsole()
    {
        if (consoleWindow != IntPtr.Zero && GetClientRect(container, out var bounds))
            MoveWindow(consoleWindow, 0, 0, Math.Max(1, bounds.Right), Math.Max(1, bounds.Bottom), true);
    }

    protected override bool TabIntoCore(TraversalRequest request)
    {
        return consoleWindow != IntPtr.Zero && SetFocus(consoleWindow) != IntPtr.Zero;
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        startupTimer.Stop();
        if (consoleProcess is not null)
        {
            // Windows Terminal may share a process with the user's other windows.
            // Close only the unique window created for this application.
            if (consoleWindow != IntPtr.Zero)
                PostMessage(consoleWindow, 0x0010, IntPtr.Zero, IntPtr.Zero);
            consoleProcess.Dispose();
            consoleProcess = null;
        }
        DestroyWindow(hwnd.Handle);
        container = consoleWindow = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("kernel32.dll")] private static extern void SetLastError(uint error);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
