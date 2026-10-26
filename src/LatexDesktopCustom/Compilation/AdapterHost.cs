using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace LatexDesktopCustom.Compilation;

internal static class AdapterHost
{
    private static Process? hostedProcess;
    internal static void StopOwned()
    {
        var process = hostedProcess;
        hostedProcess = null;
        if (process is null) return;
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally { process.Dispose(); }
    }
    internal static async Task EnsureStartedAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        async Task<bool> Ready()
        {
            try
            {
                using var response = await http.GetAsync("http://127.0.0.1:5017/health");
                if (!response.IsSuccessStatusCode) return false;
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return json.RootElement.TryGetProperty("status", out var status) && status.GetString() == "ready";
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException) { return false; }
        }
        if (await Ready()) return;
        var executable = Path.Combine(AppContext.BaseDirectory, "Adapter", "DesktopAdapter.exe");
        ProcessStartInfo start;
        if (File.Exists(executable)) start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)! };
        else
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            string? project = null;
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "backend", "desktop-adapter", "DesktopAdapter.csproj");
                if (File.Exists(candidate)) { project = candidate; break; }
                directory = directory.Parent;
            }
            if (project is null) throw new FileNotFoundException("No se encontró Adapter/DesktopAdapter.exe. Publica y distribuye la carpeta completa de la aplicación.");
            start = new ProcessStartInfo("dotnet");
            foreach (var arg in new[] { "run", "--project", project, "--no-launch-profile" }) start.ArgumentList.Add(arg);
            start.WorkingDirectory = Path.GetDirectoryName(project)!;
        }
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = start.RedirectStandardError = true;
        var logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LatexDesktopCustom", "Logs");
        Directory.CreateDirectory(logs);
        var logPath = Path.Combine(logs, "adapter-" + Guid.NewGuid().ToString("N") + ".log");
        var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar el adaptador.");
        hostedProcess = process;
        var logLock = new object();
        void Log(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            try { lock (logLock) File.AppendAllText(logPath, e.Data + Environment.NewLine); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        process.OutputDataReceived += Log;
        process.ErrorDataReceived += Log;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (await Ready()) return;
            if (process.HasExited) throw new InvalidOperationException($"El adaptador se cerró. Consulta {logPath}");
            await Task.Delay(300);
        }
        throw new TimeoutException($"El adaptador no respondió a tiempo. Consulta {logPath}");
    }
}
