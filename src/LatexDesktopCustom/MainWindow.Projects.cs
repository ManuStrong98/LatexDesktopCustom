using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LatexDesktopCustom.Projects;

namespace LatexDesktopCustom;

public partial class MainWindow
{
    private readonly ProjectHistory projectHistory = new();
    private readonly DispatcherTimer projectPositionTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool capturingPosition;
    private bool openingProject;
    private bool projectOpened;

    private async Task CaptureProjectPositionAsync()
    {
        if (!projectOpened || capturingPosition || selectedMainFile is null || neovimSession is null || isClosing) return;
        capturingPosition = true;
        var main = selectedMainFile;
        try
        {
            var position = await neovimSession.GetPositionAsync(compilationCancellation.Token);
            projectHistory.Position(main, position.File, position.Line, position.Column);
        }
        catch (Exception) { /* Terminal may be starting or already closed. */ }
        finally { capturingPosition = false; }
    }

    private void RecentProjects_Click(object sender, RoutedEventArgs e)
    {
        if (compiling || openingProject) return;
        var menu = new ContextMenu();
        foreach (var project in projectHistory.Items.ToArray())
        {
            var exists = File.Exists(project.MainFile);
            var item = new MenuItem
            {
                Header = $"{project.Name} — {project.MainFile}" + (exists ? "" : " (no disponible)"),
                ToolTip = $"Última apertura: {project.LastOpened.LocalDateTime:g}",
                IsEnabled = exists
            };
            item.Click += async (_, _) => await OpenMainFileInEditorAsync(project.MainFile);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "Todavía no hay proyectos recientes", IsEnabled = false });
        menu.Items.Add(new Separator());
        var open = new MenuItem { Header = "Abrir otro proyecto…" };
        open.Click += async (_, _) => await PickMainFileAsync();
        menu.Items.Add(open);
        menu.PlacementTarget = RecentProjectsButton;
        menu.IsOpen = true;
    }

    private void ClearProjectPdf()
    {
        compiledPdfPath = null;
        DownloadPdfButton.IsEnabled = false;
        displayedDocumentId = null;
        displayedCompilation = null;
        compileLogPath = null;
        OpenCompileLogButton.Visibility = Visibility.Collapsed;
        PdfFileName.Text = "Ningún PDF abierto";
        PdfViewerStatus.Text = "Compila el proyecto para visualizar su PDF.";
        RetryPdfViewerButton.Visibility = Visibility.Collapsed;
        PdfViewerStatusPanel.Visibility = Visibility.Visible;
    }
}
