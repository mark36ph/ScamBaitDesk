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
    private readonly EmailForensicsService _forensics = new();
    private readonly IndicatorExtractionService _indicatorExtractor = new();
    private readonly EvidenceExportService _evidenceExporter = new();
    private InboxMessage? _selected;
    private AnalysisResult? _analysis;
    private CaseRecord? _currentCase;
    private List<InboxMessage> _messages = [];
    private List<CaseRecord> _caseRecords = [];

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
        StatusBox.SelectedIndex = 0;
        ShowForensics(_selected);
        ShowIndicators([]);
        _ = LoadCasesAsync();
    }

    private async Task LoadCasesAsync()
    {
        _caseRecords = (await _cases.LoadAsync()).ToList();
        ApplyFilter();
    }

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await _settings.LoadAsync();
            if (settings is null) { await ShowMessage("Set up the dedicated inbox first."); return; }
            var password = _settings.LoadPassword(settings.Username);
            if (string.IsNullOrWhiteSpace(password)) { await ShowMessage("The inbox credential is missing. Open Inbox settings."); return; }
            _messages = (await _inbox.FetchAsync(settings, password)).ToList();
            ApplyFilter();
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
        _currentCase = null;
        TimelineList.ItemsSource = null;
        StatusBox.SelectedIndex = 0;
        ShowForensics(_selected);
        ShowIndicators(ConversationService.FindConversation(_selected, _messages));
    }

    private void ShowIndicators(IEnumerable<InboxMessage> messages) =>
        IndicatorList.ItemsSource = _indicatorExtractor.Extract(messages);

    private void CopyIndicator_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not IndicatorRecord indicator) return;
        var package = new DataPackage();
        package.SetText(indicator.Value);
        Clipboard.SetContent(package);
    }

    private async void LookupIndicator_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not IndicatorRecord indicator || !indicator.SupportsLookup) return;
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "External reputation lookup", Content = $"Open VirusTotal and disclose only this {indicator.TypeDisplay.ToLowerInvariant()}?\n\n{indicator.Value}\n\nNo message text, notes, or other indicators will be sent.", PrimaryButtonText = "Open lookup", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var route = indicator.Type switch { IndicatorType.Domain => "domain", IndicatorType.IpAddress => "ip-address", _ => "search" };
        await Windows.System.Launcher.LaunchUriAsync(new Uri($"https://www.virustotal.com/gui/{route}/{Uri.EscapeDataString(indicator.Value)}"));
        if (_currentCase is null) return;
        _currentCase.UpdatedAt = DateTimeOffset.Now;
        _currentCase.Timeline.Add(new CaseEvent(_currentCase.UpdatedAt, "Indicator lookup", $"User approved {indicator.TypeDisplay} lookup for {indicator.Value}."));
        await _cases.SaveAsync(_currentCase);
        TimelineList.ItemsSource = _currentCase.Timeline.OrderByDescending(item => item.At);
    }

    private async void SaveCase_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _analysis is null) { await ShowMessage("Select a message before saving a case."); return; }
        var now = DateTimeOffset.Now;
        _currentCase ??= new CaseRecord
        {
            Title = _selected.Subject,
            CreatedAt = now,
            Messages = ConversationService.FindConversation(_selected, _messages).ToList(),
            Timeline = [new CaseEvent(now, "Created", "Case created from the dedicated inbox.")]
        };
        _currentCase.Analysis = _analysis;
        _currentCase.DraftReply = DraftBox.Text;
        _currentCase.Notes = NotesBox.Text;
        _currentCase.Status = StatusFromIndex(StatusBox.SelectedIndex);
        _currentCase.UpdatedAt = now;
        _currentCase.Timeline.Add(new CaseEvent(now, "Saved", $"Case saved with {_currentCase.Messages.Count} message(s)."));
        await _cases.SaveAsync(_currentCase);
        await LoadCasesAsync();
        TimelineList.ItemsSource = _currentCase.Timeline.OrderByDescending(item => item.At);
        await ShowMessage("Case saved locally.");
    }

    private async void ExportEvidence_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null) { await ShowMessage("Open or save a case before exporting evidence."); return; }
        var warning = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Export redacted evidence", Content = "The ZIP will contain redacted message bodies, notes, and drafts. Original mail headers are preserved for forensic value and may contain personal data. Store the export securely.", PrimaryButtonText = "Choose destination", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await warning.ShowAsync() != ContentDialogResult.Primary) return;

        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"ScamBait-{_currentCase.Id:N}"
        };
        picker.FileTypeChoices.Add("ZIP evidence package", new List<string> { ".zip" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        try
        {
            await using var stream = await file.OpenStreamForWriteAsync();
            stream.SetLength(0);
            var result = _evidenceExporter.Export(_currentCase, stream);
            _currentCase.UpdatedAt = DateTimeOffset.Now;
            _currentCase.Timeline.Add(new CaseEvent(_currentCase.UpdatedAt, "Evidence export", $"Created redacted package with {result.EvidenceFileCount} hashed evidence files. Manifest SHA-256: {result.ManifestSha256}."));
            await _cases.SaveAsync(_currentCase);
            TimelineList.ItemsSource = _currentCase.Timeline.OrderByDescending(item => item.At);
            await ShowMessage($"Evidence package saved.\n\nManifest SHA-256:\n{result.ManifestSha256}");
        }
        catch (Exception ex) { await ShowMessage($"Evidence export failed: {ex.Message}"); }
    }

    private void CaseList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _currentCase = CaseList.SelectedItem as CaseRecord;
        if (_currentCase is null) return;
        _selected = _currentCase.Messages.OrderByDescending(message => message.ReceivedAt).FirstOrDefault();
        _analysis = _currentCase.Analysis;
        SubjectText.Text = _currentCase.Title;
        SenderText.Text = $"{_currentCase.Messages.Count} message(s) · created {_currentCase.CreatedAt.LocalDateTime:g}";
        MessageBody.Text = string.Join("\n\n──────────\n\n", _currentCase.Messages.OrderBy(message => message.ReceivedAt).Select(message => $"{message.ReceivedDisplay} · {message.Sender}\n{ScamAnalysisService.Redact(message.Body)}"));
        DraftBox.Text = _currentCase.DraftReply;
        NotesBox.Text = _currentCase.Notes;
        SignalList.ItemsSource = _analysis?.Signals;
        TimelineList.ItemsSource = _currentCase.Timeline.OrderByDescending(item => item.At);
        StatusBox.SelectedIndex = IndexFromStatus(_currentCase.Status);
        ShowForensics(_selected);
        ShowIndicators(_currentCase.Messages);
    }

    private void ShowForensics(InboxMessage? message)
    {
        if (message is null) { ForensicsBar.IsOpen = false; ForensicsList.ItemsSource = null; ForensicsWarnings.ItemsSource = null; RawHeadersBox.Text = string.Empty; return; }
        var report = _forensics.Analyze(message);
        ForensicsBar.IsOpen = true;
        ForensicsBar.Title = report.Summary;
        ForensicsBar.Message = "Header results are evidence indicators, not proof that a sender is safe or malicious.";
        ForensicsBar.Severity = report.Warnings.Count == 0 ? InfoBarSeverity.Success : report.Warnings.Count <= 2 ? InfoBarSeverity.Warning : InfoBarSeverity.Error;
        ForensicsList.ItemsSource = report.Findings;
        ForensicsWarnings.ItemsSource = report.Warnings;
        RawHeadersBox.Text = report.RawHeaders;
    }

    private async void StatusBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_currentCase is null || StatusBox.SelectedIndex < 0) return;
        var next = StatusFromIndex(StatusBox.SelectedIndex);
        if (next == _currentCase.Status) return;
        _currentCase.Status = next;
        _currentCase.UpdatedAt = DateTimeOffset.Now;
        _currentCase.Timeline.Add(new CaseEvent(_currentCase.UpdatedAt, "Status", $"Changed to {_currentCase.StatusDisplay}."));
        await _cases.SaveAsync(_currentCase);
        await LoadCasesAsync();
        TimelineList.ItemsSource = _currentCase.Timeline.OrderByDescending(item => item.At);
    }

    private async void Reputation_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !TryGetDomain(_selected.Sender, out var domain)) { await ShowMessage("No sender domain was found."); return; }
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "External reputation lookup", Content = $"Open VirusTotal and disclose only this domain?\n\n{domain}\n\nNo message text or case notes will be sent.", PrimaryButtonText = "Open lookup", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await Windows.System.Launcher.LaunchUriAsync(new Uri($"https://www.virustotal.com/gui/domain/{Uri.EscapeDataString(domain)}"));
        if (_currentCase is not null)
        {
            _currentCase.UpdatedAt = DateTimeOffset.Now;
            _currentCase.Timeline.Add(new CaseEvent(_currentCase.UpdatedAt, "Reputation lookup", $"User approved lookup for {domain}."));
            await _cases.SaveAsync(_currentCase);
            TimelineList.ItemsSource = _currentCase.Timeline.OrderByDescending(item => item.At);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        MessageList.ItemsSource = _messages.Where(message => string.IsNullOrEmpty(query) || message.Subject.Contains(query, StringComparison.OrdinalIgnoreCase) || message.Sender.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        CaseList.ItemsSource = _caseRecords.Where(record => string.IsNullOrEmpty(query) || record.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || record.Notes.Contains(query, StringComparison.OrdinalIgnoreCase) || record.StatusDisplay.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static bool TryGetDomain(string sender, out string domain)
    {
        var match = System.Text.RegularExpressions.Regex.Match(sender, @"@([A-Z0-9.-]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        domain = match.Success ? match.Groups[1].Value.TrimEnd('.').ToLowerInvariant() : string.Empty;
        return domain.Length > 3;
    }

    private static CaseStatus StatusFromIndex(int index) => index switch { 1 => CaseStatus.Investigating, 2 => CaseStatus.AwaitingVerification, 3 => CaseStatus.Reported, 4 => CaseStatus.Closed, _ => CaseStatus.New };
    private static int IndexFromStatus(CaseStatus status) => status switch { CaseStatus.Investigating => 1, CaseStatus.AwaitingVerification => 2, CaseStatus.Reported => 3, CaseStatus.Closed => 4, _ => 0 };

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
