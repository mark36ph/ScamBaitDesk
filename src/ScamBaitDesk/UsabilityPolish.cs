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
        UpdateVisibleBuildNumber();
        RedesignHomePage();

        SearchBox.PlaceholderText = "Search messages or cases…";
        SearchBox.Header = "Find a message or case";
        AddNavigationToolTips();
        AddWorkspaceToolTips();
    }

    private void TidyShellLayout()
    {
        if (Content is not Grid root || root.Children.Count < 2 || root.Children[1] is not Grid layout)
            return;

        if (layout.ColumnDefinitions.Count >= 3)
        {
            layout.ColumnDefinitions[0].Width = new GridLength(250);
            CollectionColumn.Width = new GridLength(330);
        }

        var workspaceBorder = layout.Children.OfType<Border>().FirstOrDefault(border => Grid.GetColumn(border) == 2);
        if (workspaceBorder is not null)
        {
            workspaceBorder.Margin = new Thickness(10);
            workspaceBorder.CornerRadius = new CornerRadius(14);
        }

        CollectionPane.Padding = new Thickness(12);
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
            item.Padding = new Thickness(12, 10, 12, 10);
            item.Margin = new Thickness(0, 3, 0, 3);
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

    private void UpdateVisibleBuildNumber()
    {
        foreach (var text in FindDescendantTextBlocks(this))
        {
            if (text.Text is { } value && value.StartsWith("BUILD ", StringComparison.OrdinalIgnoreCase))
                text.Text = "BUILD 48";
        }
    }

    private void RedesignHomePage()
    {
        if (GuideTab.Content is not ScrollViewer scrollViewer || scrollViewer.Content is not StackPanel stack)
            return;

        stack.Padding = new Thickness(28, 24, 28, 28);
        stack.Spacing = 22;
        stack.MaxWidth = 1180;

        var titlePanel = stack.Children.OfType<StackPanel>().FirstOrDefault();
        if (titlePanel is not null)
        {
            var title = titlePanel.Children.OfType<TextBlock>().FirstOrDefault();
            if (title is not null)
            {
                title.Text = "What are you investigating?";
                title.FontSize = 34;
                title.FontWeight = FontWeights.SemiBold;
            }

            var subtitle = titlePanel.Children.OfType<TextBlock>().Skip(1).FirstOrDefault();
            if (subtitle is not null)
            {
                subtitle.Text = "Choose the source first. ScamBait Desk will open the right tools and keep the investigation manual, local and evidence-focused.";
                subtitle.FontSize = 15;
                subtitle.Opacity = 0.72;
            }
        }

        var actionPanel = stack.Children.OfType<StackPanel>().Skip(1).FirstOrDefault();
        if (actionPanel is not null)
        {
            var buttons = actionPanel.Children.OfType<Button>().ToList();
            if (buttons.Count > 0) buttons[0].Content = "Open messages";
            if (buttons.Count > 1) buttons[1].Content = "Check a website";
            actionPanel.Spacing = 10;
        }

        foreach (var child in stack.Children.OfType<Border>().ToList())
        {
            if (child.Child is StackPanel panel && panel.Children.OfType<TextBlock>().Any(text => text.Text == "CHOOSE WHAT YOU'RE CHECKING"))
                stack.Children.Remove(child);
        }

        var workflowGrid = stack.Children.OfType<Grid>().FirstOrDefault();
        if (workflowGrid is not null)
        {
            workflowGrid.Children.Clear();
            workflowGrid.RowDefinitions.Clear();
            workflowGrid.ColumnDefinitions.Clear();
            workflowGrid.ColumnSpacing = 14;
            workflowGrid.RowSpacing = 14;

            for (var i = 0; i < 3; i++)
                workflowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            workflowGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            workflowGrid.Children.Add(CreateInvestigationCard("EMAIL", "Messages, headers and links", "Review a suspicious message, preserve the case and inspect sender evidence.", "Open email investigation", "Inbox", "&#xE715;"));
            var phone = CreateInvestigationCard("PHONE", "Calls and transcripts", "Analyse a caller number and transcript for social-engineering signals without placing a call.", "Open phone investigation", "Engage", "&#xE717;");
            Grid.SetColumn(phone, 1);
            workflowGrid.Children.Add(phone);
            var website = CreateInvestigationCard("WEBSITE", "URLs and page content", "Check a URL locally first, then optionally scan limited live page content for suspicious indicators.", "Open website investigation", "Website", "&#xE774;");
            Grid.SetColumn(website, 2);
            workflowGrid.Children.Add(website);

            var workflowLabel = new TextBlock
            {
                Text = "WORKFLOW",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.58,
                Margin = new Thickness(2, 4, 0, -10)
            };
            stack.Children.Insert(Math.Min(stack.Children.IndexOf(workflowGrid), stack.Children.Count), workflowLabel);

            var existingSafety = stack.Children.OfType<Expander>().FirstOrDefault();
            if (existingSafety is not null)
            {
                var safety = new Border
                {
                    Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(16),
                    Child = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = "NEXT", FontSize = 11, FontWeight = FontWeights.SemiBold, Opacity = 0.58 },
                            new TextBlock { Text = "Collect → review → investigate → plan → respond → finish", FontSize = 17, FontWeight = FontWeights.SemiBold },
                            new TextBlock { Text = "Only continue when the previous step is complete. Use a dedicated bait identity and stop when the safety objective is met.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7 }
                        }
                    }
                };
                var safetyIndex = stack.Children.IndexOf(existingSafety);
                stack.Children.Insert(safetyIndex, safety);
            }
        }
    }

    private Border CreateInvestigationCard(string eyebrow, string title, string description, string buttonText, string destination, string glyph)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new FontIcon { Glyph = glyph, FontSize = 24, Opacity = 0.85 });
        panel.Children.Add(new TextBlock { Text = eyebrow, FontSize = 11, FontWeight = FontWeights.SemiBold, Opacity = 0.58 });
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, MinHeight = 62 });
        panel.Children.Add(new Button { Content = buttonText, Tag = destination, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 7, 14, 7) });
        var button = panel.Children.OfType<Button>().Last();
        button.Click += NavigateTo_Click;

        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18),
            Child = panel
        };
    }

    private static IEnumerable<TextBlock> FindDescendantTextBlocks(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock textBlock)
                yield return textBlock;

            foreach (var descendant in FindDescendantTextBlocks(child))
                yield return descendant;
        }
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
}
