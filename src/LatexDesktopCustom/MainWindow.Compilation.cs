using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using LatexDesktopCustom.Compilation;

namespace LatexDesktopCustom;

public partial class MainWindow
{
    private readonly CompilationClient compilationClient = new();
    private readonly CancellationTokenSource compilationCancellation = new();
    private string? selectedMainFile;
    private string? compileLogPath;
    private bool compiling;

    private void ConfigureCompilationProject()
    {
        var arguments = Environment.GetCommandLineArgs();
        var argumentIndex = Array.IndexOf(arguments, "--compile");
        if (argumentIndex >= 0 && argumentIndex + 1 < arguments.Length && File.Exists(arguments[argumentIndex + 1]))
        {
            SelectMainFile(arguments[argumentIndex + 1]);
            return;
        }
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "plantilla_apa", "main.tex");
            if (File.Exists(candidate)) { SelectMainFile(candidate); break; }
            directory = directory.Parent;
        }
    }

    private void SelectMainFile(string path)
    {
        selectedMainFile = Path.GetFullPath(path);
        MainFileLabel.Text = selectedMainFile;
        MainFileLabel.ToolTip = selectedMainFile;
    }

    private async void SelectMainFile_Click(object sender, RoutedEventArgs e) => await PickMainFileAsync();

    private async Task<bool> PickMainFileAsync()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "Seleccionar archivo principal LaTeX", Filter = "Archivos LaTeX (*.tex)|*.tex", CheckFileExists = true };
        if (selectedMainFile is not null) picker.InitialDirectory = Path.GetDirectoryName(selectedMainFile);
        return picker.ShowDialog(this) == true && await OpenMainFileInEditorAsync(picker.FileName);
    }

    private async Task<bool> OpenMainFileInEditorAsync(string path)
    {
        SelectMainFileButton.IsEnabled = CompileButton.IsEnabled = false;
        try
        {
            if (neovimSession is null) throw new InvalidOperationException("Neovim todavía no está listo.");
            CompilationStatus.Text = "Abriendo archivo en Neovim…";
            await neovimSession.OpenFileAsync(path, compilationCancellation.Token);
            if (isClosing) return false;
            SelectMainFile(path);
            CompilationStatus.Text = "Archivo abierto en Neovim. Directorio del proyecto actualizado.";
            powerShellWindow?.BringToFront();
            splitterInputSurface?.BringToFront();
            return true;
        }
        catch (OperationCanceledException)
        {
            if (!isClosing) CompilationStatus.Text = "Neovim no respondió a tiempo. Comprueba que siga abierto y vuelve a seleccionar el archivo.";
            return false;
        }
        catch (Exception ex)
        {
            if (!isClosing) CompilationStatus.Text = ex.Message;
            return false;
        }
        finally
        {
            if (!isClosing) SelectMainFileButton.IsEnabled = CompileButton.IsEnabled = true;
        }
    }

    private async void Compile_Click(object sender, RoutedEventArgs e) => await CompileSelectedProjectAsync();

    private async Task CompileSelectedProjectAsync()
    {
        if (compiling || isClosing) return;
        if (selectedMainFile is null && !await PickMainFileAsync()) return;
        if (selectedMainFile is null) return;
        compiling = true;
        CompileButton.IsEnabled = SelectMainFileButton.IsEnabled = false;
        CompilationStatus.Text = "Compilando con Overleaf…";
        OpenCompileLogButton.Visibility = Visibility.Collapsed;
        try
        {
            var result = await compilationClient.CompileAsync(selectedMainFile, compilationCancellation.Token);
            if (isClosing) return;
            compileLogPath = result.LogPath;
            OpenCompileLogButton.Visibility = compileLogPath is null ? Visibility.Collapsed : Visibility.Visible;
            if (result.PdfPath is not null)
            {
                OpenPdfFile(result.PdfPath, result.Snapshot);
                CompilationStatus.Text = "Compilación completada. PDF actualizado.";
            }
            else
            {
                CompilationStatus.Text = $"{result.Error?.TrimEnd('.', ' ')}. Consulta Ver log.";
            }
        }
        catch (HttpRequestException)
        {
            if (!isClosing) CompilationStatus.Text = "No se pudo conectar con el adaptador (127.0.0.1:5017). Inícialo con dotnet run --project backend/desktop-adapter.";
        }
        catch (OperationCanceledException)
        {
            if (!isClosing) CompilationStatus.Text = "La compilación se canceló o excedió el tiempo de espera.";
        }
        catch (Exception ex)
        {
            if (!isClosing) CompilationStatus.Text = $"No se pudo compilar: {ex.Message}";
        }
        finally
        {
            compiling = false;
            if (!isClosing) CompileButton.IsEnabled = SelectMainFileButton.IsEnabled = true;
        }
    }

    private void OpenCompileLog_Click(object sender, RoutedEventArgs e)
    {
        if (compileLogPath is not null && File.Exists(compileLogPath))
        {
            var start = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
            start.ArgumentList.Add(compileLogPath);
            Process.Start(start);
        }
    }
}
