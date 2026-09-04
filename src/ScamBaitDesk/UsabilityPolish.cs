using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ScamBaitDesk;

public sealed partial class MainWindow
{
    private bool _usabilityPolishInitialized;

    internal void InitializeUsabilityPolish()
    {
        if (_usabilityPolishInitialized) return;
        _usabilityPolishInitialized = true;
        Loaded += UsabilityPolish_Loaded;
    }

    private void UsabilityPolish_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= UsabilityPolish_Loaded;

        SimplifyTopBar();
        OrganizeCommandBar();
        SimplifyNavigation();
        SimplifyCollectionTabs();
        SimplifyWorkspaceTabs();
        TidyLayout();

        SearchBox.PlaceholderText = "Search messages or cases…";
        SearchBox.Header = "Find a message or case";

        AddNavigationToolTips();
        AddWorkspaceToolTips();
        AddHomeQuickStart();
    }

    private void SimplifyTopBar()
    {
        foreach (var command in FindCommandBarButtons())
        {
            var label = command switch
            {
                AppBarButton button => button.Label?.ToString(),
                AppBarToggleButton toggle => toggle.Label?.ToString(),
                _ => null
            };

            var simplified = label switch
            {
                "Sync inbox" => "Sync",
                "Monitor inbox" => "Monitor",
                "Create case" => "New case",
                "Save changes" => "Save",
                "Export evidence" => "Export",
                "Check website" => "Website",
                "GLOBAL SEND STOP" => "STOP",
                _ => null
            };

            if (simplified is null) continue;
            switch (command)
            {
                case AppBarButton button: button.Label = simplified; break;
                case AppBarToggleButton toggle: toggle.Label = simplified; break;
            }
        }
    }

    private void OrganizeCommandBar()
    {
        if (Content is not Grid root || root.Children.OfType<CommandBar>().FirstOrDefault() is not CommandBar bar)
            return;

        // Keep the primary bar focused on the actions people use most often.
        var moveToSecondary = bar.PrimaryCommands
            .OfType<Control>()
            .Where(command => command is AppBarButton button &&
                (button.Label?.ToString() is "Save" or "Export"))
            .ToList();

        foreach (var command in moveToSecondary)
        {
            bar.PrimaryCommands.Remove(command);
            bar.SecondaryCommands.Add(command);
        }

        // Monitoring is useful, but it does not need to occupy the main action row.
        var monitor = bar.PrimaryCommands.OfType<AppBarToggleButton>()
            .FirstOrDefault(button => string.Equals(button.Label?.ToString(), "Monitor", StringComparison.OrdinalIgnoreCase));
        if (monitor is not null)
        {
            bar.PrimaryCommands.Remove(monitor);
            bar.SecondaryCommands.Add(monitor);
        }

        bar.DefaultLabelPosition = CommandBarDefaultLabelPosition.Right;
    }

    private void TidyLayout()
    {
        if (Content is not Grid root || root.Children.Count < 2 || root.Children[1] is not Grid layout)
            return;

        // Give the main workspace more room while keeping navigation readable.
        if (layout.ColumnDefinitions.Count >= 3)
        {
            layout.ColumnDefinitions[0].Width = new GridLength(195);
            CollectionColumn.Width = new GridLength(265);
        }

        if (layout.Children.OfType<Border>().FirstOrDefault(border => Grid.GetColumn(border) == 2) is Border workspaceBorder)
        {
            workspaceBorder.Margin = new Thickness(12);
            workspaceBorder.CornerRadius = new CornerRadius(10);
        }

        if (CollectionPane is not null)
            CollectionPane.Padding = new Thickness(10);

        if (WorkspaceTabs is not null)
            WorkspaceTabs.Margin = new Thickness(0);
    }

    private void SimplifyNavigation()
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Home"] = "Start",
            ["Inbox"] = "Messages & cases",
            ["Case"] = "Review",
            ["Investigate"] = "Investigate",
            ["Engage"] = "Respond safely",
            ["Report"] = "Finish & report",
            ["Website"] = "Website check"
        };

        foreach (var item in ShellMenu.Items.OfType<ListViewItem>())
        {
            if (item.Tag is not string tag || !labels.TryGetValue(tag, out var label))
                continue;

            if (item.Content is StackPanel panel)
            {
                var text = panel.Children.OfType<TextBlock>().FirstOrDefault();
                if (text is not null)
                    text.Text = label;
            }
        }
    }

    private void SimplifyCollectionTabs()
    {
        InboxCollectionTab.Header = "Messages";
        CasesCollectionTab.Header = "Cases";
        DashboardCollectionTab.Header = "Overview";
        ActionsCollectionTab.Header = "Next steps";
    }

    private void SimplifyWorkspaceTabs()
    {
        GuideTab.Header = "Start";
        ReviewTab.Header = "Review";
        ReplyTab.Header = "Respond";
        CallsTab.Header = "Phone";
        NotesTab.Header = "Case notes";
        WorkspaceTabs.SelectedItem = GuideTab;
    }

    private IEnumerable<Control> FindCommandBarButtons()
    {
        if (Content is not Grid root) yield break;
        foreach (var commandBar in root.Children.OfType<CommandBar>())
        {
            foreach (var button in commandBar.PrimaryCommands.OfType<AppBarButton>()) yield return button;
            foreach (var toggle in commandBar.PrimaryCommands.OfType<AppBarToggleButton>()) yield return toggle;
            foreach (var button in commandBar.SecondaryCommands.OfType<AppBarButton>()) yield return button;
            foreach (var toggle in commandBar.SecondaryCommands.OfType<AppBarToggleButton>()) yield return toggle;
        }
    }

    private void AddNavigationToolTips()
    {
        var tips = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Home"] = "Start here: choose Email, Phone or Website.",
            ["Inbox"] = "Find suspicious messages and saved cases.",
            ["Case"] = "Review the selected case and its risk signals.",
            ["Investigate"] = "Check websites, headers, indicators and evidence.",
            ["Engage"] = "Plan and write a safe manual response.",
            ["Report"] = "Finish the case and export evidence.",
            ["Website"] = "Check a suspicious website without opening it normally."
        };

        foreach (var item in ShellMenu.Items.OfType<ListViewItem>())
        {
            if (item.Tag is string tag && tips.TryGetValue(tag, out var tip))
                ToolTipService.SetToolTip(item, tip);
        }

        ToolTipService.SetToolTip(EmergencyStopButton, "Emergency control: disable engagement actions.");
    }

    private void AddWorkspaceToolTips()
    {
        ToolTipService.SetToolTip(NextStepButton, "Follow the recommended next step for the current case.");
        ToolTipService.SetToolTip(StopEngagementButton, "Permanently stop engagement for the current case.");
    }

    private void AddHomeQuickStart()
    {
        if (GuideTab.Content is not ScrollViewer scrollViewer || scrollViewer.Content is not StackPanel stack)
            return;

        if (stack.Children.OfType<Border>().Any(border =>
                border.Child is StackPanel panel &&
                panel.Children.OfType<TextBlock>().Any(text => text.Text == "CHOOSE WHAT YOU'RE CHECKING")))
            return;

        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14)
        };

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "CHOOSE WHAT YOU'RE CHECKING",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Opacity = 0.65
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Pick a scam channel first. ScamBait Desk will take you to the right workspace.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(CreateQuickStartButton("Email", "Inbox"));
        buttons.Children.Add(CreateQuickStartButton("Phone", "Engage"));
        buttons.Children.Add(CreateQuickStartButton("Website", "Website"));
        panel.Children.Add(buttons);
        card.Child = panel;

        stack.Children.Insert(Math.Min(3, stack.Children.Count), card);
    }

    private Button CreateQuickStartButton(string label, string destination)
    {
        var button = new Button { Content = label, Tag = destination, MinWidth = 100 };
        button.Click += NavigateTo_Click;
        return button;
    }
}
