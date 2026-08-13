using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScamBaitDesk.Services;
using Windows.ApplicationModel.DataTransfer;
using MimeKit;

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
    private readonly EngagementSafetyService _engagementSafety = new();
    private readonly SmtpEngagementService _smtp = new();
    private readonly EngagementWorkspaceService _workspace = new();
    private readonly CaseIntelligenceService _intelligence = new();
    private readonly SafetyStateService _safetyState = new();
    private InboxMessage? _selected;
    private AnalysisResult? _analysis;
    private CaseRecord? _currentCase;
    private List<InboxMessage> _messages = [];
    private List<CaseRecord> _caseRecords = [];
    private List<PersonaProfile> _personas = [];
    private bool _globalEmergencyStop;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
        StatusBox.SelectedIndex = 0;
        ShowForensics(_selected);
        ShowIndicators([]);
        ReviewDraft();
        TemplateBox.ItemsSource = _workspace.Templates;
        TemplateBox.SelectedIndex = 0;
        _ = LoadPersonasAsync();
        _ = LoadSafetyStateAsync();
        _ = LoadCasesAsync();
    }

    private async Task LoadSafetyStateAsync()
    {
        _globalEmergencyStop = await _safetyState.IsEmergencyStopEnabledAsync();
        EmergencyStopButton.IsChecked = _globalEmergencyStop;
        ReviewDraft();
    }

    private async Task LoadPersonasAsync()
    {
        _personas = (await _workspace.LoadPersonasAsync()).ToList();
        PersonaBox.ItemsSource = _personas;
        if (_currentCase?.PersonaId is Guid personaId)
            PersonaBox.SelectedItem = _personas.FirstOrDefault(item => item.Id == personaId);
    }

    private async Task LoadCasesAsync()
    {
        _caseRecords = (await _cases.LoadAsync()).ToList();
        ApplyFilter();
        RefreshDashboard();
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
        var smtpHost = new TextBox { Header = "SMTP host", Text = current?.SmtpHost ?? "smtp.gmail.com" };
        var smtpPort = new NumberBox { Header = "SMTP port", Value = current?.SmtpPort ?? 587, Minimum = 1, Maximum = 65535 };
        var smtpSsl = new CheckBox { Content = "SMTP uses implicit TLS (usually port 465)", IsChecked = current?.SmtpUseSsl ?? false };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(host); panel.Children.Add(port); panel.Children.Add(smtpHost); panel.Children.Add(smtpPort); panel.Children.Add(smtpSsl); panel.Children.Add(user); panel.Children.Add(password);
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Dedicated test inbox", Content = panel, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(host.Text) || string.IsNullOrWhiteSpace(smtpHost.Text) || string.IsNullOrWhiteSpace(user.Text) || string.IsNullOrWhiteSpace(password.Password)) { await ShowMessage("IMAP host, SMTP host, username, and app password are required."); return; }
        await _settings.SaveAsync(new InboxSettings(host.Text.Trim(), (int)port.Value, user.Text.Trim(), smtpHost.Text.Trim(), (int)smtpPort.Value, smtpSsl.IsChecked == true), password.Password);
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
        RecipientText.Text = TryGetSenderAddress(_selected.Sender, out var recipient) ? $"Locked recipient: {recipient}" : "Sender address could not be parsed";
        NotesBox.Text = string.Empty;
        _currentCase = null;
        PersonaBox.SelectedItem = null;
        OutboundList.ItemsSource = null;
        ShowStoppedState();
        TimelineList.ItemsSource = null;
        StatusBox.SelectedIndex = 0;
        ShowForensics(_selected);
        ShowIndicators(ConversationService.FindConversation(_selected, _messages));
        AttachmentList.ItemsSource = _selected.Attachments;
        DuplicateList.ItemsSource = null;
        ReminderList.ItemsSource = null;
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
        _currentCase.PersonaId = (PersonaBox.SelectedItem as PersonaProfile)?.Id;
        _currentCase.UpdatedAt = now;
        _currentCase.Timeline.Add(new CaseEvent(now, "Saved", $"Case saved with {_currentCase.Messages.Count} message(s)."));
        await _cases.SaveAsync(_currentCase);
        await LoadCasesAsync();
        ShowStoppedState();
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
        RecipientText.Text = _selected is not null && TryGetSenderAddress(_selected.Sender, out var recipient) ? $"Locked recipient: {recipient}" : "Sender address could not be parsed";
        OutboundList.ItemsSource = _currentCase.OutboundMessages.OrderByDescending(item => item.SentAt);
        PersonaBox.SelectedItem = _personas.FirstOrDefault(item => item.Id == _currentCase.PersonaId);
        ShowStoppedState();
        NotesBox.Text = _currentCase.Notes;
        SignalList.ItemsSource = _analysis?.Signals;
        TimelineList.ItemsSource = _currentCase.Timeline.OrderByDescending(item => item.At);
        StatusBox.SelectedIndex = IndexFromStatus(_currentCase.Status);
        ShowForensics(_selected);
        ShowIndicators(_currentCase.Messages);
        RefreshCaseTools();
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

    private void DraftBox_TextChanged(object sender, TextChangedEventArgs e) => ReviewDraft();

    private void ReviewDraft()
    {
        if (PrivacyBar is null || DraftBox is null || PrivacyFindings is null || SendButton is null) return;
        var review = _engagementSafety.Review(DraftBox.Text ?? string.Empty);
        PrivacyBar.Title = review.CanSend ? "Privacy guard passed" : "Privacy guard blocked sending";
        PrivacyBar.Message = review.Summary;
        PrivacyBar.Severity = !review.CanSend ? InfoBarSeverity.Error : review.Findings.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        PrivacyFindings.ItemsSource = review.Findings;
        SendButton.IsEnabled = review.CanSend && !string.IsNullOrWhiteSpace(DraftBox.Text) && _selected is not null && _currentCase?.EngagementStopped != true && !_globalEmergencyStop;
    }

    private async void SendReply_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !TryGetSenderAddress(_selected.Sender, out var recipient)) { await ShowMessage("The selected sender address could not be parsed."); return; }
        var review = _engagementSafety.Review(DraftBox.Text);
        if (!review.CanSend) { await ShowMessage("Sending is blocked until all privacy-guard issues are removed."); return; }
        if (_currentCase is null) { await ShowMessage("Save or open the case before sending so the engagement has an audit trail."); return; }
        if (_currentCase.EngagementStopped) { await ShowMessage("This engagement was permanently stopped. Sending is disabled for this case."); return; }
        if (_globalEmergencyStop) { await ShowMessage("The global emergency stop is enabled. Disable it before any outbound message can be sent."); return; }

        var recent = _currentCase.OutboundMessages.Where(item => item.SentAt >= DateTimeOffset.Now.AddHours(-1)).OrderByDescending(item => item.SentAt).ToList();
        if (recent.Count >= 5) { await ShowMessage("Rate limit reached: no more than five messages per case per hour."); return; }
        if (recent.FirstOrDefault() is { } last && last.SentAt >= DateTimeOffset.Now.AddMinutes(-2)) { await ShowMessage("Please wait at least two minutes between outbound messages in this case."); return; }

        var dedicated = new CheckBox { Content = new TextBlock { Text = "I confirm this is a dedicated bait account and the persona/details are fictional.", TextWrapping = TextWrapping.Wrap } };
        var noHarm = new CheckBox { Content = new TextBlock { Text = "I confirm this message contains no threats, authority impersonation, credential collection, tracking, malware, or real secrets.", TextWrapping = TextWrapping.Wrap } };
        var summary = new TextBlock { Text = $"To: {recipient}\nSubject: Re: {_selected.Subject}\n\nThis sends one plain-text message. Attachments and automatic follow-ups are not supported.", TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(summary); panel.Children.Add(dedicated); panel.Children.Add(noHarm);
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Final manual-send confirmation", Content = panel, PrimaryButtonText = "Send one message", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (dedicated.IsChecked != true || noHarm.IsChecked != true) { await ShowMessage("Both safety confirmations are required. Nothing was sent."); return; }

        try
        {
            SendButton.IsEnabled = false;
            var settings = await _settings.LoadAsync();
            if (settings is null) { await ShowMessage("Configure the dedicated mail account first."); return; }
            var password = _settings.LoadPassword(settings.Username);
            if (string.IsNullOrWhiteSpace(password)) { await ShowMessage("The mail credential is missing. Open Inbox settings."); return; }
            var subject = _selected.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? _selected.Subject : $"Re: {_selected.Subject}";
            var messageId = await _smtp.SendAsync(settings, password, recipient, subject, DraftBox.Text);
            var now = DateTimeOffset.Now;
            _currentCase.DraftReply = DraftBox.Text;
            _currentCase.OutboundMessages.Add(new OutboundMessageRecord(now, recipient, subject, ScamAnalysisService.Redact(DraftBox.Text), messageId));
            _currentCase.UpdatedAt = now;
            _currentCase.Timeline.Add(new CaseEvent(now, "Manual outbound", $"Sent one plain-text message to {recipient}. Message-ID: {messageId}."));
            await _cases.SaveAsync(_currentCase);
            OutboundList.ItemsSource = _currentCase.OutboundMessages.OrderByDescending(item => item.SentAt);
            TimelineList.ItemsSource = _currentCase.Timeline.OrderByDescending(item => item.At);
            await ShowMessage("One message was sent and recorded in the case audit trail.");
        }
        catch (Exception ex) { await ShowMessage($"Nothing was recorded as sent. SMTP failed: {ex.Message}"); }
        finally { ReviewDraft(); }
    }

    private static bool TryGetSenderAddress(string sender, out string address)
    {
        if (MailboxAddress.TryParse(sender, out var mailbox)) { address = mailbox.Address; return true; }
        address = string.Empty;
        return false;
    }

    private async void Personas_Click(object sender, RoutedEventArgs e)
    {
        var existing = new ComboBox { Header = "Edit existing or create new", ItemsSource = _personas, DisplayMemberPath = "Display", PlaceholderText = "New persona" };
        var name = new TextBox { Header = "Fictional name" };
        var timezone = new TextBox { Header = "Time zone", Text = "Europe/London" };
        var backstory = new TextBox { Header = "Fictional backstory", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var details = new TextBox { Header = "Safe fictional details", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, PlaceholderText = "Never enter real addresses, phone numbers, credentials, or financial data." };
        existing.SelectionChanged += (_, _) =>
        {
            if (existing.SelectedItem is not PersonaProfile item) return;
            name.Text = item.Name; timezone.Text = item.TimeZone; backstory.Text = item.Backstory; details.Text = item.SafeDetails;
        };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(existing); panel.Children.Add(name); panel.Children.Add(timezone); panel.Children.Add(backstory); panel.Children.Add(details);
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Fictional persona manager", Content = panel, PrimaryButtonText = "Save persona", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(name.Text)) { await ShowMessage("Enter a fictional persona name."); return; }
        var combined = $"{name.Text}\n{backstory.Text}\n{details.Text}";
        var review = _engagementSafety.Review(combined);
        if (!review.CanSend) { await ShowMessage("The persona contains high-risk personal or secret-like data. Remove it before saving."); return; }
        var persona = existing.SelectedItem as PersonaProfile ?? new PersonaProfile();
        persona.Name = name.Text.Trim(); persona.TimeZone = timezone.Text.Trim(); persona.Backstory = backstory.Text.Trim(); persona.SafeDetails = details.Text.Trim();
        await _workspace.SavePersonaAsync(persona);
        await LoadPersonasAsync();
        PersonaBox.SelectedItem = _personas.FirstOrDefault(item => item.Id == persona.Id);
    }

    private async void PersonaBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_currentCase is null) return;
        _currentCase.PersonaId = (PersonaBox.SelectedItem as PersonaProfile)?.Id;
        _currentCase.UpdatedAt = DateTimeOffset.Now;
        _currentCase.Timeline.Add(new CaseEvent(_currentCase.UpdatedAt, "Persona", PersonaBox.SelectedItem is PersonaProfile persona ? $"Assigned fictional persona {persona.Name}." : "Removed persona assignment."));
        await _cases.SaveAsync(_currentCase);
    }

    private void ApplyTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateBox.SelectedItem is not ReplyTemplate template) return;
        var persona = PersonaBox.SelectedItem as PersonaProfile;
        var signoff = persona is null ? string.Empty : $"\n\n{persona.Name}";
        DraftBox.Text = template.Body + signoff;
    }

    private async void StopEngagement_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null) { await ShowMessage("Open or save a case before stopping an engagement."); return; }
        if (_currentCase.EngagementStopped) { await ShowMessage("This engagement is already permanently stopped."); return; }
        var reason = new TextBox { Header = "Reason", Text = "Safety decision", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Permanently stop this engagement?", Content = reason, PrimaryButtonText = "Stop permanently", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var now = DateTimeOffset.Now;
        _currentCase.EngagementStopped = true;
        _currentCase.EngagementStoppedAt = now;
        _currentCase.EngagementStopReason = string.IsNullOrWhiteSpace(reason.Text) ? "Safety decision" : reason.Text.Trim();
        _currentCase.UpdatedAt = now;
        _currentCase.Timeline.Add(new CaseEvent(now, "Engagement stopped", _currentCase.EngagementStopReason));
        await _cases.SaveAsync(_currentCase);
        ShowStoppedState();
        ReviewDraft();
    }

    private void ShowStoppedState()
    {
        if (StoppedBar is null || StopEngagementButton is null) return;
        var stopped = _currentCase?.EngagementStopped == true;
        StoppedBar.IsOpen = stopped;
        StoppedBar.Message = stopped ? $"Stopped {_currentCase!.EngagementStoppedAt?.LocalDateTime:g}: {_currentCase.EngagementStopReason}" : string.Empty;
        StopEngagementButton.IsEnabled = _currentCase is not null && !stopped;
    }

    private void GenerateReport_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null) { ReportBox.Text = "Open or save a case before generating a report."; return; }
        ReportBox.Text = EngagementWorkspaceService.BuildReport(_currentCase, ReportDestinationBox.SelectedItem?.ToString() ?? "Reporting service");
    }

    private void CopyReport_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ReportBox.Text)) return;
        var package = new DataPackage(); package.SetText(ReportBox.Text); Clipboard.SetContent(package);
    }

    private void RefreshDashboard()
    {
        if (DashboardList is null || PendingList is null) return;
        DashboardList.ItemsSource = _intelligence.Dashboard(_caseRecords);
        PendingList.ItemsSource = _caseRecords.Where(record =>
            record.Status != CaseStatus.Closed || record.Reminders.Any(reminder => !reminder.Completed && reminder.DueAt <= DateTimeOffset.Now))
            .OrderByDescending(record => record.Reminders.Any(reminder => !reminder.Completed && reminder.DueAt <= DateTimeOffset.Now))
            .ThenByDescending(record => record.Analysis?.Score ?? 0).ToList();
    }

    private void RefreshCaseTools()
    {
        if (_currentCase is null) return;
        AttachmentList.ItemsSource = _currentCase.Messages.SelectMany(message => message.Attachments).ToList();
        DuplicateList.ItemsSource = _intelligence.FindDuplicates(_currentCase, _caseRecords);
        ReminderList.ItemsSource = _currentCase.Reminders.OrderBy(reminder => reminder.Completed).ThenBy(reminder => reminder.DueAt).ToList();
    }

    private async void EmergencyStop_Click(object sender, RoutedEventArgs e)
    {
        var requested = EmergencyStopButton.IsChecked == true;
        if (requested)
        {
            var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Enable global send stop?", Content = "This immediately disables outbound sending across every case. Inbox review, evidence, reports, and drafts remain available.", PrimaryButtonText = "Enable stop", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) { EmergencyStopButton.IsChecked = false; return; }
        }
        else if (_globalEmergencyStop)
        {
            var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Re-enable outbound sending?", Content = "Manual confirmations, privacy checks, rate limits, and per-case stops will still apply.", PrimaryButtonText = "Re-enable", CloseButtonText = "Keep stopped", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) { EmergencyStopButton.IsChecked = true; return; }
        }
        _globalEmergencyStop = requested;
        await _safetyState.SetEmergencyStopAsync(requested);
        ReviewDraft();
    }

    private async void AddReminder_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null) { await ShowMessage("Open or save a case first."); return; }
        var date = new DatePicker { Header = "Date", Date = DateTimeOffset.Now.AddDays(1) };
        var time = new TimePicker { Header = "Time", Time = new TimeSpan(10, 0, 0) };
        var note = new TextBox { Header = "Reminder note", Text = "Review for a reply" };
        var panel = new StackPanel { Spacing = 10 }; panel.Children.Add(date); panel.Children.Add(time); panel.Children.Add(note);
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Manual follow-up reminder", Content = panel, PrimaryButtonText = "Add", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var local = date.Date.Date + time.Time;
        var due = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        _currentCase.Reminders.Add(new FollowUpReminder(Guid.NewGuid(), due, string.IsNullOrWhiteSpace(note.Text) ? "Review case" : note.Text.Trim(), false));
        _currentCase.UpdatedAt = DateTimeOffset.Now;
        _currentCase.Timeline.Add(new CaseEvent(_currentCase.UpdatedAt, "Reminder", $"Manual follow-up scheduled for {due.LocalDateTime:g}. No message will be sent automatically."));
        await _cases.SaveAsync(_currentCase); RefreshCaseTools(); await LoadCasesAsync();
    }

    private async void CompleteReminder_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null || ReminderList.SelectedItem is not FollowUpReminder selected) { await ShowMessage("Select a reminder first."); return; }
        var index = _currentCase.Reminders.FindIndex(item => item.Id == selected.Id);
        if (index < 0) return;
        _currentCase.Reminders[index] = selected with { Completed = true };
        _currentCase.UpdatedAt = DateTimeOffset.Now;
        _currentCase.Timeline.Add(new CaseEvent(_currentCase.UpdatedAt, "Reminder completed", selected.Note));
        await _cases.SaveAsync(_currentCase); RefreshCaseTools(); await LoadCasesAsync();
    }

    private async void ExportSummary_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null) { await ShowMessage("Open or save a case first."); return; }
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary, SuggestedFileName = $"ScamBait-summary-{_currentCase.Id:N}" };
        picker.FileTypeChoices.Add("Text summary", new List<string> { ".txt" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync(); if (file is null) return;
        var summary = EngagementWorkspaceService.BuildReport(_currentCase, "Case summary");
        await Windows.Storage.FileIO.WriteTextAsync(file, summary);
        _currentCase.UpdatedAt = DateTimeOffset.Now;
        _currentCase.Timeline.Add(new CaseEvent(_currentCase.UpdatedAt, "Summary export", "Exported a lightweight redacted text summary."));
        await _cases.SaveAsync(_currentCase);
    }

    private async Task ShowMessage(string message) => await new ContentDialog
    {
        XamlRoot = Content.XamlRoot, Title = "ScamBait Desk", Content = message, CloseButtonText = "OK"
    }.ShowAsync();
}
