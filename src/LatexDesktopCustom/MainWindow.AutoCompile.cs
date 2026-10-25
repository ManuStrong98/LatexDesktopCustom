using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace LatexDesktopCustom;

public partial class MainWindow
{
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private string? lastSaveNotification;
    private DateTime compileAfterSave;
    private bool compilePending;

    private async void CheckNeovimSave(object? sender, EventArgs e)
    {
        if (isClosing || neovimSession is null) return;
        try
        {
            var path = neovimSession.SaveNotificationPath;
            if (File.Exists(path))
            {
                var notification = File.ReadAllText(path);
                if (notification != lastSaveNotification)
                {
                    using var json = JsonDocument.Parse(notification);
                    var file = json.RootElement.GetProperty("file").GetString();
                    lastSaveNotification = notification;
                    if (selectedMainFile is not null && file is not null &&
                        Path.GetFullPath(file).StartsWith(Path.GetDirectoryName(selectedMainFile)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        compilePending = true;
                        compileAfterSave = DateTime.UtcNow.AddMilliseconds(500);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { /* Retry a notification still being written. */ }
        if (!compilePending || compiling || openingProject || !viewerReady || DateTime.UtcNow < compileAfterSave) return;
        compilePending = false;
        await CompileSelectedProjectAsync();
    }
}
