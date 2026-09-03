using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ScamBaitDesk;

public sealed partial class MainWindow
{
    private bool _updateCommandBarInitialized;

    private void InitializeUpdateCommandBar()
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
            Label = "Update",
            ToolTipService = { ToolTip = "Check for and install the latest ScamBait Desk build" }
        };
        updateButton.Click += UpdateCommandBar_Click;
        commandBar.PrimaryCommands.Insert(0, updateButton);
    }

    private void UpdateCommandBar_Click(object sender, RoutedEventArgs e) => UpdateApp_Click(sender, e);

    private void MainWindow_UpdateCommandBarLoaded(object sender, RoutedEventArgs e)
    {
        InitializeUpdateCommandBar();
    }

    private bool _updateCommandBarHooked = HookUpdateCommandBar();

    private bool HookUpdateCommandBar()
    {
        Loaded += MainWindow_UpdateCommandBarLoaded;
        return true;
    }
}
