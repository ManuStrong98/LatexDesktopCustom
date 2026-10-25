using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LatexDesktopCustom.Editor;

using var session = new NeovimSession();
var file = Path.Combine(Path.GetDirectoryName(session.SaveNotificationPath)!, "prueba á.tex");
File.WriteAllText(file, "Inicial");
async Task Run(string action)
{
    // Exercise the same legacy Windows PowerShell argument conversion as Terminal.
    static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
    var command = session.StartupCommand(null).Replace("& nvim ", "& nvim --headless --clean ") +
        " " + Quote(file) + " -c " + Quote(action) + " -c 'qa!'";
    var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
    var start = new ProcessStartInfo(shell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) }) start.ArgumentList.Add(arg);
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    try { await process.WaitForExitAsync(timeout.Token); }
    finally { if (!process.HasExited) process.Kill(true); }
    await output;
    var errors = await error;
    if (process.ExitCode != 0) throw new Exception(errors);
    using var json = JsonDocument.Parse(File.ReadAllText(session.SaveNotificationPath));
    if (!string.Equals(Path.GetFullPath(json.RootElement.GetProperty("file").GetString()!), file, StringComparison.OrdinalIgnoreCase)) throw new Exception("Wrong saved file");
}
try
{
    await Run("write");
    Console.WriteLine(":w → notificación de guardado: OK");
    File.Delete(session.SaveNotificationPath);
    await Run("lua vim.api.nvim_exec_autocmds('VimEnter', {}); vim.api.nvim_buf_set_lines(0,0,-1,false,{'Guardado con Ctrl+S'}); vim.api.nvim_feedkeys(vim.api.nvim_replace_termcodes('<C-s>',true,false,true),'xt',false)");
    if (!File.ReadAllText(file).Contains("Guardado con Ctrl+S")) throw new Exception("Ctrl+S did not save");
    Console.WriteLine("Ctrl+S → archivo guardado y notificación: OK");
}
finally { File.Delete(file); }
