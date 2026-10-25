using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LatexDesktopCustom.Compilation;

internal sealed class CompilationClient : IDisposable
{
    private readonly HttpClient http = new() { BaseAddress = new Uri("http://127.0.0.1:5017"), Timeout = TimeSpan.FromSeconds(110) };
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".tex", ".bib", ".sty", ".cls", ".bst", ".bbx", ".cbx", ".lbx", ".def", ".cfg", ".clo", ".fd", ".csv", ".txt" };
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".pdf", ".eps", ".ps", ".svg", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".otf", ".ttf", ".woff", ".woff2" };

    public async Task<CompilationResult> CompileAsync(string mainFile, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(mainFile);
        var directory = Path.GetDirectoryName(fullPath)!;
        var resources = new List<object>();
        long sourceBytes = 0;
        long textBytes = 0;
        var enumeration = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden, IgnoreInaccessible = false };
        foreach (var file in Directory.EnumerateFiles(directory, "*", enumeration))
        {
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            if (relative.Split('/').Any(part => part is ".git" or "bin" or "obj" or "node_modules" or "artifacts")) continue;
            var text = TextExtensions.Contains(Path.GetExtension(file));
            if (!text && !BinaryExtensions.Contains(Path.GetExtension(file))) continue;
            var size = new FileInfo(file).Length;
            sourceBytes += size;
            if (text) textBytes += size;
            if (sourceBytes > 20 * 1024 * 1024 || textBytes > 5 * 1024 * 1024 || resources.Count >= 2000)
                throw new InvalidOperationException("El proyecto supera el límite de 20 MB de recursos, 5 MB de texto o 2000 archivos.");
            if (text)
                resources.Add(new { path = relative, content = await File.ReadAllTextAsync(file, Encoding.UTF8, cancellationToken) });
            else
                resources.Add(new { path = relative, base64 = Convert.ToBase64String(await File.ReadAllBytesAsync(file, cancellationToken)) });
        }
        var projectId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant())))[..24].ToLowerInvariant();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            rootResourcePath = Path.GetFileName(fullPath), compiler = "pdflatex", resources
        });
        if (payload.Length > 32 * 1024 * 1024)
            throw new InvalidOperationException("La petición codificada supera 32 MB. Reduce el tamaño de las imágenes.");
        using var requestContent = new ByteArrayContent(payload);
        requestContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await http.PostAsync($"/api/projects/{projectId}/compile", requestContent, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var details = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"El adaptador devolvió {(int)response.StatusCode}: {details}");
        }
        var result = await response.Content.ReadFromJsonAsync<CompileResponse>(cancellationToken)
            ?? throw new InvalidOperationException("El adaptador devolvió una respuesta vacía.");
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LatexDesktopCustom", "Builds", projectId, result.BuildId ?? Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        string? logPath = null;
        if (result.LogUrl is not null)
        {
            logPath = Path.Combine(cache, "output.log");
            await File.WriteAllBytesAsync(logPath, await http.GetByteArrayAsync(result.LogUrl, cancellationToken), cancellationToken);
        }
        if (result.Status != "success" || result.PdfUrl is null)
        {
            var error = string.IsNullOrWhiteSpace(result.Error) ? $"La compilación terminó con estado: {result.Status}" : result.Error;
            if (logPath is not null)
            {
                var log = await File.ReadAllTextAsync(logPath, cancellationToken);
                var match = Regex.Match(log, @"^!\s+(.+)$", RegexOptions.Multiline);
                if (match.Success) error = match.Groups[1].Value.Trim();
            }
            return new(null, logPath, error);
        }
        var pdfPath = Path.Combine(cache, "output.pdf");
        await File.WriteAllBytesAsync(pdfPath, await http.GetByteArrayAsync(result.PdfUrl, cancellationToken), cancellationToken);
        return new(pdfPath, logPath, null);
    }

    public void Dispose() => http.Dispose();
    private record CompileResponse(string Status, string? BuildId, string? PdfUrl, string? LogUrl, string? Error);
}

internal record CompilationResult(string? PdfPath, string? LogPath, string? Error);
