using System.IO;
using System.Text.Json;
using System.Windows;
using LatexDesktopCustom.Compilation;
using Microsoft.Web.WebView2.Core;

namespace LatexDesktopCustom;

public partial class MainWindow
{
    private const string ViewerOrigin = "https://latexdesktop.local";
    private bool pdfViewerInitializing;
    private bool browserConfigured;
    private bool viewerReady;
    private TaskCompletionSource<bool> viewerReadySource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private string? displayedDocumentId;
    private CompilationSnapshot? displayedCompilation;
    private bool firstCompiledPdfOpened;

    private async Task InitializePdfViewerAsync()
    {
        if (pdfViewerInitializing || isClosing) return;
        pdfViewerInitializing = true;
        viewerReady = false;
        viewerReadySource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PdfViewerStatusPanel.Visibility = Visibility.Visible;
        RetryPdfViewerButton.Visibility = Visibility.Collapsed;
        OpenPdfButton.IsEnabled = CompileButton.IsEnabled = false;
        PdfViewerStatus.Text = "Iniciando visor PDF…";
        try
        {
            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LatexDesktopCustom", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            if (isClosing) return;
            await PdfWebView.EnsureCoreWebView2Async(environment);
            if (isClosing) return;
            var assets = Path.Combine(AppContext.BaseDirectory, "PdfViewer");
            if (!File.Exists(Path.Combine(assets, "index.html"))) throw new FileNotFoundException("No se encontraron los archivos del visor PDF.js.");
            var core = PdfWebView.CoreWebView2;
            if (!browserConfigured)
            {
                core.SetVirtualHostNameToFolderMapping("latexdesktop.local", assets, CoreWebView2HostResourceAccessKind.Deny);


                core.WebMessageReceived += PdfViewerMessageReceived;
                core.NewWindowRequested += (_, e) => e.Handled = true;
                core.NavigationStarting += (_, e) =>
                {
                    if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "latexdesktop.local") e.Cancel = true;
                };
                core.NavigationCompleted += (_, e) =>
                {
                    if (!e.IsSuccess) viewerReadySource.TrySetException(new InvalidOperationException("No se pudo cargar PDF.js: " + e.WebErrorStatus));
                };
                browserConfigured = true;
            }
            core.Navigate(ViewerOrigin + "/index.html");
            await viewerReadySource.Task.WaitAsync(TimeSpan.FromSeconds(25), compilationCancellation.Token);
            if (isClosing) return;
            viewerReady = true;
            PdfViewerStatusPanel.Visibility = Visibility.Collapsed;
            OpenPdfButton.IsEnabled = CompileButton.IsEnabled = true;

            var arguments = Environment.GetCommandLineArgs();
            var pdfArgument = Array.IndexOf(arguments, "--pdf");
            if (pdfArgument >= 0)
            {
                if (pdfArgument + 1 >= arguments.Length) throw new ArgumentException("Falta la ruta del PDF después de --pdf.");
                OpenPdfFile(arguments[pdfArgument + 1]);
            }
            var compileArgument = Array.IndexOf(arguments, "--compile");
            if (compileArgument >= 0)
            {
                if (compileArgument + 1 >= arguments.Length) throw new ArgumentException("Falta el archivo principal después de --compile.");
                if (await OpenMainFileInEditorAsync(arguments[compileArgument + 1])) await CompileSelectedProjectAsync();
            }
            else if (pdfArgument < 0)
            {
                if (selectedMainFile is not null) await OpenMainFileInEditorAsync(selectedMainFile);
                else await PickMainFileAsync();
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
        catch (Exception) when (isClosing) { }
        finally { pdfViewerInitializing = false; }
    }

    private async void PdfViewerMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (isClosing || !Uri.TryCreate(e.Source, UriKind.Absolute, out var source)
            || source.Scheme != "https" || source.Host != "latexdesktop.local" || source.AbsolutePath != "/index.html") return;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var message = document.RootElement;
            var type = message.GetProperty("type").GetString();
            if (type == "ready") { viewerReadySource.TrySetResult(true); return; }
            if (type == "viewerError" && !viewerReady)
            {
                viewerReadySource.TrySetException(new InvalidOperationException(message.GetProperty("error").GetString()));
                return;
            }
            if (!message.TryGetProperty("documentId", out var id) || id.GetString() != displayedDocumentId) return;
            if (type == "gotoSource")
                await GoToPdfSourceAsync(id.GetString()!, message.GetProperty("page").GetInt32(), message.GetProperty("h").GetDouble(), message.GetProperty("v").GetDouble());
            else if (type == "viewerError") CompilationStatus.Text = "No se pudo mostrar el PDF: " + message.GetProperty("error").GetString();
            else if (type == "pdfLoaded") CompilationStatus.Text = displayedCompilation is null
                ? "PDF local. Compila desde la app para activar la navegación al código."
                : "PDF listo. Doble clic sobre texto o una figura para ir al código en Neovim.";
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            if (!isClosing) CompilationStatus.Text = "No se pudo procesar la posición del PDF.";
        }
    }

    private async void RetryPdfViewer_Click(object sender, RoutedEventArgs e) => await InitializePdfViewerAsync();

    private void OpenPdf_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "Abrir documento PDF", Filter = "Documentos PDF (*.pdf)|*.pdf", CheckFileExists = true, Multiselect = false };
        if (picker.ShowDialog(this) != true) return;
        try { OpenPdfFile(picker.FileName); }
        catch (Exception ex) { CompilationStatus.Text = "No se pudo abrir el PDF: " + ex.Message; }
    }

    private void OpenPdfFile(string fileName, CompilationSnapshot? snapshot = null)
    {
        PdfViewerStatusPanel.Visibility = Visibility.Collapsed;
        if (!viewerReady) throw new InvalidOperationException("El visor todavía no está listo.");
        var fullPath = Path.GetFullPath(fileName);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("No se encontró el PDF.", fullPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".pdf", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Selecciona un archivo PDF.");
        var bytes = File.ReadAllBytes(fullPath);
        var preservePosition = snapshot is not null && displayedCompilation?.ProjectDirectory == snapshot.ProjectDirectory;
        var initialCompileView = snapshot is not null && !firstCompiledPdfOpened;

        displayedDocumentId = Guid.NewGuid().ToString("N");
        displayedCompilation = snapshot;
        PdfFileName.Text = Path.GetFileName(fullPath);
        PdfFileName.ToolTip = fullPath;
        using var sharedBuffer = PdfWebView.CoreWebView2.Environment.CreateSharedBuffer((ulong)bytes.Length);
        using (var stream = sharedBuffer.OpenStream()) stream.Write(bytes);
        PdfWebView.CoreWebView2.PostSharedBufferToScript(sharedBuffer, CoreWebView2SharedBufferAccess.ReadOnly, JsonSerializer.Serialize(new
        {
            type = "open", documentId = displayedDocumentId,

            name = Path.GetFileName(fullPath), syncEnabled = snapshot is not null, preservePosition, initialCompileView
        }));
        if (snapshot is not null) firstCompiledPdfOpened = true;
    }
}
