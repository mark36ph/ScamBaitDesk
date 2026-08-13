using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace ScamBaitDesk;

public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ConfigureTaskbarIdentity();
        _window = new MainWindow();
        _window.Activate();
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
