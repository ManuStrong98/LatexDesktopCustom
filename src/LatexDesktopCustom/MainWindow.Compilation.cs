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

    private void SelectMainFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "Seleccionar archivo principal LaTeX", Filter = "Archivos LaTeX (*.tex)|*.tex", CheckFileExists = true };
        if (picker.ShowDialog(this) == true) SelectMainFile(picker.FileName);
    }

    private async void Compile_Click(object sender, RoutedEventArgs e) => await CompileSelectedProjectAsync();

    private async Task CompileSelectedProjectAsync()
    {
        if (compiling || isClosing) return;
        if (selectedMainFile is null) { SelectMainFile_Click(this, new RoutedEventArgs()); if (selectedMainFile is null) return; }
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
                OpenPdfFile(result.PdfPath);
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
