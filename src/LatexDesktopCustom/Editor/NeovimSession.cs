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

    public Task OpenFileAsync(string mainFile, CancellationToken cancellationToken)
        => OpenAsync(mainFile, Path.GetDirectoryName(Path.GetFullPath(mainFile))!, 0, 1, cancellationToken);

    public Task OpenLocationAsync(string file, string projectDirectory, int line, int column, CancellationToken cancellationToken)
        => OpenAsync(file, projectDirectory, Math.Max(1, line), Math.Max(1, column), cancellationToken);

    private async Task OpenAsync(string mainFile, string projectDirectory, int line, int column, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        cancellationToken = timeout.Token;
        var fullPath = Path.GetFullPath(mainFile);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("No se encontró el archivo LaTeX.", fullPath);
        var arguments = JsonSerializer.Serialize(new object[] { Path.GetFullPath(projectDirectory), fullPath, line, column });
        // JSON carries paths as data; fnameescape handles spaces and Ex metacharacters.
        const string lua = "(function() local p = vim.fn.json_decode(_A); " +
            "local b = vim.fn.bufnr(p[2]); if vim.bo.modified or (b > 0 and vim.bo[b].modified) then return \"unsaved\" end; " +
            "vim.cmd(\"cd \" .. vim.fn.fnameescape(p[1])); " +
            "vim.cmd(\"edit \" .. vim.fn.fnameescape(p[2])); " +
            "if p[3] > 0 then local row = math.min(p[3], vim.api.nvim_buf_line_count(0)); " +
            "local text = vim.api.nvim_buf_get_lines(0,row-1,row,false)[1] or \"\"; " +
            "vim.api.nvim_win_set_cursor(0,{row,math.min(p[4]-1,#text)}); vim.cmd(\"normal! zvzz\"); end; " +
            "return \"opened\" end)()";
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
