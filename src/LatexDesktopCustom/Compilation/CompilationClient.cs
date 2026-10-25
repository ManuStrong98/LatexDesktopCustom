using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
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

    public async Task<CompilationResult> CompileAsync(string mainFile, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(mainFile);
        var directory = Path.GetDirectoryName(fullPath)!;
        var resources = new List<object>();
        long sourceBytes = 0;
        var enumeration = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden, IgnoreInaccessible = false };
        foreach (var file in Directory.EnumerateFiles(directory, "*", enumeration))
        {
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            if (relative.Split('/').Any(part => part is ".git" or "bin" or "obj" or "node_modules" or "artifacts")) continue;
            if (!TextExtensions.Contains(Path.GetExtension(file))) continue;
            sourceBytes += new FileInfo(file).Length;
            if (sourceBytes > 5 * 1024 * 1024 || resources.Count >= 2000)
                throw new InvalidOperationException("El proyecto supera el límite de esta integración inicial (5 MB de texto o 2000 archivos).");
            resources.Add(new { path = relative, content = await File.ReadAllTextAsync(file, Encoding.UTF8, cancellationToken) });
        }
        var projectId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant())))[..24].ToLowerInvariant();
        using var response = await http.PostAsJsonAsync($"/api/projects/{projectId}/compile", new
        {
            rootResourcePath = Path.GetFileName(fullPath), compiler = "pdflatex", resources
        }, cancellationToken);
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
