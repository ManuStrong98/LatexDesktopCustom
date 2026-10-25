using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LatexDesktopCustom.Compilation;

internal record CompilationSnapshot(string ProjectId, string BuildId, string ProjectDirectory,
    IReadOnlyDictionary<string, string> SourceHashes)
{
    public string SourcePath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Contains(':')
            || relative.Split('/').Any(part => part is "" or "." or "..") || !SourceHashes.ContainsKey(relative))
            throw new InvalidOperationException("SyncTeX no señaló un archivo de código del proyecto.");
        var path = Path.GetFullPath(Path.Combine(ProjectDirectory, relative));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ProjectDirectory)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La ruta de SyncTeX está fuera del proyecto.");
        return path;
    }

    public async Task EnsureSourcesUnchangedAsync(CancellationToken cancellationToken)
    {
        foreach (var source in SourceHashes)
        {
            var path = SourcePath(source.Key);
            if (!File.Exists(path)) throw new InvalidOperationException("Cambió el proyecto desde esta compilación. Recompila antes de navegar al código.");
            var content = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
            if (hash != source.Value)
                throw new InvalidOperationException("El código cambió desde que se generó este PDF. Recompila antes de navegar a una línea.");
        }
    }
}
