using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace LatexDesktopCustom;

public partial class MainWindow
{
    private bool pdfViewerInitializing;

    private async Task InitializePdfViewerAsync()
    {
        if (pdfViewerInitializing || isClosing) return;
        pdfViewerInitializing = true;
        PdfViewerStatusPanel.Visibility = Visibility.Visible;
        RetryPdfViewerButton.Visibility = Visibility.Collapsed;
        PdfViewerStatus.Text = "Iniciando visor PDF…";

        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LatexDesktopCustom", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            if (isClosing) return;
            await PdfWebView.EnsureCoreWebView2Async(environment);
            if (isClosing) return;

            PdfWebView.NavigateToString("""
                <!doctype html>
                <html lang="es">
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1">
                    <title>Visor PDF</title>
                    <style>
                        body { margin: 0; min-height: 100vh; display: grid; place-items: center;
                               background: #fff; color: #374151; font: 15px 'Segoe UI', sans-serif; }
                        main { padding: 32px; text-align: center; }
                        h1 { font-size: 22px; font-weight: 600; }
                        p { line-height: 1.6; }
                    </style>
                </head>
                <body><main><h1>Visor PDF</h1><p>Aquí se mostrarán tus documentos LaTeX.</p></main></body>
                </html>
                """);
            PdfViewerStatusPanel.Visibility = Visibility.Collapsed;
            OpenPdfButton.IsEnabled = true;

            var arguments = Environment.GetCommandLineArgs();
            var pdfArgument = Array.IndexOf(arguments, "--pdf");
            if (pdfArgument >= 0)
            {
                if (pdfArgument + 1 >= arguments.Length)
                    throw new ArgumentException("Falta la ruta del PDF después de --pdf.");
                OpenPdfFile(arguments[pdfArgument + 1]);
            }
        }
        catch (Exception ex) when (!isClosing)
        {
            PdfViewerStatusPanel.Visibility = Visibility.Visible;
            PdfViewerStatus.Text = ex is WebView2RuntimeNotFoundException
                ? "Instala Microsoft Edge WebView2 Runtime para iniciar el visor PDF."
                : $"No se pudo iniciar el visor PDF: {ex.Message}";
            RetryPdfViewerButton.Visibility = Visibility.Visible;
        }
        catch (Exception) when (isClosing)
        {
            // The window may close while the browser is being initialized.
        }
        finally
        {
            pdfViewerInitializing = false;
        }
    }

    private async void RetryPdfViewer_Click(object sender, RoutedEventArgs e)
        => await InitializePdfViewerAsync();

    private void OpenPdf_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Abrir documento PDF",
            Filter = "Documentos PDF (*.pdf)|*.pdf",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true) return;
        OpenPdfFile(picker.FileName);
    }

    private void OpenPdfFile(string fileName)
    {
        var fullPath = Path.GetFullPath(fileName);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("No se encontró el PDF.", fullPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Selecciona un archivo PDF.");
        PdfWebView.CoreWebView2.Navigate(new Uri(fullPath).AbsoluteUri);
        PdfFileName.Text = Path.GetFileName(fullPath);
        PdfFileName.ToolTip = fullPath;
    }
}
