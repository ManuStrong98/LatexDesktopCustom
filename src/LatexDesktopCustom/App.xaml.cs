using System.Configuration;
using System.Data;
using System.Windows;

namespace LatexDesktopCustom;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) => System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "latexdesktop-terminal-error.log"), e.Exception.ToString() + Environment.NewLine);
    }}

