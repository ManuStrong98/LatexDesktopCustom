using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using LatexDesktopCustom.Compilation;

var project = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "binary-integration", Guid.NewGuid().ToString("N"));
var images = Path.Combine(project, "imágenes con espacios");
Directory.CreateDirectory(images);
var main = Path.Combine(project, "main.tex");
using var client = new CompilationClient();

async Task<CompilationResult> Compile()
{
    var result = await client.CompileAsync(main, CancellationToken.None);
    if (result.PdfPath is null) throw new Exception($"{result.Error}\nLog: {result.LogPath}");
    var bytes = await File.ReadAllBytesAsync(result.PdfPath);
    if (Encoding.ASCII.GetString(bytes[..5]) != "%PDF-") throw new Exception("No se recibió un PDF válido.");
    return result;
}

// Regression: plain-text requests still work, and supply a PDF inclusion fixture.
await File.WriteAllTextAsync(main, @"\documentclass{article}\begin{document}Documento incluido.\end{document}");
var plain = await Compile();
File.Copy(plain.PdfPath!, Path.Combine(images, "incluido.pdf"));
File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "first.png"), Path.Combine(images, "español.png"));
File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.jpg"), Path.Combine(images, "foto.jpg"));
await File.WriteAllTextAsync(main, """
    \documentclass{article}
    \usepackage{graphicx}
    \begin{document}
    \includegraphics[width=3cm]{imágenes con espacios/español.png}
    \includegraphics[width=3cm]{imágenes con espacios/foto.jpg}
    \clearpage
    \includegraphics[width=10cm]{imágenes con espacios/incluido.pdf}
    \end{document}
    """);
var first = await Compile();
var firstImages = await EmbeddedImages(first.PdfPath!);
if (!Regex.IsMatch(firstImages, @"\s64\s+32\s") || !Regex.IsMatch(firstImages, @"\s80\s+40\s"))
    throw new Exception("El PDF no contiene los PNG/JPEG esperados.\n" + firstImages);
Console.WriteLine("PNG, JPEG y PDF incluidos; rutas con espacios y acentos: OK");

File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "updated.png"), Path.Combine(images, "español.png"), true);
var updated = await Compile();
var updatedImages = await EmbeddedImages(updated.PdfPath!);
if (!Regex.IsMatch(updatedImages, @"\s128\s+64\s")) throw new Exception("Se reutilizó una imagen antigua.\n" + updatedImages);
Console.WriteLine("Cambio de imagen con el mismo proyecto y nombre: OK");

using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5017") };
foreach (var bad in new[] { (Path: "image.png", Base64: "invalid!"), (Path: "../image.png", Base64: "AQID") })
{
    using var response = await http.PostAsJsonAsync("/api/projects/abcdefabcdefabcdefabcdef/compile", new
    {
        rootResourcePath = "main.tex",
        resources = new object[] { new { path = "main.tex", content = "text" }, new { path = bad.Path, base64 = bad.Base64 } }
    });
    if (response.StatusCode != HttpStatusCode.BadRequest) throw new Exception("No se rechazó un recurso inválido.");
}
Console.WriteLine("Base64 inválido y rutas fuera del proyecto rechazados: OK");
Console.WriteLine("Último PDF: " + updated.PdfPath);

static async Task<string> EmbeddedImages(string pdf)
{
    var start = new ProcessStartInfo("docker")
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
    };
    foreach (var argument in new[] { "exec", "-i", "sharelatex", "pdfimages", "-list", "-" }) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    await process.StandardInput.BaseStream.WriteAsync(await File.ReadAllBytesAsync(pdf));
    process.StandardInput.Close();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    try { await process.WaitForExitAsync(timeout.Token); }
    finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    if (process.ExitCode != 0) throw new Exception(await error);
    return await output;
}
