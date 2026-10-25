using System.IO;
using System.Text.Json;

namespace LatexDesktopCustom.Projects;

internal sealed record RecentProject(string MainFile, DateTimeOffset LastOpened, string? EditorFile = null, int Line = 1, int Column = 1)
{
    public string Directory => Path.GetDirectoryName(MainFile)!;
    public string Name => Path.GetFileName(Directory);
}

internal sealed class ProjectHistory
{
    private readonly string path;
    public List<RecentProject> Items { get; private set; } = [];
    public ProjectHistory(string? storagePath = null)
    {
        path = storagePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LatexDesktopCustom", "projects.json");
        try { Items = JsonSerializer.Deserialize<List<RecentProject>>(File.ReadAllText(path)) ?? []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        Items = Items.Where(p => Path.IsPathFullyQualified(p.MainFile)).OrderByDescending(p => p.LastOpened).Take(30).ToList();
    }
    public RecentProject? Find(string mainFile) => Items.FirstOrDefault(p => string.Equals(p.MainFile, mainFile, StringComparison.OrdinalIgnoreCase));
    public void Open(string mainFile)
    {
        mainFile = Path.GetFullPath(mainFile);
        var entry = (Find(mainFile) ?? new RecentProject(mainFile, DateTimeOffset.UtcNow)) with { LastOpened = DateTimeOffset.UtcNow };
        Items.RemoveAll(p => string.Equals(p.MainFile, mainFile, StringComparison.OrdinalIgnoreCase));
        Items.Insert(0, entry);
        Items = Items.Take(30).ToList();
        Save();
    }
    public void Position(string mainFile, string file, int line, int column)
    {
        var index = Items.FindIndex(p => string.Equals(p.MainFile, mainFile, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || !Path.GetFullPath(file).StartsWith(Path.GetDirectoryName(mainFile)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        Items[index] = Items[index] with { EditorFile = file, Line = Math.Max(1, line), Column = Math.Max(1, column) };
        Save();
    }
    private void Save()
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Items, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}
