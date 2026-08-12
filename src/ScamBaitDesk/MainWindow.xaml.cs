using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScamBaitDesk.Services;
using Windows.ApplicationModel.DataTransfer;

namespace ScamBaitDesk;

public sealed partial class MainWindow : Window
{
    private readonly ImapInboxService _inbox = new();
    private readonly ScamAnalysisService _analyzer = new();
    private readonly SettingsService _settings = new();
    private readonly CaseRepository _cases = new();
    private InboxMessage? _selected;
    private AnalysisResult? _analysis;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
    }

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await _settings.LoadAsync();
            if (settings is null) { await ShowMessage("Set up the dedicated inbox first."); return; }
            var password = _settings.LoadPassword(settings.Username);
            if (string.IsNullOrWhiteSpace(password)) { await ShowMessage("The inbox credential is missing. Open Inbox settings."); return; }
            MessageList.ItemsSource = await _inbox.FetchAsync(settings, password);
        }
        catch (Exception ex) { await ShowMessage($"Inbox sync failed: {ex.Message}"); }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var current = await _settings.LoadAsync();
        var host = new TextBox { Header = "IMAP host", Text = current?.Host ?? "imap.gmail.com" };
        var port = new NumberBox { Header = "Port", Value = current?.Port ?? 993, Minimum = 1, Maximum = 65535 };
        var user = new TextBox { Header = "Inbox username", Text = current?.Username ?? string.Empty };
        var password = new PasswordBox { Header = "App password" };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(host); panel.Children.Add(port); panel.Children.Add(user); panel.Children.Add(password);
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Dedicated test inbox", Content = panel, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(host.Text) || string.IsNullOrWhiteSpace(user.Text) || string.IsNullOrWhiteSpace(password.Password)) { await ShowMessage("Host, username, and app password are required."); return; }
        await _settings.SaveAsync(new InboxSettings(host.Text.Trim(), (int)port.Value, user.Text.Trim()), password.Password);
    }

    private void MessageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = MessageList.SelectedItem as InboxMessage;
        if (_selected is null) return;
        _analysis = _analyzer.Analyze($"{_selected.Subject}\n{_selected.Body}");
        SubjectText.Text = _selected.Subject;
        SenderText.Text = $"From {_selected.Sender} · {_selected.ReceivedDisplay}";
        RiskBar.IsOpen = true;
        RiskBar.Title = _analysis.Summary;
        RiskBar.Message = "Automated flags are indicators, not a final determination.";
        RiskBar.Severity = _analysis.Score >= 70 ? InfoBarSeverity.Error : _analysis.Score >= 35 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        SignalList.ItemsSource = _analysis.Signals;
        MessageBody.Text = _analysis.RedactedText;
        DraftBox.Text = ScamAnalysisService.CreateSafeDraft(_selected);
        NotesBox.Text = string.Empty;
    }

    private async void SaveCase_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _analysis is null) { await ShowMessage("Select a message before saving a case."); return; }
        await _cases.SaveAsync(new CaseRecord(Guid.NewGuid(), DateTimeOffset.Now, _selected, _analysis, DraftBox.Text, NotesBox.Text));
        await ShowMessage("Case saved locally.");
    }

    private void CopyDraft_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(DraftBox.Text);
        Clipboard.SetContent(package);
    }

    private async Task ShowMessage(string message) => await new ContentDialog
    {
        XamlRoot = Content.XamlRoot, Title = "ScamBait Desk", Content = message, CloseButtonText = "OK"
    }.ShowAsync();
}
