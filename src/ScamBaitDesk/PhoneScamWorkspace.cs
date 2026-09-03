using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace ScamBaitDesk;

public sealed partial class MainWindow
{
    private readonly Services.PhoneScamAnalysisService _phoneScamAnalysis = new();
    private bool _phoneWorkspaceHooked;
    private TabViewItem? _phoneAnalysisTab;
    private TextBox? _phoneNumberInput;
    private TextBox? _phoneTranscriptInput;
    private InfoBar? _phoneAnalysisBar;
    private TextBlock? _phoneGuidance;
    private StackPanel? _phoneFindingsPanel;

    private bool RegisterPhoneWorkspaceHook()
    {
        Loaded += (_, _) => InitializePhoneWorkspace();
        return true;
    }

    private readonly bool _phoneWorkspaceHook = false;

    private void InitializePhoneWorkspace()
    {
        if (_phoneWorkspaceHooked) return;
        _phoneWorkspaceHooked = true;

        AddPhoneCommandButton();
        AddPhoneAnalysisPanel();
        AddHomeScamSourceCard();
    }

    private void AddPhoneCommandButton()
    {
        if (Content is not Grid root) return;
        var commandBar = root.Children.OfType<CommandBar>().FirstOrDefault();
        if (commandBar is null || commandBar.PrimaryCommands.OfType<AppBarButton>().Any(button => button.Label == "Phone scam check")) return;

        var button = new AppBarButton { Label = "Phone scam check", Icon = new FontIcon { Glyph = "\uE717" } };
        button.Click += (_, _) => OpenPhoneWorkspace();
        commandBar.PrimaryCommands.Add(button);
    }

