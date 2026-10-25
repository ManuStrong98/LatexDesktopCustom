using System.Diagnostics;
using System.Text;
using System.Text.Json;

/// <summary>Serves one compile's immutable binary snapshot inside Overleaf's network namespace.</summary>
internal sealed class BinaryResourceServer : IAsyncDisposable
{
    private readonly Process process;
    private readonly int port;
    private readonly string token;

    private BinaryResourceServer(Process process, int port, string token)
    {
        this.process = process;
        this.port = port;
        this.token = token;
    }

    public string UrlFor(string path) => $"http://127.0.0.1:{port}/{token}/" +
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    public static async Task<BinaryResourceServer> StartAsync(string container, IEnumerable<ProjectResource> files, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("docker")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "exec", "-i", container, "node", "-e", ServerScript }) start.ArgumentList.Add(argument);
        var process = Process.Start(start) ?? throw new IOException("No se pudo preparar el servidor de imágenes.");
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            var snapshot = JsonSerializer.Serialize(files.Select(file => new { path = file.Path, base64 = file.Base64 }));
            await process.StandardInput.WriteLineAsync(snapshot.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null) throw new IOException("No se pudo iniciar el servidor de imágenes: " + await errors);
            using var metadata = JsonDocument.Parse(line);
            var port = metadata.RootElement.GetProperty("port").GetInt32();
            var token = metadata.RootElement.GetProperty("token").GetString()!;
            if (port is < 1 or > 65535 || token.Length != 48 || !token.All(Uri.IsHexDigit))
                throw new IOException("Respuesta inválida del servidor de imágenes.");
            return new BinaryResourceServer(process, port, token);
        }
        catch
        {
            process.StandardInput.Close();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // EOF shuts down Node, releases the snapshot and closes its port.
            process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (IOException) { }
        finally { process.Dispose(); }
    }

    private const string ServerScript = """
        const http = require('node:http');
        const crypto = require('node:crypto');
        const input = require('node:readline').createInterface({input: process.stdin, crlfDelay: Infinity});
        let server;
        function shutdown() {
            if (server) server.close();
            input.close();
            process.stdin.destroy();
        }
        // A safety deadline also releases resources if the Docker client disconnects unexpectedly.
        setTimeout(shutdown, 120000).unref();
        process.stdin.on('end', shutdown);
        input.once('line', line => {
            try {
                const files = new Map(JSON.parse(line).map(f => [f.path, Buffer.from(f.base64, 'base64')]));
                const token = crypto.randomBytes(24).toString('hex');
                const prefix = '/' + token + '/';
                server = http.createServer((req, res) => {
                    try {
                        const pathname = new URL(req.url, 'http://localhost').pathname;
                        if (!['GET', 'HEAD'].includes(req.method) || !pathname.startsWith(prefix)) {
                            res.writeHead(404); res.end(); return;
                        }
                        const buffer = files.get(decodeURIComponent(pathname.slice(prefix.length)));
                        if (!buffer) { res.writeHead(404); res.end(); return; }
                        res.writeHead(200, {'Content-Type': 'application/octet-stream',
                            'Content-Length': buffer.length, 'Cache-Control': 'no-store'});
                        res.end(req.method === 'HEAD' ? undefined : buffer);
                    } catch { res.writeHead(400); res.end(); }
                });
                server.on('error', error => { console.error(error.message); process.exit(1); });
                server.listen(0, '127.0.0.1', () => {
                    console.log(JSON.stringify({port: server.address().port, token}));
                });
            } catch (error) { console.error(error.message); process.exit(1); }
        });
        """;
}
