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
        if (sender is AppBarButton button)
            button.IsEnabled = false;

        try
        {
            var updater = _appUpdate.FindUpdater();
            if (updater is null)
            {
                await ShowMessage("The updater is not available in this installation. Please run the updater once from the ScamBait Desk development folder.");
                return;
            }

            var check = await _appUpdate.CheckAsync();
            if (!check.IsAvailable)
            {
                await ShowMessage(check.Message);
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Update available",
                Content = $"Build {check.LatestBuild} is available (current build {check.CurrentBuild}). ScamBait Desk will close and restart during the update.",
                PrimaryButtonText = "Update now",
                CloseButtonText = "Later",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _appUpdate.Launch(updater);
                await ShowMessage("The updater has been started. ScamBait Desk will close when the new build is ready.");
                Close();
            }
        }
        catch (Exception exception)
        {
            await ShowMessage("The update could not be started. " + exception.Message);
        }
        finally
        {
            if (sender is AppBarButton updateButton)
                updateButton.IsEnabled = true;
        }
    }
}
