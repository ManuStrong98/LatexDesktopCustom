using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LatexDesktopCustom.Editor;

internal sealed class NeovimSession : IDisposable
{
    private bool startupExplorerOpened;
    private readonly string bridgeDirectory = Path.Combine(Path.GetTempPath(), "LatexDesktopNvim-" + Guid.NewGuid().ToString("N"));
    public string SaveNotificationPath => Path.Combine(bridgeDirectory, "saved.json");
    internal string SaveBridgeScript => Path.Combine(bridgeDirectory, "bridge.lua");
    private static string LuaString(string value)
    {
        var equals = "=";
        while (value.Contains("]" + equals + "]", StringComparison.Ordinal)) equals += "=";
        return "[" + equals + "[" + value + "]" + equals + "]";
    }

    public NeovimSession()
    {
        Directory.CreateDirectory(bridgeDirectory);
        var notification = LuaString(SaveNotificationPath.Replace('\\', '/'));
        File.WriteAllText(SaveBridgeScript, "local destination = " + notification + "\n" + """
            local group = vim.api.nvim_create_augroup('LatexDesktopSave', {clear=true})
            vim.api.nvim_create_autocmd('BufWritePost', {group=group, callback=function(args)
                local data = vim.json.encode({file=vim.api.nvim_buf_get_name(args.buf), nonce=tostring(vim.uv.hrtime())})
                vim.fn.writefile({data}, destination)
            end})
            vim.api.nvim_create_autocmd('VimEnter', {group=group, once=true, callback=function()
                vim.keymap.set({'n','i','v'}, '<C-s>', function() vim.cmd('write') end,
                    {silent=true, desc='Guardar y compilar en LatexDesktop'})
            end})
            """);
    }

    public void Dispose()
    {
        try { File.Delete(SaveBridgeScript); File.Delete(SaveNotificationPath); Directory.Delete(bridgeDirectory); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public async Task<(string File, int Line, int Column)> GetPositionAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var start = new ProcessStartInfo("nvim") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "--headless", "--clean", "--server", Address, "--remote-expr", "json_encode([expand('%:p'),line('.'),col('.')])" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await error;
            using var json = JsonDocument.Parse(await output);
            return (json.RootElement[0].GetString()!, json.RootElement[1].GetInt32(), json.RootElement[2].GetInt32());
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
    public string Address { get; } = @"\\.\pipe\LatexDesktopNvim-" + Guid.NewGuid().ToString("N");

    public string StartupCommand(string? mainFile)
    {
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var command = $"& nvim --listen {Quote(Address)}";
        // Windows PowerShell 5.1 removes embedded double quotes when invoking
        // native executables. Lua long strings survive that argument conversion.
        var lua = "lua dofile(" + LuaString(SaveBridgeScript.Replace('\\', '/')) + ")";
        command += " --cmd " + Quote(lua);
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
        var arguments = JsonSerializer.Serialize(new object[] { Path.GetFullPath(projectDirectory), fullPath, line, column, !startupExplorerOpened });
        // JSON carries paths as data; fnameescape handles spaces and Ex metacharacters.
        const string lua = "(function() local p = vim.fn.json_decode(_A); " +
            "for _,b in ipairs(vim.api.nvim_list_bufs()) do if vim.bo[b].modified then return \"unsaved\" end end; " +
            "vim.cmd(\"cd \" .. vim.fn.fnameescape(p[1])); " +
            "vim.cmd(\"edit \" .. vim.fn.fnameescape(p[2])); " +
            "if p[3] > 0 then local row = math.min(p[3], vim.api.nvim_buf_line_count(0)); " +
            "local text = vim.api.nvim_buf_get_lines(0,row-1,row,false)[1] or \"\"; " +
            "vim.api.nvim_win_set_cursor(0,{row,math.min(p[4]-1,#text)}); vim.cmd(\"normal! zvzz\"); end; " +
            "if p[5] then vim.schedule(function() local ok,s = pcall(require, \"snacks\"); " +
            "if ok then s.explorer({cwd=p[1]}) end end) end; " +
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
                    if (result == "opened") { startupExplorerOpened = true; return; }
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