    private void AddPhoneAnalysisPanel()
    {
        if (CallsTab is null || CallsTab.Content is not ScrollViewer scroll || scroll.Content is not StackPanel stack) return;

        CallsTab.Header = "Phone scams";
        _phoneAnalysisTab = CallsTab;

        var card = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Phone scam analysis", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        panel.Children.Add(new TextBlock { Text = "Paste a suspicious call transcript, add the number if you have it, and ScamBait Desk will score common social-engineering indicators locally. It never calls the number or records the conversation.", TextWrapping = TextWrapping.Wrap, Opacity = 0.72 });

        _phoneNumberInput = new TextBox { Header = "Caller number (optional)", PlaceholderText = "+44 20 1234 5678", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
        panel.Children.Add(_phoneNumberInput);

        _phoneTranscriptInput = new TextBox
        {
            Header = "Call transcript or your redacted notes",
            PlaceholderText = "Example: They said my account was compromised and I had to move money to a safe account immediately...",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 180
        };
        panel.Children.Add(_phoneTranscriptInput);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var analyze = new Button { Content = "Analyse phone scam", HorizontalAlignment = HorizontalAlignment.Left };
        analyze.Click += PhoneAnalyze_Click;
        buttons.Children.Add(analyze);
        var clear = new Button { Content = "Clear" };
        clear.Click += (_, _) => ClearPhoneAnalysis();
        buttons.Children.Add(clear);
        var copy = new Button { Content = "Copy findings" };
        copy.Click += CopyPhoneFindings_Click;
        buttons.Children.Add(copy);
        panel.Children.Add(buttons);

        _phoneAnalysisBar = new InfoBar { IsOpen = false, IsClosable = false };
        panel.Children.Add(_phoneAnalysisBar);
        _phoneGuidance = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        panel.Children.Add(_phoneGuidance);
        panel.Children.Add(new TextBlock { Text = "Signals found", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        _phoneFindingsPanel = new StackPanel { Spacing = 8 };
        panel.Children.Add(_phoneFindingsPanel);

        card.Child = panel;
        stack.Children.Insert(0, card);
    }

    private void AddHomeScamSourceCard()
    {
        if (GuideTab?.Content is not ScrollViewer scroll || scroll.Content is not StackPanel stack) return;
        var sourceCard = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 4)
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "One workspace · three scam channels", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "ScamBait Desk is not just an email tool. Use the same investigation workflow for email, phone calls, and suspicious websites.", TextWrapping = TextWrapping.Wrap, Opacity = 0.72 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var email = new Button { Content = "Email scams" };
        email.Click += (_, _) => { SelectShellDestination("Inbox"); NavigateShell("Inbox"); };
        var phone = new Button { Content = "Phone scams" };
        phone.Click += (_, _) => OpenPhoneWorkspace();
        var website = new Button { Content = "Website scams" };
        website.Click += (_, _) => { SelectShellDestination("Website"); NavigateShell("Website"); };
        buttons.Children.Add(email); buttons.Children.Add(phone); buttons.Children.Add(website);
        panel.Children.Add(buttons);
        sourceCard.Child = panel;
        stack.Children.Insert(0, sourceCard);
    }

    private void OpenPhoneWorkspace()
    {
        if (_phoneAnalysisTab is null) return;
        foreach (var tab in new[] { GuideTab, ReviewTab, ReplyTab, CallsTab, NotesTab, TimelineTab, WebsiteTab, HeadersTab, IndicatorsTab, ReportTab, ToolsTab, PlanTab, InsightTab, SettingsTab })
            tab.Visibility = tab == _phoneAnalysisTab ? Visibility.Visible : Visibility.Collapsed;
        CollectionPane.Visibility = Visibility.Collapsed;
        CollectionColumn.Width = new GridLength(0);
        WorkspaceTabs.SelectedItem = _phoneAnalysisTab;
        SelectShellDestination("Engage");
    }

    private void PhoneAnalyze_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = _phoneScamAnalysis.Analyze(_phoneNumberInput?.Text ?? string.Empty, _phoneTranscriptInput?.Text ?? string.Empty);
            _phoneAnalysisBar!.IsOpen = true;
            _phoneAnalysisBar.Title = result.Summary;
            _phoneAnalysisBar.Message = "This is a local indicator score, not a definitive identification of the caller.";
            _phoneAnalysisBar.Severity = result.Score >= 60 ? InfoBarSeverity.Error : result.Score >= 30 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
            _phoneGuidance!.Text = result.Guidance;
            _phoneFindingsPanel!.Children.Clear();
            foreach (var finding in result.Findings)
            {
                var border = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10)
                };
                var findingPanel = new StackPanel { Spacing = 4 };
                findingPanel.Children.Add(new TextBlock { Text = finding.Label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 15 });
                findingPanel.Children.Add(new TextBlock { Text = finding.SeverityDisplay, FontSize = 12, Opacity = 0.7 });
                findingPanel.Children.Add(new TextBlock { Text = finding.Detail, TextWrapping = TextWrapping.Wrap });
                findingPanel.Children.Add(new TextBlock { Text = finding.EvidenceDisplay, TextWrapping = TextWrapping.Wrap, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12, Opacity = 0.78 });
                findingPanel.Children.Add(new TextBlock { Text = finding.RecommendationDisplay, TextWrapping = TextWrapping.Wrap, Opacity = 0.82 });
                border.Child = findingPanel;
                _phoneFindingsPanel.Children.Add(border);
            }
        }
        catch (Exception ex)
        {
            _phoneAnalysisBar!.IsOpen = true;
            _phoneAnalysisBar.Title = "Phone analysis could not run";
            _phoneAnalysisBar.Message = ex.Message;
            _phoneAnalysisBar.Severity = InfoBarSeverity.Error;
        }
    }

    private void ClearPhoneAnalysis()
    {
        if (_phoneNumberInput is not null) _phoneNumberInput.Text = string.Empty;
        if (_phoneTranscriptInput is not null) _phoneTranscriptInput.Text = string.Empty;
        if (_phoneAnalysisBar is not null) _phoneAnalysisBar.IsOpen = false;
        if (_phoneGuidance is not null) _phoneGuidance.Text = string.Empty;
        _phoneFindingsPanel?.Children.Clear();
    }

    private void CopyPhoneFindings_Click(object sender, RoutedEventArgs e)
    {
        if (_phoneAnalysisBar is null || !_phoneAnalysisBar.IsOpen || _phoneFindingsPanel is null) return;
        var lines = new List<string> { _phoneAnalysisBar.Title ?? string.Empty, _phoneGuidance?.Text ?? string.Empty };
        foreach (var border in _phoneFindingsPanel.Children.OfType<Border>())
        {
            if (border.Child is StackPanel panel)
                lines.Add(string.Join(" | ", panel.Children.OfType<TextBlock>().Select(text => text.Text)));
        }
        var package = new DataPackage();
        package.SetText(string.Join(Environment.NewLine + Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line))));
        Clipboard.SetContent(package);
    }
}
