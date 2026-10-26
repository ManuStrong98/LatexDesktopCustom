using System.Configuration;
using System.Data;
using System.Windows;

namespace LatexDesktopCustom;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        Compilation.AdapterHost.StopOwned();
        base.OnExit(e);
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try { await Compilation.AdapterHost.EnsureStartedAsync(); }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo preparar el adaptador: " + ex.Message, "LatexDesktop", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
    public App()
    {
        DispatcherUnhandledException += (_, e) => System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "latexdesktop-terminal-error.log"), e.Exception.ToString() + Environment.NewLine);
    }}

