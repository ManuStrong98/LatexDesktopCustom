using LatexDesktopCustom.Projects;

var directory = Path.Combine(Path.GetTempPath(), "LatexDesktopHistory-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var path = Path.Combine(directory, "projects.json");
var first = Path.Combine(directory, "proyecto á", "main.tex");
var second = Path.Combine(directory, "otro", "main.tex");
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
try
{
    var history = new ProjectHistory(path);
    Check(history.Items.Count == 0, "Missing history must be empty");
    history.Open(first);
    history.Position(first, Path.Combine(directory, "proyecto á", "capítulo.tex"), 42, 7);
    history.Open(second);
    history.Open(first.ToUpperInvariant());
    var saved = new ProjectHistory(path);
    Check(saved.Items.Count == 2 && saved.Items[0].MainFile.Equals(first, StringComparison.OrdinalIgnoreCase), "Recent order and case insensitive deduplication");
    Check(saved.Items[0].Line == 42 && saved.Items[0].Column == 7, "Position must survive reopening");
    saved.Position(first, second, 99, 1);
    Check(saved.Items[0].Line == 42, "Do not restore files outside project");
    File.WriteAllText(path, "invalid JSON");
    Check(new ProjectHistory(path).Items.Count == 0, "Corrupt history must not prevent startup");
    Console.WriteLine("OK: persistencia, orden, duplicados, cursor, límites del proyecto e historial corrupto.");
}
finally
{
    foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
    Directory.Delete(directory);
}
