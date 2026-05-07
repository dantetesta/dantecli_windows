using System;
using System.IO;
using System.Windows;

namespace DanteCLI;

public partial class App : Application
{
    private static readonly string LogPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "dantecli_startup.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("OnStartup");
        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
            Log($"Unhandled: {ev.ExceptionObject}");
        DispatcherUnhandledException += (s, ev) =>
        {
            Log($"DispatcherUnhandled: {ev.Exception}");
            ev.Handled = true;
        };
        base.OnStartup(e);
    }

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { /* ignore */ }
    }
}
