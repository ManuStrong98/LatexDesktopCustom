using LatexDesktopCustom.Compilation;

try
{
    await AdapterHost.EnsureStartedAsync();
    await AdapterHost.EnsureStartedAsync();
    Console.WriteLine("OK: adaptador listo y reutilizado sin iniciar otra instancia.");
}
finally { AdapterHost.StopOwned(); }
