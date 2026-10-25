using System.Windows.Input;
using System.Text.Json;
using Microsoft.Web.WebView2.Wpf;

namespace LatexDesktopCustom.PdfViewer;

/// <summary>Preserves Chromium's native double-click sequence for PDF.js.</summary>
public sealed class PdfWebViewControl : WebView2CompositionControl
{
    public event Action<string>? NavigationInputFailed;

    protected override async void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        // Capture the WPF event directly, rather than relying on Chromium's
        // double-click count after the composition control forwards mouse input.
        // https://github.com/MicrosoftEdge/WebView2Feedback/issues/5099
        if (e.ChangedButton != MouseButton.Left || ActualWidth <= 0 || ActualHeight <= 0 || CoreWebView2 is null) return;
        var point = e.GetPosition(this);
        try { await ForwardDoubleClickAsync(point.X / ActualWidth, point.Y / ActualHeight); }
        catch (Exception) { NavigationInputFailed?.Invoke("No se pudo enviar el doble clic al visor. Vuelve a abrir o compilar el PDF."); }
    }

    internal Task<string> ForwardDoubleClickAsync(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x is < 0 or > 1 || y is < 0 or > 1)
            return Task.FromResult("false");
        var position = JsonSerializer.Serialize(new { x, y });
        return CoreWebView2.ExecuteScriptAsync($"window.latexDesktopNavigateAt?.({position}) ?? false");
    }
}
