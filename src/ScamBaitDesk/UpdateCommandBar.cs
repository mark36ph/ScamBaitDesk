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

        _ = CheckGitHubForUpdatesOnStartupAsync();

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

    private async Task CheckGitHubForUpdatesOnStartupAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2));

            UpdateStatusBar.Severity = InfoBarSeverity.Informational;
            UpdateStatusBar.Title = "Checking GitHub…";
            UpdateStatusBar.Message = "Checking GitHub for the latest ScamBait Desk build.";

            var check = await _appUpdate.CheckAsync();

            if (check.IsAvailable)
            {
                UpdateStatusBar.Severity = InfoBarSeverity.Success;
                UpdateStatusBar.Title = $"Build {check.LatestBuild} available";
                UpdateStatusBar.Message = $"A newer build is available. Current build: {check.CurrentBuild}.";
                UpdateButton.IsEnabled = true;
            }
            else
            {
                UpdateStatusBar.Severity = InfoBarSeverity.Informational;
                UpdateStatusBar.Title = $"Up to date · Build {check.CurrentBuild}";
                UpdateStatusBar.Message = "No newer build was found on GitHub.";
                UpdateButton.IsEnabled = false;
            }
        }
        catch
        {
            UpdateStatusBar.Severity = InfoBarSeverity.Warning;
            UpdateStatusBar.Title = "GitHub update check unavailable";
            UpdateStatusBar.Message = "Could not check GitHub right now. Use Check for updates to try again.";
        }
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
                await ShowMessage("The updater is not available in this installation. Please run the updater once from the Scam Bait Desk development folder.");
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
