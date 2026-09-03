using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ScamBaitDesk;

public sealed partial class MainWindow
{
    private bool _updateCommandBarInitialized;

    internal void InitializeUpdateCommandBar()
    {
        if (_updateCommandBarInitialized) return;
        _updateCommandBarInitialized = true;

        if (Content is not Grid root || root.Children.Count == 0 || root.Children[0] is not CommandBar commandBar)
            return;

        if (commandBar.PrimaryCommands.OfType<AppBarButton>().Any(button =>
            string.Equals(button.Label?.ToString(), "Update", StringComparison.OrdinalIgnoreCase)))
            return;

        var updateButton = new AppBarButton
        {
            Icon = new SymbolIcon(Symbol.Sync),
            Label = "Update"
        };
        ToolTipService.SetToolTip(updateButton, "Check for and install the latest ScamBait Desk build");
        updateButton.Click += UpdateCommandBar_Click;
        commandBar.PrimaryCommands.Insert(0, updateButton);
    }

    private async void UpdateCommandBar_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as AppBarButton;
        if (button is not null)
            button.IsEnabled = false;

        try
        {
            var updater = _appUpdate.FindUpdater();
            if (updater is null)
            {
                var bootstrap = Path.Combine(AppContext.BaseDirectory, "scripts", "Bootstrap-ScamBaitDeskUpdate.ps1");
                if (File.Exists(bootstrap))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        UseShellExecute = true,
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{bootstrap}\""
                    });
                    return;
                }

                await ShowMessage("The updater could not be found in this installation. Please install the latest ScamBait Desk build once from the development repository.");
                return;
            }

            var check = await CheckForUpdatesAsync();
            if (check is null || !check.IsAvailable)
            {
                await ShowMessage("ScamBait Desk is already up to date.");
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Update available",
                Content = $"Version {check.Version} is ready to install. ScamBait Desk will close and restart after the update.",
                PrimaryButtonText = "Update now",
                CloseButtonText = "Later",
                XamlRoot = Content.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await _appUpdate.InstallUpdateAsync(check);
        }
        catch (Exception exception)
        {
            await ShowMessage("The update could not be started. " + exception.Message);
        }
        finally
        {
            if (button is not null)
                button.IsEnabled = true;
        }
    }
}
