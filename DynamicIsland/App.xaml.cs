using System.IO;
using DynamicIsland.Core;
using System.Windows;
using System.Windows.Threading;

namespace DynamicIsland;

public partial class App : Application
{
    static Mutex? _single;
    static readonly string LogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // Claude Code's permission hook runs this same exe as a tiny relay to the running island; it shows nothing.
        if (e.Args.Contains("--claude-approve")) Environment.Exit(Modules.Approvals.ApprovalServer.RunHook());

        _single = new Mutex(true, "DynamicIsland.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            if (e.Args.Contains("--settings")) SettingsSignal.Raise(); // the island is already running: ask it to open the settings
            Shutdown();
            return;
        }

        // A small widget should not vanish because one refresh hit something unexpected: log it and carry on.
        DispatcherUnhandledException += (_, args) => { Log("UI thread", args.Exception); args.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log("fatal", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => { Log("background task", args.Exception); args.SetObserved(); };

        base.OnStartup(e);

        SettingsSignal.Listen(() => Dispatcher.BeginInvoke(OpenSettings));
        if (e.Args.Contains("--settings")) Dispatcher.BeginInvoke(OpenSettings, DispatcherPriority.ContextIdle);
    }

    static void OpenSettings() => (Current.MainWindow as IslandWindow)?.OpenSettings();

    /// <summary>Appends to %LocalAppData%\DynamicIsland\error.log (kept small) so a problem can be diagnosed afterwards.</summary>
    internal static void Log(string where, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 512 * 1024) File.Delete(LogFile);
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{where}]  {ex}\r\n\r\n");
        }
        catch { /* logging must never cause a second failure */ }
    }
}
