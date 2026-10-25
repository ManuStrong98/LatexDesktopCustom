using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using LatexDesktopCustom.Compilation;
using LatexDesktopCustom.Editor;
using LatexDesktopCustom.PdfViewer;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

internal static class Program
{
    [STAThread]
    public static void Main(string[] arguments)
    {
        var physicalMouse = arguments.Contains("--mouse");
        var app = new Application();
        var web = new PdfWebViewControl();
        var window = new Window { Title = "Prueba de doble clic PDF", Content = web, Width = 900, Height = 700, Left = physicalMouse ? 100 : -3000, Top = 40, ShowInTaskbar = physicalMouse, ShowActivated = physicalMouse };
        window.Loaded += async (_, _) =>
        {
            Process? nvim = null;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(physicalMouse ? 300 : 90));
                var token = timeout.Token;
                using var client = new CompilationClient();
                var root = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "pdf-viewer-integration", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                var main = Path.Combine(root, "main.tex");
                await File.WriteAllTextAsync(main, "\\documentclass{article}\n\\begin{document}\nPrimera linea.\\par\nTexto para saltar.\\par\n\\end{document}\n", token);
                var compiled = await client.CompileAsync(main, token);
                var snapshot = compiled.Snapshot ?? throw new Exception(compiled.Error);
                var session = new NeovimSession();
                var start = new ProcessStartInfo("nvim") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in new[] { "--headless", "--clean", "--listen", session.Address }) start.ArgumentList.Add(arg);
                nvim = Process.Start(start)!;
                _ = nvim.StandardOutput.ReadToEndAsync(); _ = nvim.StandardError.ReadToEndAsync();

                var ready = new TaskCompletionSource<bool>();
                var loaded = new TaskCompletionSource<bool>();
                TaskCompletionSource<(int Page, double H, double V)>? clicked = null;
                const string documentId = "integration-test";
                await web.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(root, "WebView2")));
                web.CoreWebView2.SetVirtualHostNameToFolderMapping("latexdesktop.local", Path.Combine(AppContext.BaseDirectory, "PdfViewer"), CoreWebView2HostResourceAccessKind.Deny);
                web.CoreWebView2.WebMessageReceived += (_, e) =>
                {
                    using var json = JsonDocument.Parse(e.WebMessageAsJson);
                    var message = json.RootElement;
                    switch (message.GetProperty("type").GetString())
                    {
                        case "ready": ready.TrySetResult(true); break;
                        case "pdfLoaded": loaded.TrySetResult(true); break;
                        case "viewerError":
                            var error = new Exception(message.GetProperty("error").GetString());
                            ready.TrySetException(error); loaded.TrySetException(error); clicked?.TrySetException(error); break;
                        case "gotoSource": clicked?.TrySetResult((message.GetProperty("page").GetInt32(), message.GetProperty("h").GetDouble(), message.GetProperty("v").GetDouble())); break;
                    }
                };
                web.CoreWebView2.Navigate("https://latexdesktop.local/index.html");
                await ready.Task.WaitAsync(token);
                var bytes = await File.ReadAllBytesAsync(compiled.PdfPath!, token);
                using (var buffer = web.CoreWebView2.Environment.CreateSharedBuffer((ulong)bytes.Length))
                {
                    using (var stream = buffer.OpenStream()) stream.Write(bytes);
                    web.CoreWebView2.PostSharedBufferToScript(buffer, CoreWebView2SharedBufferAccess.ReadOnly,
                        JsonSerializer.Serialize(new { type = "open", documentId, name = "test.pdf", syncEnabled = true, preservePosition = false }));
                }
                await loaded.Task.WaitAsync(token);
                Console.WriteLine("PDF.js: PDF recibido por memoria compartida y documento cargado: OK");
                foreach (var scale in physicalMouse ? new[] { "page-width" } : new[] { "page-width", "2" })
                {
                    await web.ExecuteScriptAsync($"document.getElementById('zoom').value='{scale}';document.getElementById('zoom').dispatchEvent(new Event('change'));");
                    clicked = new();
                    var sent = physicalMouse;
                    if (physicalMouse) Console.WriteLine("Esperando doble clic físico sobre 'Texto para saltar'.");
                    for (var attempt = 0; attempt < 100 && !sent; attempt++)
                    {
                        var response = await web.ExecuteScriptAsync("""
                            (() => {
                              const span = [...document.querySelectorAll('.textLayer span')].find(s => s.textContent.includes('Texto para saltar'));
                              if (!span) return false;
                              const r = span.getBoundingClientRect();
                              if (!r.width || !r.height) return false;
                              span.dispatchEvent(new MouseEvent('dblclick',{bubbles:true,clientX:r.left+r.width/2,clientY:r.top+r.height/2}));
                              return true;
                            })()
                            """);
                        sent = response == "true";
                        if (!sent) await Task.Delay(100, token);
                    }
                    if (!sent) throw new Exception("El PDF no creó la capa de texto.");
                    var point = await clicked.Task.WaitAsync(token);
                    var position = await client.SyncFromPdfAsync(snapshot, point.Page, point.H, point.V, token);
                    if (position?.File != "main.tex" || position.Line != 4) throw new Exception("Doble clic: posición incorrecta " + JsonSerializer.Serialize(position));
                    await snapshot.EnsureSourcesUnchangedAsync(token);
                    await session.OpenLocationAsync(snapshot.SourcePath(position.File), root, position.Line, position.Column, token);
                    Console.WriteLine($"Doble clic PDF.js, zoom {scale}, SyncTeX y navegación a Neovim: OK (línea {position.Line})");
                }
                if (!physicalMouse)
                {
                    await Task.Delay(350, token);
                    clicked = new();
                    var coordinates = JsonSerializer.Deserialize<double[]>(await web.ExecuteScriptAsync("""
                        (() => {
                          const span = [...document.querySelectorAll('.textLayer span')].find(s => s.textContent.includes('Texto para saltar'));
                          const r = span.getBoundingClientRect();
                          return [(r.left+r.width/2)/innerWidth,(r.top+r.height/2)/innerHeight];
                        })()
                        """))!;
                    await web.ForwardDoubleClickAsync(coordinates[0], coordinates[1]);
                    var point = await clicked.Task.WaitAsync(token);
                    var source = await client.SyncFromPdfAsync(snapshot, point.Page, point.H, point.V, token);
                    if (source?.File != "main.tex" || source.Line != 4) throw new Exception("Falló el puente nativo WPF → PDF.js.");
                    await session.OpenLocationAsync(main, root, source.Line, source.Column, token);
                    Console.WriteLine("Puente del doble clic nativo WPF → PDF.js → SyncTeX → Neovim: OK");
                }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally
            {
                if (nvim is not null) { if (!nvim.HasExited) nvim.Kill(entireProcessTree: true); nvim.Dispose(); }
                web.Dispose(); window.Close();
            }
        };
        app.Run(window);
    }
}
