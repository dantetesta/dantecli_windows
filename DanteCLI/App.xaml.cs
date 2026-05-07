using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace DanteCLI;

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "dantecli_startup.log");

    public App()
    {
        Log("App ctor");
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Log($"UnhandledException: {e.ExceptionObject}");
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            Log($"UnobservedTaskException: {e.Exception}");
        UnhandledException += (s, e) =>
        {
            Log($"UnhandledException (UI): {e.Message}\n{e.Exception}");
            e.Handled = true;
        };

        try
        {
            InitializeComponent();
            Log("InitializeComponent ok");
        }
        catch (Exception ex)
        {
            Log($"InitializeComponent failed: {ex}");
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Log("OnLaunched start");
            Window = new MainWindow();
            Window.Activate();
            Log("Window activated");
        }
        catch (Exception ex)
        {
            Log($"OnLaunched failed: {ex}");
        }
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { /* ignore */ }
    }
}

