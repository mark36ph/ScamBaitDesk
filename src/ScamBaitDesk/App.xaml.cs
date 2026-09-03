using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace ScamBaitDesk;

public partial class App : Application
{
    private Window? _window;
    private static readonly string StartupLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScamBaitDesk",
        "startup.log");

    public App()
    {
        InitializeComponent();
        UnhandledException += App_UnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            WriteStartupLog("Launch requested.");
            ConfigureTaskbarIdentity();
            WriteStartupLog("Creating MainWindow.");
            _window = new MainWindow();
            WriteStartupLog("MainWindow created; activating window.");
            _window.Activate();
            WriteStartupLog("Window activated successfully.");
        }
        catch (Exception exception)
        {
            WriteStartupLog("Launch failed: " + exception);
            throw;
        }
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        WriteStartupLog("Unhandled UI exception: " + args.Exception);
    }

    private static void WriteStartupLog(string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(StartupLogPath)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(StartupLogPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never prevent the application from starting.
        }
    }

    private static void ConfigureTaskbarIdentity()
    {
        try
        {
            var applicationUserModelId = $"{Package.Current.Id.FamilyName}!ScamBaitDeskApp";
            _ = SetCurrentProcessExplicitAppUserModelID(applicationUserModelId);
        }
        catch
        {
            // Unpackaged development runs still receive a stable taskbar identity.
            _ = SetCurrentProcessExplicitAppUserModelID("ScamBaitDesk.Desktop");
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string applicationUserModelId);
}
