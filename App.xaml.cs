using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ClaudeUsageWidget;

public partial class App : Application
{
    // WPF occasionally throws from inside its own window-message handling — seen as a
    // Win32Exception ("The system cannot find the file specified") shortly after the
    // PC woke from Modern Standby, which took down every widget at once. Nothing here
    // can be fixed at the source, and a widget is better left running than lost, so
    // log what happened (with the stack, to pin down the origin) and carry on.
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log(args.ExceptionObject);
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        e.Handled = true;
    }

    private static string ErrorLogPath => Path.Combine(AppProfile.DataDir, "errors.log");

    private static void Log(object exception)
    {
        try
        {
            Directory.CreateDirectory(AppProfile.DataDir);
            File.AppendAllText(ErrorLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never be what brings the widget down.
        }
    }
}
