using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5017");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 32 * 1024 * 1024);
builder.Services.AddHttpClient("overleaf", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Overleaf:Url"] ?? "http://127.0.0.1");
    client.Timeout = TimeSpan.FromSeconds(30);
});
var app = builder.Build();
var compileLock = new SemaphoreSlim(1, 1);
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var container = builder.Configuration["Overleaf:Container"] ?? "sharelatex";

app.MapGet("/health", () => Results.Ok(new { status = "ready" }));

app.MapPost("/api/projects/{projectId}/compile", async (string projectId, CompileRequest request, HttpContext context) =>
{
    if (!ValidId(projectId) || request.Resources is not { Count: > 0 and <= 2000 }
        || !ValidPath(request.RootResourcePath) || request.Resources.Any(file => file is null || !ValidPath(file.Path) || (file.Content is null) == (file.Base64 is null))
        || !request.Resources.Any(file => file.Path == request.RootResourcePath && file.Content is not null)
        || request.Resources.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count() != request.Resources.Count
        || request.Compiler is not ("pdflatex" or "xelatex" or "lualatex" or "latex"))
        return Results.BadRequest(new { error = "Proyecto, rutas, recursos o compilador inválidos." });
    long sourceBytes = 0;
    long textBytes = 0;
    try
    {
        foreach (var file in request.Resources)
        {
            var size = file.Content is not null ? Encoding.UTF8.GetByteCount(file.Content) : Convert.FromBase64String(file.Base64!).Length;
            sourceBytes += size;
            if (file.Content is not null) textBytes += size;
        }
    }
    catch (FormatException) { return Results.BadRequest(new { error = "Un recurso binario contiene Base64 inválido." }); }
    if (sourceBytes > 20 * 1024 * 1024 || textBytes > 5 * 1024 * 1024)
        return Results.Json(new { error = "El proyecto supera 20 MB de recursos o 5 MB de texto." }, statusCode: 413);
    if (!await compileLock.WaitAsync(0, context.RequestAborted))
        return Results.Conflict(new { error = "Ya hay una compilación en curso. Inténtalo al terminar." });
    try
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(100));
        var binaries = request.Resources.Where(file => file.Base64 is not null).ToArray();
        await using var binaryServer = binaries.Length == 0 ? null : await BinaryResourceServer.StartAsync(container, binaries, timeout.Token);
        var resources = request.Resources.Select(file => file.Content is not null
            ? new Dictionary<string, object> { ["path"] = file.Path, ["content"] = file.Content }
            : new Dictionary<string, object> { ["path"] = file.Path, ["url"] = binaryServer!.UrlFor(file.Path) });
        var payload = JsonSerializer.Serialize(new
        {
            compile = new
            {
                options = new { compiler = request.Compiler, timeout = 60 },
                rootResourcePath = request.RootResourcePath,
                resources
            }
        }, jsonOptions);
        var response = await CallClsi(container, $"/project/{projectId}/compile", payload, timeout.Token);
        using var document = JsonDocument.Parse(response);
        var compile = document.RootElement.GetProperty("compile");
        var status = compile.GetProperty("status").GetString() ?? "error";
        var files = compile.TryGetProperty("outputFiles", out var output) ? output.EnumerateArray().ToArray() : [];
        string? FileUrl(string name)
        {
            var file = files.FirstOrDefault(file => file.GetProperty("path").GetString() == name);
            if (file.ValueKind == JsonValueKind.Undefined) return null;
            var build = file.GetProperty("build").GetString();
            return ValidBuild(build) ? $"/api/projects/{projectId}/builds/{build}/files/{name}" : null;
        }
        return Results.Ok(new
        {
            status,
            buildId = compile.TryGetProperty("buildId", out var buildId) ? buildId.GetString() : null,
            pdfUrl = FileUrl("output.pdf"),
            logUrl = FileUrl("output.log"),
            error = compile.TryGetProperty("error", out var error) ? error.ToString() : null
        });
    }
    catch (OperationCanceledException) { return Results.Problem("La compilación se canceló o excedió el tiempo de espera.", statusCode: 504); }
    catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
    {
        app.Logger.LogError(ex, "No se pudo compilar el proyecto {ProjectId}", projectId);
        return Results.Problem($"No se pudo acceder a CLSI. Comprueba Docker y el contenedor '{container}'. {ex.Message}", statusCode: 502);
    }
    finally { compileLock.Release(); }
});

app.MapGet("/api/projects/{projectId}/builds/{buildId}/files/{name}", async
    (string projectId, string buildId, string name, IHttpClientFactory factory, HttpContext context) =>
{
    if (!ValidId(projectId) || !ValidBuild(buildId) || name is not ("output.pdf" or "output.log" or "output.synctex.gz"))
        return Results.BadRequest();
    try
    {
        var client = factory.CreateClient("overleaf");
        using var response = await client.GetAsync($"/project/{projectId}/build/{buildId}/output/{name}", context.RequestAborted);
        if (!response.IsSuccessStatusCode) return Results.Problem("Overleaf no pudo entregar el archivo compilado.", statusCode: (int)response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(context.RequestAborted);
        return Results.File(bytes, name == "output.pdf" ? "application/pdf" : name.EndsWith(".gz") ? "application/gzip" : "text/plain; charset=utf-8");
    }
    catch (HttpRequestException) { return Results.Problem("No se pudo descargar el resultado de Overleaf.", statusCode: 502); }
});

app.Run();

static bool ValidId(string value) => Regex.IsMatch(value, "^[0-9a-f]{24}$");
static bool ValidBuild(string? value) => value is not null && Regex.IsMatch(value, "^[0-9a-f-]{1,100}$");
static bool ValidPath(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length < 1024
    && !value.StartsWith('/') && !value.Contains('\\') && !value.Contains(':')
    && !value.Any(char.IsControl) && value.Split('/').All(part => part is not ("" or "." or ".."));

static async Task<string> CallClsi(string container, string path, string payload, CancellationToken cancellationToken)
{
    var start = new ProcessStartInfo("docker")
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
    };
    // No shell interpolation: every argument and the JSON body are passed separately.
    foreach (var argument in new[] { "exec", "-i", container, "curl", "--silent", "--show-error", "--fail-with-body", "--max-time", "90",
        "-H", "Content-Type: application/json", "--data-binary", "@-", $"http://127.0.0.1:3013{path}" })
        start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new IOException("No se pudo iniciar Docker.");
    var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
    var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
    try
    {
        await process.StandardInput.WriteAsync(payload.AsMemory(), cancellationToken);
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken);
        var body = await stdout;
        var errors = await stderr;
        // CLSI can return useful compile/error data with a non-2xx status.
        if (process.ExitCode != 0 && !body.TrimStart().StartsWith('{'))
            throw new IOException(errors.Length > 1500 ? errors[..1500] : errors);
        return body;
    }
    finally
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
    }
}

record CompileRequest(string RootResourcePath, List<ProjectResource> Resources, string Compiler = "pdflatex");
record ProjectResource(string Path, string? Content = null, string? Base64 = null);
