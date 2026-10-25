using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LatexDesktopCustom.Editor;

internal sealed class NeovimSession
{
    public string Address { get; } = @"\\.\pipe\LatexDesktopNvim-" + Guid.NewGuid().ToString("N");

    public string StartupCommand(string? mainFile)
    {
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var command = $"& nvim --listen {Quote(Address)}";
        if (mainFile is not null) command += $" -- {Quote(Path.GetFullPath(mainFile))}";
        return command;
    }

    public async Task OpenFileAsync(string mainFile, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        cancellationToken = timeout.Token;
        var fullPath = Path.GetFullPath(mainFile);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("No se encontró el archivo LaTeX.", fullPath);
        var arguments = JsonSerializer.Serialize(new[] { Path.GetDirectoryName(fullPath)!, fullPath });
        // JSON carries paths as data; fnameescape handles spaces and Ex metacharacters.
        const string lua = "(function() local p = vim.fn.json_decode(_A); " +
            "if vim.bo.modified then return \"unsaved\" end; " +
            "vim.cmd(\"cd \" .. vim.fn.fnameescape(p[1])); " +
            "vim.cmd(\"edit \" .. vim.fn.fnameescape(p[2])); return \"opened\" end)()";
        var expression = "luaeval('" + lua.Replace("'", "''") + "', '" + arguments.Replace("'", "''") + "')";
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo("nvim")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "--headless", "--clean", "--server", Address, "--remote-expr", expression }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo conectar con Neovim.");
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
                var result = (await stdout).Trim();
                var error = (await stderr).Trim();
                if (process.ExitCode == 0)
                {
                    if (result == "opened") return;
                    if (result == "unsaved") throw new InvalidOperationException("Guarda los cambios del archivo actual en Neovim antes de cambiar de archivo.");
                    throw new InvalidOperationException($"Neovim no pudo abrir el archivo: {result} {error}");
                }
                if (DateTime.UtcNow >= deadline || !(error.Contains("connect", StringComparison.OrdinalIgnoreCase) || error.Contains("E247", StringComparison.Ordinal)))
                    throw new InvalidOperationException($"No se pudo contactar con Neovim. Comprueba que siga abierto en la terminal. {error}");
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            await Task.Delay(200, cancellationToken);
        }
    }
}
