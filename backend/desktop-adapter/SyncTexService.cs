using System.Diagnostics;
using System.Text;
using System.Text.Json;

internal static class SyncTexService
{
    public static async Task<SyncTexResponse> FromPdfAsync(string container, string projectId, string buildId,
        PdfPosition position, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("docker")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "exec", "-i", container, "node", "-e", Script }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("No se pudo iniciar SyncTeX.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            var payload = JsonSerializer.Serialize(new { projectId, buildId, page = position.Page, h = position.H, v = position.V });
            await process.StandardInput.WriteAsync(payload.AsMemory(), cancellationToken);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0) throw new IOException(await error);
            return JsonSerializer.Deserialize<SyncTexResponse>(await output, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new IOException("SyncTeX devolvió una respuesta vacía.");
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    // Run against the immutable build cache explicitly. The current CLSI route
    // can pass an absolute path to the latest compile even when buildId is set.
    private const string Script = """
        const fs = require('node:fs');
        const path = require('node:path');
        const {execFile} = require('node:child_process');
        let input = '';
        process.stdin.setEncoding('utf8');
        process.stdin.on('data', data => input += data);
        process.stdin.on('end', () => {
          const p = JSON.parse(input);
          const directory = path.join(process.env.CLSI_OUTPUT_PATH || '/var/lib/overleaf/data/output', p.projectId, 'generated-files', p.buildId);
          const sourceDirectory = path.join(process.env.CLSI_COMPILES_PATH || '/var/lib/overleaf/data/compiles', p.projectId);
          if (!fs.existsSync(path.join(directory, 'output.pdf')) || !fs.existsSync(path.join(directory, 'output.synctex.gz'))) {
            console.log(JSON.stringify({code: [], error: 'missing-build'})); return;
          }
          const target = `${p.page}:${p.h}:${p.v}:${path.join(directory, 'output.pdf')}`;
          execFile('synctex', ['edit', '-o', target], {cwd: directory, timeout: 10000, maxBuffer: 1048576}, (error, stdout) => {
            if (error) { console.error(error.message); process.exitCode = 1; return; }
            const records = [];
            let record;
            for (const line of stdout.split(/\r?\n/)) {
              const colon = line.indexOf(':');
              if (colon < 0) continue;
              const key = line.slice(0, colon).trim(), value = line.slice(colon + 1).trim();
              if (key === 'Output') { record = {}; records.push(record); }
              else if (record && key === 'Input') {
                record.file = path.isAbsolute(value) ? path.relative(sourceDirectory, value) : path.normalize(value);
              } else if (record && key === 'Line') record.line = Number(value);
              else if (record && key === 'Column') record.column = Math.max(1, Number(value) + 1);
            }
            console.log(JSON.stringify({code: records.filter(r => r.file && r.line > 0 && !path.isAbsolute(r.file) && !r.file.split('/').includes('..'))}));
          });
        });
        """;
}

record PdfPosition(int Page, double H, double V);
record SourcePosition(string File, int Line, int Column);
record SyncTexResponse(List<SourcePosition> Code, string? Error);
