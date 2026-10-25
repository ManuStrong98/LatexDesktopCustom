namespace LatexDesktopCustom;

public partial class MainWindow
{
    private bool navigatingToSource;

    private async Task GoToPdfSourceAsync(string documentId, int page, double h, double v)
    {
        if (navigatingToSource || isClosing || documentId != displayedDocumentId) return;
        var snapshot = displayedCompilation;
        if (snapshot is null) { CompilationStatus.Text = "Compila este proyecto desde la app para activar SyncTeX."; return; }
        if (page < 1 || !double.IsFinite(h) || !double.IsFinite(v) || h < 0 || v < 0) return;
        navigatingToSource = true;
        try
        {
            CompilationStatus.Text = "Buscando la posición en el código…";
            var token = compilationCancellation.Token;
            var position = await compilationClient.SyncFromPdfAsync(snapshot, page, h, v, token);
            if (isClosing || documentId != displayedDocumentId) return;
            if (position is null) { CompilationStatus.Text = "SyncTeX no encontró código del proyecto para esta posición."; return; }
            await snapshot.EnsureSourcesUnchangedAsync(token);
            if (isClosing || documentId != displayedDocumentId) return;
            if (neovimSession is null) throw new InvalidOperationException("Neovim no está disponible.");
            var path = snapshot.SourcePath(position.File);
            await neovimSession.OpenLocationAsync(path, snapshot.ProjectDirectory, position.Line, Math.Max(1, position.Column), token);
            if (isClosing) return;
            CompilationStatus.Text = $"{position.File}, línea {position.Line}.";
            powerShellWindow?.Activate();
            splitterInputSurface?.BringToFront();
        }
        catch (OperationCanceledException) { if (!isClosing) CompilationStatus.Text = "La búsqueda se canceló o Neovim no respondió a tiempo."; }
        catch (Exception ex) { if (!isClosing) CompilationStatus.Text = ex.Message; }
        finally { navigatingToSource = false; }
    }
}
