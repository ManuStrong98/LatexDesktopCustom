using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LatexDesktopCustom.Compilation;
using LatexDesktopCustom.Editor;

var root = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "synctex-integration", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "capítulos"));
Directory.CreateDirectory(Path.Combine(root, "imagenes"));
File.Copy("tests/BinaryCompilation.Integration/Fixtures/first.png", Path.Combine(root, "imagenes", "figura.png"));
var main = Path.Combine(root, "main.tex");
var chapter = Path.Combine(root, "capítulos", "texto con espacios.tex");
const string fragment = "% Archivo incluido\nTexto inicial.\\par\nTexto que queremos localizar.\\par\n\\includegraphics[width=3cm]{imagenes/figura.png}\n";
await File.WriteAllTextAsync(chapter, fragment);
await File.WriteAllTextAsync(main, "\\documentclass{article}\n\\usepackage{graphicx}\n\\begin{document}\n\\input{capítulos/texto con espacios}\n\\end{document}\n");
using var client = new CompilationClient();
async Task<CompilationSnapshot> Compile()
{
    var result = await client.CompileAsync(main, CancellationToken.None);
    return result.Snapshot ?? throw new Exception($"Falló la compilación: {result.Error}. Log: {result.LogPath}");
}
var first = await Compile();
await first.EnsureSourcesUnchangedAsync(CancellationToken.None);
var textPoint = await PointFor(first, "capítulos/texto con espacios.tex", 3);
var imagePoint = await PointFor(first, "capítulos/texto con espacios.tex", 4);
var text = await client.SyncFromPdfAsync(first, textPoint.Page, textPoint.H, textPoint.V, CancellationToken.None);
var image = await client.SyncFromPdfAsync(first, imagePoint.Page, imagePoint.H, imagePoint.V, CancellationToken.None);
if (text?.File != "capítulos/texto con espacios.tex" || text.Line != 3) throw new Exception("Texto: posición incorrecta: " + JsonSerializer.Serialize(text));
if (image?.File != "capítulos/texto con espacios.tex" || image.Line != 4) throw new Exception("Figura: posición incorrecta: " + JsonSerializer.Serialize(image));
Console.WriteLine("SyncTeX: texto y figura en archivo incluido, con espacios y acentos: OK");

await File.WriteAllTextAsync(chapter, new string('\n', 10) + fragment);
var changedDetected = false;
try { await first.EnsureSourcesUnchangedAsync(CancellationToken.None); }
catch (InvalidOperationException) { changedDetected = true; }
if (!changedDetected) throw new Exception("No detectó código modificado después del PDF.");
var second = await Compile();
var oldText = await client.SyncFromPdfAsync(first, textPoint.Page, textPoint.H, textPoint.V, CancellationToken.None);
var newText = await client.SyncFromPdfAsync(second, textPoint.Page, textPoint.H, textPoint.V, CancellationToken.None);
if (oldText?.Line != 3 || newText?.Line != 13) throw new Exception("SyncTeX confundió compilaciones anteriores y nuevas.");
Console.WriteLine("Build anterior/nuevo independientes y detección de código desfasado: OK");
var rejected = false;
try { first.SourcePath("../outside.tex"); } catch (InvalidOperationException) { rejected = true; }
if (!rejected) throw new Exception("Se permitió una ruta fuera del proyecto.");

var session = new NeovimSession();
var serverStart = new ProcessStartInfo("nvim") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
foreach (var arg in new[] { "--headless", "--clean", "--listen", session.Address }) serverStart.ArgumentList.Add(arg);
using var server = Process.Start(serverStart)!;
_ = server.StandardOutput.ReadToEndAsync(); _ = server.StandardError.ReadToEndAsync();
try
{
    await Task.Delay(400);
    await session.OpenLocationAsync(second.SourcePath(newText!.File), root, newText.Line, 5, CancellationToken.None);
    var cursor = JsonSerializer.Deserialize<int[]>(await Expr(session, "json_encode([line('.'),col('.')])"))!;
    if (cursor[0] != 13 || cursor[1] != 5 || await Expr(session, "getcwd()") != root) throw new Exception("Neovim no posicionó el cursor o cambió la raíz del proyecto.");
    await Expr(session, "luaeval('(function() vim.api.nvim_buf_set_lines(0,0,0,false,{\"Sin guardar\"}); return 1 end)()')");
    var protectedChanges = false;
    try { await session.OpenLocationAsync(main, root, 1, 1, CancellationToken.None); }
    catch (InvalidOperationException ex) when (ex.Message.Contains("Guarda")) { protectedChanges = true; }
    if (!protectedChanges || await Expr(session, "&modified") != "1") throw new Exception("Se perdieron cambios sin guardar.");
    Console.WriteLine("Neovim: archivo, línea, columna, raíz del proyecto y protección de cambios: OK");
}
finally { if (!server.HasExited) server.Kill(entireProcessTree: true); }

static async Task<(int Page, double H, double V)> PointFor(CompilationSnapshot snapshot, string file, int line)
{
    var source = $"/var/lib/overleaf/data/compiles/{snapshot.ProjectId}/{file}";
    var output = $"/var/lib/overleaf/data/output/{snapshot.ProjectId}/generated-files/{snapshot.BuildId}/output.pdf";
    var stdout = await Run("docker", ["exec", "sharelatex", "synctex", "view", "-i", $"{line}:0:{source}", "-o", output]);
    double Number(string label) => double.Parse(Regex.Match(stdout, $@"(?m)^{label}:\s*([^\r\n]+)").Groups[1].Value, CultureInfo.InvariantCulture);
    return ((int)Number("Page"), Number("h"), Number("v"));
}
static Task<string> Expr(NeovimSession session, string expression)
    => Run("nvim", ["--headless", "--clean", "--server", session.Address, "--remote-expr", expression]);
static async Task<string> Run(string executable, string[] arguments)
{
    var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments) info.ArgumentList.Add(argument);
    using var process = Process.Start(info)!;
    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    try { await process.WaitForExitAsync(timeout.Token); }
    finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    if (process.ExitCode != 0) throw new Exception(await error);
    return (await output).Trim();
}
