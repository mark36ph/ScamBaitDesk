using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;

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
        TidyShellLayout();
        SimplifyTopBar();
        OrganizeCommandBar();
        SimplifyNavigation();
        SimplifyCollectionTabs();
        SimplifyWorkspaceTabs();

        SearchBox.PlaceholderText = "Search messages or cases…";
        SearchBox.Header = "Find a message or case";
        AddNavigationToolTips();
        AddWorkspaceToolTips();
        AddHomeQuickStart();
    }

    private void TidyShellLayout()
    {
        if (Content is not Grid root || root.Children.Count < 2 || root.Children[1] is not Grid layout)
            return;

        if (layout.ColumnDefinitions.Count >= 3)
        {
            layout.ColumnDefinitions[0].Width = new GridLength(220);
            CollectionColumn.Width = new GridLength(300);
        }

        var workspaceBorder = layout.Children.OfType<Border>().FirstOrDefault(border => Grid.GetColumn(border) == 2);
        if (workspaceBorder is not null)
        {
            workspaceBorder.Margin = new Thickness(12);
            workspaceBorder.CornerRadius = new CornerRadius(12);
        }

        CollectionPane.Padding = new Thickness(10);
        WorkspaceTabs.Margin = new Thickness(0);
    }

    private void SimplifyTopBar()
    {
        foreach (var command in FindCommandBarButtons())
        {
            var label = GetCommandLabel(command);
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
            if (simplified is not null) SetCommandLabel(command, simplified);
        }
    }

    private void OrganizeCommandBar()
    {
        if (Content is not Grid root || root.Children.OfType<CommandBar>().FirstOrDefault() is not CommandBar bar)
            return;

        // Keep the primary row focused on the four most common actions.
        var moveLabels = new[] { "Save", "Export", "Monitor" };
        foreach (var command in bar.PrimaryCommands.ToList())
        {
            var label = GetCommandLabel(command);
            if (label is null || !moveLabels.Contains(label, StringComparer.OrdinalIgnoreCase))
                continue;

            bar.PrimaryCommands.Remove(command);
            bar.SecondaryCommands.Add(command);
        }

        bar.DefaultLabelPosition = CommandBarDefaultLabelPosition.Right;
        bar.OverflowButtonVisibility = CommandBarOverflowButtonVisibility.Visible;
        AddTopBarToolTips(bar);
    }

    private static string? GetCommandLabel(Control command) => command switch
    {
        AppBarButton button => button.Label?.ToString(),
        AppBarToggleButton toggle => toggle.Label?.ToString(),
        _ => null
    };

    private static void SetCommandLabel(Control command, string label)
    {
        switch (command)
        {
            case AppBarButton button: button.Label = label; break;
            case AppBarToggleButton toggle: toggle.Label = label; break;
        }
    }

    private static void AddTopBarToolTips(CommandBar bar)
    {
        foreach (var command in bar.PrimaryCommands.Concat(bar.SecondaryCommands))
        {
            var label = GetCommandLabel(command);
            var tip = label switch
            {
                "Sync" => "Sync the dedicated bait mailbox.",
                "Monitor" => "Start or stop periodic inbox checks.",
                "New case" => "Create a case from the selected message.",
                "Save" => "Save the current case locally.",
                "Export" => "Export verified evidence.",
                "Website" => "Inspect a suspicious website safely.",
                "STOP" => "Emergency control: disable engagement actions.",
                "Inbox settings" => "Configure the mailbox connection.",
                "Connect Gmail OAuth" => "Connect the dedicated Gmail mailbox.",
                "Test mail connection" => "Test the configured mailbox.",
                "Manage personas" => "Manage fictional engagement personas.",
                _ => null
            };
            if (tip is not null) ToolTipService.SetToolTip(command, tip);
        }
    }

    private void SimplifyNavigation()
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Home"] = "Start",
            ["Inbox"] = "Messages & cases",
            ["Case"] = "Review case",
            ["Investigate"] = "Investigate",
            ["Engage"] = "Respond safely",
            ["Report"] = "Finish & report",
            ["Website"] = "Website check"
        };

        foreach (var item in ShellMenu.Items.OfType<ListViewItem>())
        {
            item.Padding = new Thickness(10, 8, 10, 8);
            item.Margin = new Thickness(0, 2, 0, 2);
            if (item.Tag is not string tag || !labels.TryGetValue(tag, out var label)) continue;

            if (item.Content is StackPanel panel)
            {
                var text = panel.Children.OfType<TextBlock>().FirstOrDefault();
                if (text is not null)
                {
                    text.Text = label;
                    text.FontSize = 14;
                }
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
            if (item.Tag is string tag && tips.TryGetValue(tag, out var tip))
                ToolTipService.SetToolTip(item, tip);

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
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16)
        };

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "CHOOSE WHAT YOU'RE CHECKING",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.65
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Start with one channel. The workspace will only show the tools needed for that job.",
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
        var button = new Button { Content = label, Tag = destination, MinWidth = 104 };
        button.Click += NavigateTo_Click;
        return button;
    }
}
