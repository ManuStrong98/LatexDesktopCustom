using System.Text;
using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace LatexDesktopCustom;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private TransparentInputSurface? terminalInputSurface;
    private TransparentInputSurface? pdfInputSurface;

    public MainWindow()
    {
        InitializeComponent();
        ContentRendered += (_, _) =>
        {
            EnableTransparentBackground();
            if (terminalInputSurface is not null) return;
            terminalInputSurface = new TransparentInputSurface(this, TerminalPanel);
            pdfInputSurface = new TransparentInputSurface(this, PdfViewerPanel);
            UpdateInputSurfaces();
            terminalInputSurface.Show();
            pdfInputSurface.Show();
        };
        LocationChanged += (_, _) => UpdateInputSurfaces();
        LayoutUpdated += (_, _) => UpdateInputSurfaces();

        var workArea = SystemParameters.WorkArea;
        Width = workArea.Width * 0.9;
        Height = workArea.Height * 0.9;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    private void UpdateInputSurfaces()
    {
        terminalInputSurface?.UpdateBounds();
        pdfInputSurface?.UpdateBounds();
    }

    private void EnableTransparentBackground()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        if (source is null) return;
        source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
        source.AddHook(KeepLayeredStyle);
        SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x00080000);
        // Only background pixels become transparent; controls remain opaque.
        if (!SetLayeredWindowAttributes(handle, 0x00FF00FF, 255, 1))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private IntPtr KeepLayeredStyle(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x007C && wParam.ToInt64() == -20)
        {
            var styles = Marshal.PtrToStructure<NativeStyles>(lParam);
            styles.NewStyle |= 0x00080000;
            Marshal.StructureToPtr(styles, lParam, false);
            handled = true;
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeStyles { public uint OldStyle, NewStyle; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);
}
