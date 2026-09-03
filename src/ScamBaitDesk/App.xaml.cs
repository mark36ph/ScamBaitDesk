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
        WriteStartupLog("App constructor entered.");
        try
        {
            InitializeComponent();
            WriteStartupLog("InitializeComponent completed.");
            UnhandledException += App_UnhandledException;
        }
        catch (Exception exception)
        {
            WriteStartupLog("App constructor failed: " + exception);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        WriteStartupLog("OnLaunched entered.");
        try
        {
            ConfigureTaskbarIdentity();
            WriteStartupLog("Taskbar identity configured.");
            var mainWindow = new MainWindow();
            _window = mainWindow;
            WriteStartupLog("MainWindow created; initializing phone workspace.");
            mainWindow.InitializePhoneWorkspace();
            WriteStartupLog("Phone workspace initialized; initializing channel chooser.");
            mainWindow.InitializeChannelChooser();
            WriteStartupLog("Channel chooser initialized; initializing update command bar.");
            mainWindow.InitializeUpdateCommandBar();
            WriteStartupLog("Update command bar initialized; activating window.");
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
