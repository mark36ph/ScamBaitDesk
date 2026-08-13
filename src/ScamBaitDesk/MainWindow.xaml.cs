using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScamBaitDesk.Services;
using Windows.ApplicationModel.DataTransfer;
using MimeKit;
using System.Runtime.InteropServices;
using System.Text.Json;

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
    private readonly EvidenceVerificationService _evidenceVerifier = new();
    private readonly WebsiteSafetyService _websiteSafety = new();
    private readonly EngagementSafetyService _engagementSafety = new();
    private readonly SmtpEngagementService _smtp = new();
    private readonly EngagementWorkspaceService _workspace = new();
    private readonly CaseIntelligenceService _intelligence = new();
    private readonly SafetyStateService _safetyState = new();
    private readonly GoogleOAuthService _googleOAuth = new();
    private readonly DraftRecoveryService _draftRecovery = new();
    private readonly ConversationSummaryService _conversationSummary = new();
    private readonly MailDiagnosticService _mailDiagnostic = new();
    private readonly AppUpdateService _appUpdate = new();
    private readonly VpnIntegrationService _vpn = new();
    private InboxMessage? _selected;
    private AnalysisResult? _analysis;
    private CaseRecord? _currentCase;
    private List<InboxMessage> _messages = [];
    private List<CaseRecord> _caseRecords = [];
    private List<PersonaProfile> _personas = [];
    private bool _globalEmergencyStop;
    private bool _syncInProgress;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _monitorTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _draftTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _sessionTimer;
    private DateTimeOffset? _sessionStartedAt;
    private CaseRecord? _sessionCase;

    public MainWindow()
    {
        InitializeComponent();
        SetWindowIcon();
        Activated += (_, _) => SetWindowIcon();
        NavigateShell("Home");
        _monitorTimer = DispatcherQueue.CreateTimer(); _monitorTimer.Interval = TimeSpan.FromSeconds(60); _monitorTimer.Tick += MonitorTimer_Tick;
        _draftTimer = DispatcherQueue.CreateTimer(); _draftTimer.Interval = TimeSpan.FromSeconds(2); _draftTimer.IsRepeating = false; _draftTimer.Tick += DraftTimer_Tick;
        _sessionTimer = DispatcherQueue.CreateTimer(); _sessionTimer.Interval = TimeSpan.FromSeconds(1); _sessionTimer.Tick += SessionTimer_Tick;
        Closed += (_, _) => { _monitorTimer.Stop(); _draftTimer.Stop(); _sessionTimer.Stop(); };
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Maximize();
        StatusBox.SelectedIndex = 0;
        ShowForensics(_selected);
        ShowIndicators([]);
        ReviewDraft();
        TemplateBox.ItemsSource = _workspace.Templates;
        TemplateBox.SelectedIndex = 0;
        QuestionBox.ItemsSource = _workspace.Questions;
        QuestionBox.SelectedIndex = 0;
        PlaybookBox.ItemsSource = _workspace.Playbooks;
        PlaybookBox.SelectedIndex = 0;
        _ = LoadPersonasAsync();
        _ = LoadTemplatesAsync();
        _ = LoadSafetyStateAsync();
        _ = LoadCasesAsync();
    }

    private async Task LoadSafetyStateAsync()
    {
        _globalEmergencyStop = await _safetyState.IsEmergencyStopEnabledAsync();
        EmergencyStopButton.IsChecked = _globalEmergencyStop;
        ReviewDraft();
    }

    private async void ShellMenu_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShellMenu.SelectedItem is not ListViewItem item || item.Tag is not string destination) return;
        if (destination == "Case" && _selected is null && _currentCase is null)
        {
            await ShowMessage("Select a suspicious message or saved case under Inbox & cases first.");
            SelectShellDestination("Inbox"); return;
        }
        if ((destination is "Investigate" or "Engage" or "Report") && _currentCase is null)
        {
            await ShowMessage("Create or open a saved case before continuing to this step.");
            SelectShellDestination("Inbox"); return;
        }
        NavigateShell(destination);
    }

    private void SettingsNav_Click(object sender, RoutedEventArgs e)
    {
        ShellMenu.SelectedItem = null;
        NavigateShell("Settings");
    }

    private async void NavigateTo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string destination) return;
        if (destination == "Case" && _selected is null && _currentCase is null)
        {
            await ShowMessage("First open Inbox & cases and select a suspicious message or a saved case.");
            destination = "Inbox";
        }
        else if ((destination is "Investigate" or "Engage" or "Report") && _currentCase is null)
        {
            await ShowMessage("Create or open a saved case before continuing to this step.");
            destination = "Inbox";
        }
        SelectShellDestination(destination);
        NavigateShell(destination);
    }

    private void SelectShellDestination(string destination)
    {
        var item = ShellMenu.Items.OfType<ListViewItem>().FirstOrDefault(candidate => candidate.Tag?.ToString() == destination);
        if (item is not null) ShellMenu.SelectedItem = item;
    }

    private void NavigateShell(string destination)
    {
        if (CollectionPane is null || WorkspaceTabs is null) return;
        var showCollection = destination is "Home" or "Inbox";
        CollectionPane.Visibility = showCollection ? Visibility.Visible : Visibility.Collapsed;
        CollectionColumn.Width = showCollection ? new GridLength(290) : new GridLength(0);
        SetCollectionTabs(destination);
        SetWorkspaceTabs(destination);
        RefreshGuidance();
        if (destination == "Settings") _ = RefreshGoogleSetupStatusAsync();
    }

    private void SetWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ScamBaitDesk-v2.ico");
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var module = GetModuleHandle(null);
        var embeddedIcon = LoadIcon(module, new IntPtr(EmbeddedApplicationIcon));
        if (embeddedIcon != IntPtr.Zero)
        {
            SendMessage(windowHandle, SetIconMessage, new IntPtr(IconBig), embeddedIcon);
            SendMessage(windowHandle, SetIconMessage, new IntPtr(IconSmall), embeddedIcon);
            SetClassLongPtr(windowHandle, ClassIcon, embeddedIcon);
            SetClassLongPtr(windowHandle, ClassSmallIcon, embeddedIcon);
        }
        if (!File.Exists(iconPath)) return;
        AppWindow.SetIcon(iconPath);
        var largeIcon = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 32, 32, LoadFromFile);
        var smallIcon = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 16, 16, LoadFromFile);
        if (largeIcon != IntPtr.Zero) SendMessage(windowHandle, SetIconMessage, new IntPtr(IconBig), largeIcon);
        if (smallIcon != IntPtr.Zero) SendMessage(windowHandle, SetIconMessage, new IntPtr(IconSmall), smallIcon);
        if (largeIcon != IntPtr.Zero) SetClassLongPtr(windowHandle, ClassIcon, largeIcon);
        if (smallIcon != IntPtr.Zero) SetClassLongPtr(windowHandle, ClassSmallIcon, smallIcon);
    }

    private const uint SetIconMessage = 0x0080;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x0010;
    private const int ClassIcon = -14;
    private const int ClassSmallIcon = -34;
    private const int EmbeddedApplicationIcon = 32512;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint load);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr parameter, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW", SetLastError = true)]
    private static extern IntPtr SetClassLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    private void SetCollectionTabs(string destination)
    {
        InboxCollectionTab.Visibility = destination == "Inbox" ? Visibility.Visible : Visibility.Collapsed;
        CasesCollectionTab.Visibility = destination == "Inbox" ? Visibility.Visible : Visibility.Collapsed;
        DashboardCollectionTab.Visibility = destination == "Home" ? Visibility.Visible : Visibility.Collapsed;
        ActionsCollectionTab.Visibility = destination == "Home" ? Visibility.Visible : Visibility.Collapsed;
        CollectionTabs.SelectedItem = destination == "Home" ? DashboardCollectionTab : InboxCollectionTab;
    }

    private void SetWorkspaceTabs(string destination)
    {
        var visible = destination switch
        {
            "Home" => new[] { GuideTab },
            "Inbox" => new[] { ReviewTab },
            "Website" => new[] { WebsiteTab },
            "Case" => new[] { ReviewTab, NotesTab, TimelineTab },
            "Engage" => new[] { PlanTab, ReplyTab, CallsTab },
            "Investigate" => new[] { InsightTab, WebsiteTab, HeadersTab, IndicatorsTab, ToolsTab },
            "Report" => new[] { ReportTab },
            "Settings" => new[] { SettingsTab },
            _ => new[] { GuideTab }
        };
        foreach (var tab in new[] { GuideTab, ReviewTab, ReplyTab, CallsTab, NotesTab, TimelineTab, WebsiteTab, HeadersTab, IndicatorsTab, ReportTab, ToolsTab, PlanTab, InsightTab, SettingsTab })
            tab.Visibility = visible.Contains(tab) ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceTabs.SelectedItem = visible[0];
    }

    private async Task LoadPersonasAsync()
    {
        _personas = (await _workspace.LoadPersonasAsync()).ToList();
        PersonaBox.ItemsSource = _personas;
        if (_currentCase?.PersonaId is Guid personaId)
            PersonaBox.SelectedItem = _personas.FirstOrDefault(item => item.Id == personaId);
    }

    private async Task LoadTemplatesAsync()
    {
        TemplateBox.ItemsSource = await _workspace.LoadAllTemplatesAsync();
        if (TemplateBox.Items.Count > 0) TemplateBox.SelectedIndex = 0;
    }

    private async Task LoadCasesAsync()
    {
        _caseRecords = (await _cases.LoadAsync()).ToList();
        ApplyFilter();
        RefreshDashboard();
        RefreshGuidance();
    }

    private void RefreshGuidance()
    {
        if (NextStepBar is null || NextStepButton is null) return;
        string title; string message; string button; string destination;
        if (_currentCase is null && _selected is null)
        {
            title = "Select a message or saved case";
            message = "Configure and sync the dedicated inbox if needed, then choose one suspicious message. Existing work is under Saved cases.";
            button = "Open inbox and cases"; destination = "Inbox";
        }
        else if (_currentCase is null)
        {
            title = "Create a case from the selected message";
            message = "Review its risk signals, then use New case in the top command bar so the conversation and notes are preserved.";
            button = "Review selected message"; destination = "Case";
        }
        else if (_currentCase.EngagementStopped || _currentCase.Status is CaseStatus.Reported or CaseStatus.Closed || _currentCase.EngagementStage == "Ready to report")
        {
            title = "Finish the case";
            message = "Generate the redacted report, export its hashed evidence package, and verify the ZIP before submitting it manually.";
            button = "Report and export"; destination = "Report";
        }
        else if (_currentCase.Checklist.Take(3).Any(item => !item.Completed))
        {
            title = "Investigate the saved case";
            message = "Review the website, email headers, extracted clues, and investigation checklist before deciding whether to engage.";
            button = "Continue investigation"; destination = "Investigate";
        }
        else
        {
            title = "Plan the next safe action";
            message = "Set an objective and limits, then prepare one privacy-checked manual reply or move directly to reporting.";
            button = "Open safe engagement"; destination = "Engage";
        }
        NextStepBar.Title = title; NextStepBar.Message = message;
        NextStepButton.Content = button; NextStepButton.Tag = destination;
    }

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        try { await SyncInboxAsync(true); }
        catch (Exception ex) { await ShowMessage($"Inbox sync failed: {ex.Message}"); }
    }

    private async Task<int> SyncInboxAsync(bool interactive)
    {
        if (_syncInProgress) return 0;
        _syncInProgress = true;
        try
        {
            var settings = await _settings.LoadAsync();
            if (settings is null) { if (interactive) await ShowMessage("Set up the dedicated inbox first."); return 0; }
            var before = _messages.Select(message => message.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var credential = await GetMailCredentialAsync(settings);
            var fetched = (await _inbox.FetchAsync(settings, credential, settings.Authentication == MailAuthentication.GmailOAuth)).ToList();
            var newReceived = fetched.Count(message => !message.IsOutbound && !before.Contains(message.Id));
            _messages = fetched; ApplyFilter();
            return newReceived;
        }
        finally { _syncInProgress = false; }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var current = await _settings.LoadAsync();
        var host = new TextBox { Header = "IMAP host", Text = current?.Host ?? "imap.gmail.com" };
        var port = new NumberBox { Header = "Port", Value = current?.Port ?? 993, Minimum = 1, Maximum = 65535 };
        var user = new TextBox { Header = "Inbox username", Text = current?.Username ?? string.Empty };
        var password = new PasswordBox { Header = "App password", PlaceholderText = "Stored securely in Windows Credential Locker" };
        var smtpHost = new TextBox { Header = "SMTP host", Text = current?.SmtpHost ?? "smtp.gmail.com" };
        var smtpPort = new NumberBox { Header = "SMTP port", Value = current?.SmtpPort ?? 587, Minimum = 1, Maximum = 65535 };
        var smtpSsl = new CheckBox { Content = "SMTP uses implicit TLS (usually port 465)", IsChecked = current?.SmtpUseSsl ?? false };
        var authentication = new ComboBox { Header = "Authentication", ItemsSource = new[] { "App password", "Gmail OAuth" }, SelectedIndex = current?.Authentication == MailAuthentication.GmailOAuth ? 1 : 0 };
        var oauthClientId = new TextBox { Header = "Google OAuth desktop client ID", Text = current?.OAuthClientId ?? string.Empty, PlaceholderText = "example.apps.googleusercontent.com" };
        var importStatus = new InfoBar { IsOpen = false, IsClosable = true };
        var importCredentials = new Button { Content = "Import downloaded credentials.json", HorizontalAlignment = HorizontalAlignment.Left };
        importCredentials.Click += async (_, _) =>
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads };
                picker.FileTypeFilter.Add(".json");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file = await picker.PickSingleFileAsync();
                if (file is null) return;
                if ((await file.GetBasicPropertiesAsync()).Size > 1_048_576) throw new InvalidOperationException("The credentials file is unexpectedly large.");
                using var document = JsonDocument.Parse(await Windows.Storage.FileIO.ReadTextAsync(file));
                var root = document.RootElement;
                string? clientId = null;
                if (root.TryGetProperty("installed", out var installed) && installed.TryGetProperty("client_id", out var installedClientId)) clientId = installedClientId.GetString();
                else if (root.TryGetProperty("client_id", out var directClientId)) clientId = directClientId.GetString();
                if (string.IsNullOrWhiteSpace(clientId) || !clientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("This is not a Google Desktop app credentials file. In Google Cloud, create an OAuth client with application type Desktop app.");
                oauthClientId.Text = clientId;
                importStatus.Title = "Desktop Client ID imported";
                importStatus.Message = "Only the Client ID was copied. ScamBait Desk does not retain the client secret or the JSON file.";
                importStatus.Severity = InfoBarSeverity.Success;
                importStatus.IsOpen = true;
            }
            catch (Exception ex)
            {
                importStatus.Title = "Could not import credentials";
                importStatus.Message = ex.Message;
                importStatus.Severity = InfoBarSeverity.Error;
                importStatus.IsOpen = true;
            }
        };
        var oauthPanel = new StackPanel { Spacing = 8 };
        oauthPanel.Children.Add(oauthClientId);
        oauthPanel.Children.Add(importCredentials);
        oauthPanel.Children.Add(new TextBlock { Text = "Use the JSON downloaded for a Google OAuth Desktop app. No API key or client secret is needed.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
        oauthPanel.Children.Add(importStatus);
        void RefreshAuthenticationFields()
        {
            var useGoogle = authentication.SelectedIndex == 1;
            oauthPanel.Visibility = useGoogle ? Visibility.Visible : Visibility.Collapsed;
            password.Visibility = useGoogle ? Visibility.Collapsed : Visibility.Visible;
        }
        authentication.SelectionChanged += (_, _) => RefreshAuthenticationFields();
        RefreshAuthenticationFields();
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(host); panel.Children.Add(port); panel.Children.Add(smtpHost); panel.Children.Add(smtpPort); panel.Children.Add(smtpSsl); panel.Children.Add(user); panel.Children.Add(authentication); panel.Children.Add(oauthPanel); panel.Children.Add(password);
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Dedicated test inbox", Content = panel, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var auth = authentication.SelectedIndex == 1 ? MailAuthentication.GmailOAuth : MailAuthentication.AppPassword;
        if (string.IsNullOrWhiteSpace(host.Text) || string.IsNullOrWhiteSpace(smtpHost.Text) || string.IsNullOrWhiteSpace(user.Text) || (auth == MailAuthentication.AppPassword && string.IsNullOrWhiteSpace(password.Password)) || (auth == MailAuthentication.GmailOAuth && string.IsNullOrWhiteSpace(oauthClientId.Text))) { await ShowMessage("Mail hosts and username are required. App-password mode needs a password; Gmail OAuth needs a desktop client ID."); return; }
        if (auth == MailAuthentication.GmailOAuth && !oauthClientId.Text.Trim().EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase)) { await ShowMessage("The Google Client ID should come from an OAuth Desktop app and end with .apps.googleusercontent.com. You can import Google's downloaded credentials.json instead of typing it."); return; }
        await _settings.SaveAsync(new InboxSettings(host.Text.Trim(), (int)port.Value, user.Text.Trim(), smtpHost.Text.Trim(), (int)smtpPort.Value, smtpSsl.IsChecked == true, auth, oauthClientId.Text.Trim()), password.Password);
        await RefreshGoogleSetupStatusAsync();
    }

    private async void ConnectGmail_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await _settings.LoadAsync();
            if (settings is null || settings.Authentication != MailAuthentication.GmailOAuth) { await ShowMessage("Choose Gmail OAuth and enter a Google desktop client ID in Inbox settings first."); return; }
            await _googleOAuth.AuthorizeAsync(settings.Username, settings.OAuthClientId);
            await RefreshGoogleSetupStatusAsync();
            await ShowMessage("Gmail OAuth connected. The refresh token is stored in Windows Credential Locker.");
        }
        catch (Exception ex) { await ShowMessage($"Gmail OAuth connection failed: {ex.Message}"); }
    }

    private async Task RefreshGoogleSetupStatusAsync()
    {
        if (GoogleSetupStatusBar is null) return;
        var settings = await _settings.LoadAsync();
        if (settings is null || settings.Authentication != MailAuthentication.GmailOAuth || string.IsNullOrWhiteSpace(settings.OAuthClientId))
        {
            GoogleSetupStatusBar.Title = "Google is not configured";
            GoogleSetupStatusBar.Message = "Follow the checklist below, then choose Configure inbox now.";
            GoogleSetupStatusBar.Severity = InfoBarSeverity.Informational;
        }
        else if (_googleOAuth.HasStoredAuthorization(settings.Username))
        {
            GoogleSetupStatusBar.Title = "Google account connected";
            GoogleSetupStatusBar.Message = $"Authorization is stored securely for {settings.Username}. Use Test connection to verify IMAP and SMTP.";
            GoogleSetupStatusBar.Severity = InfoBarSeverity.Success;
        }
        else
        {
            GoogleSetupStatusBar.Title = "Desktop Client ID saved";
            GoogleSetupStatusBar.Message = "Choose Connect Google and approve access using the dedicated bait Gmail account.";
            GoogleSetupStatusBar.Severity = InfoBarSeverity.Warning;
        }
        GoogleSetupStatusBar.IsOpen = true;
    }

    private async void OpenGoogleSetupPage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string address || !Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("console.cloud.google.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("developers.google.com", StringComparison.OrdinalIgnoreCase)))
        {
            await ShowMessage("Only official Google setup pages can be opened from this guide.");
            return;
        }
        if (!await Windows.System.Launcher.LaunchUriAsync(uri)) await ShowMessage("Windows could not open the Google setup page in your browser.");
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await _settings.LoadAsync() ?? throw new InvalidOperationException("Configure the dedicated mailbox first.");
            var credential = await GetMailCredentialAsync(settings);
            DiagnosticList.ItemsSource = await _mailDiagnostic.RunAsync(settings, credential, settings.Authentication == MailAuthentication.GmailOAuth);
        }
        catch (Exception ex) { DiagnosticList.ItemsSource = new[] { new ConnectionDiagnostic("Setup", false, ex.Message) }; }
    }

    private async void UpdateApp_Click(object sender, RoutedEventArgs e)
    {
        var updater = _appUpdate.FindUpdater();
        if (updater is null) { await ShowMessage("The updater script could not be found. Run the app from its development repository installation."); return; }
        var check = await CheckForUpdatesAsync();
        if (check is null || !check.IsAvailable) return;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Update ScamBait Desk?",
            Content = "The app will close and an update window will show its progress while the latest version is downloaded, built, and installed. ScamBait Desk will then reopen automatically.",
            PrimaryButtonText = "Update and close app",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try { _appUpdate.Launch(updater); Close(); }
        catch (Exception ex) { await ShowMessage($"The updater could not be started: {ex.Message}"); }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync();

    private async Task<AppUpdateService.UpdateCheckResult?> CheckForUpdatesAsync()
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateButton.IsEnabled = false;
        UpdateStatusBar.Title = "Checking for updates...";
        UpdateStatusBar.Message = "Contacting the main branch without closing the app.";
        UpdateStatusBar.Severity = InfoBarSeverity.Informational;
        try
        {
            var result = await _appUpdate.CheckAsync();
            UpdateStatusBar.Title = result.IsAvailable ? "Update available" : "Up to date";
            UpdateStatusBar.Message = $"{result.Message} Installed build {result.CurrentBuild}; latest build {result.LatestBuild}. Checked {DateTimeOffset.Now:t}.";
            UpdateStatusBar.Severity = result.IsAvailable ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
            UpdateButton.IsEnabled = result.IsAvailable;
            return result;
        }
        catch (Exception ex)
        {
            UpdateStatusBar.Title = "Update check failed";
            UpdateStatusBar.Message = $"{ex.Message} The app will remain open.";
            UpdateStatusBar.Severity = InfoBarSeverity.Error;
            return null;
        }
        finally { CheckUpdateButton.IsEnabled = true; }
    }

    private async void OpenFastVpn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_vpn.OpenFastVpn()) { await ShowMessage("FastVPN could not be found. Install or reinstall the official Namecheap FastVPN Windows app, then try again."); return; }
            await Task.Delay(800); ShowVpnStatus();
        }
        catch (Exception ex) { await ShowMessage($"FastVPN could not be opened: {ex.Message}"); }
    }

    private void CheckVpn_Click(object sender, RoutedEventArgs e) => ShowVpnStatus();

    private void ShowVpnStatus()
    {
        var status = _vpn.GetStatus();
        VpnStatusBar.Title = status.IsConnected ? "VPN tunnel detected" : "VPN not connected";
        VpnStatusBar.Message = status.Detail;
        VpnStatusBar.Severity = status.IsConnected ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
    }

    private void Monitor_Click(object sender, RoutedEventArgs e)
    {
        var enabled = sender is AppBarToggleButton toggle && toggle.IsChecked == true;
        MonitorButton.IsChecked = enabled;
        SettingsMonitorButton.IsChecked = enabled;
        if (enabled) { _monitorTimer.Start(); MonitorBar.Title = "Inbox monitor on"; MonitorBar.Message = "Checking read-only every 60 seconds while the app is open."; MonitorBar.Severity = InfoBarSeverity.Success; }
        else { _monitorTimer.Stop(); MonitorBar.Title = "Inbox monitor off"; MonitorBar.Message = "No background checks are running."; MonitorBar.Severity = InfoBarSeverity.Informational; }
    }

    private async void MonitorTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        try
        {
            var count = await SyncInboxAsync(false);
            if (count > 0) { MonitorBar.Title = $"{count} new message(s)"; MonitorBar.Message = "The dedicated inbox list has been refreshed."; MonitorBar.Severity = InfoBarSeverity.Warning; Activate(); }
        }
        catch (Exception ex) { MonitorBar.Title = "Monitor check failed"; MonitorBar.Message = ex.Message; MonitorBar.Severity = InfoBarSeverity.Error; }
    }

    private async Task<string> GetMailCredentialAsync(InboxSettings settings)
    {
        if (settings.Authentication == MailAuthentication.GmailOAuth)
            return await _googleOAuth.GetAccessTokenAsync(settings.Username, settings.OAuthClientId);
        return _settings.LoadPassword(settings.Username) ?? throw new InvalidOperationException("The mail credential is missing. Open Inbox settings.");
    }

    private void MessageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = MessageList.SelectedItem as InboxMessage;
        if (_selected is null) return;
        NavCaseTitle.Text = _selected.Subject;
        NavCaseStatus.Text = $"Unsaved conversation · {_selected.ReceivedDisplay}";
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
        RecipientText.Text = TryGetCounterpartyAddress(_selected, out var recipient) ? $"Locked recipient: {recipient}" : "Counterparty address could not be parsed";
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
        RefreshGuidance();
        _ = RestoreDraftAsync();
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

    private void CheckWebsite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = _websiteSafety.Check(WebsiteAddressBox.Text);
            WebsiteAddressBox.Text = result.NormalizedUrl;
            WebsiteResultBar.IsOpen = true; WebsiteResultBar.Title = result.Summary;
            WebsiteResultBar.Message = result.Findings.Count == 0
                ? "No obvious URL-structure warning was found. This does not prove the website is safe."
                : "These are structural warning signs only; they are not a definitive scam verdict.";
            WebsiteResultBar.Severity = result.Score >= 55 ? InfoBarSeverity.Error : result.Score >= 25 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
            WebsiteFindingList.ItemsSource = result.Findings;
        }
        catch (Exception ex)
        {
            WebsiteResultBar.IsOpen = true; WebsiteResultBar.Title = "Address could not be checked";
            WebsiteResultBar.Message = ex.Message; WebsiteResultBar.Severity = InfoBarSeverity.Error;
            WebsiteFindingList.ItemsSource = null;
        }
    }

    private async void ScanWebsiteContent_Click(object sender, RoutedEventArgs e)
    {
        WebsiteCheckResult local;
        try { local = _websiteSafety.Check(WebsiteAddressBox.Text); }
        catch (Exception ex) { await ShowMessage(ex.Message); return; }
        var confirmation = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Scan this live page?",
            Content = $"ScamBait Desk will request this page once:\n\n{local.NormalizedUrl}\n\nThe site can see your network IP and request time. The scanner does not run JavaScript, submit forms, follow non-web links, send cookies, or retain the page content. Redirects, download size, ports, and private-network destinations are restricted.",
            PrimaryButtonText = "Scan page once",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

        LiveWebsiteScanButton.IsEnabled = false;
        WebsiteScanProgress.Visibility = Visibility.Visible;
        WebsiteScanProgress.IsActive = true;
        WebsiteResultBar.IsOpen = true;
        WebsiteResultBar.Title = "Scanning page safely...";
        WebsiteResultBar.Message = "Downloading at most 1 MB of text without running page code.";
        WebsiteResultBar.Severity = InfoBarSeverity.Informational;
        try
        {
            var live = await _websiteSafety.ScanPageAsync(local.NormalizedUrl);
            var combined = local.Findings.Concat(live.Findings).ToList();
            var score = Math.Min(100, combined.Sum(finding => finding.Weight));
            var rating = score switch { >= 55 => "High concern", >= 25 => "Suspicious", >= 10 => "Review advised", _ => "No obvious warning found" };
            WebsiteAddressBox.Text = live.FinalUrl;
            WebsiteResultBar.Title = $"{rating} · {score}/100 · {combined.Count} combined signal(s)";
            WebsiteResultBar.Message = $"Page title: {live.PageTitle} · downloaded {live.DownloadBytes:N0} bytes · followed {live.RedirectCount} redirect(s). A low score does not prove the site is safe.";
            WebsiteResultBar.Severity = score >= 55 ? InfoBarSeverity.Error : score >= 25 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
            WebsiteFindingList.ItemsSource = combined;
            if (_currentCase is not null)
            {
                _currentCase.UpdatedAt = DateTimeOffset.Now;
                _currentCase.Timeline.Add(new CaseEvent(_currentCase.UpdatedAt, "Live website scan", $"User approved a restricted content scan of {new Uri(live.FinalUrl).IdnHost}; result: {rating} ({score}/100)."));
                await _cases.SaveAsync(_currentCase);
                TimelineList.ItemsSource = _currentCase.Timeline.OrderByDescending(item => item.At);
            }
        }
        catch (Exception ex)
        {
            WebsiteResultBar.Title = "Live scan stopped";
            WebsiteResultBar.Message = ex.Message;
            WebsiteResultBar.Severity = InfoBarSeverity.Error;
            WebsiteFindingList.ItemsSource = local.Findings;
        }
        finally
        {
            WebsiteScanProgress.IsActive = false;
            WebsiteScanProgress.Visibility = Visibility.Collapsed;
            LiveWebsiteScanButton.IsEnabled = true;
        }
    }

    private async void CheckWebsiteReputation_Click(object sender, RoutedEventArgs e)
    {
        WebsiteCheckResult result;
        try { result = _websiteSafety.Check(WebsiteAddressBox.Text); }
        catch (Exception ex) { await ShowMessage(ex.Message); return; }
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "External website reputation lookup", Content = $"Open VirusTotal search and disclose this full URL?\n\n{result.NormalizedUrl}\n\nThe target website itself will not be opened by ScamBait Desk. The URL will be shared with VirusTotal; do not continue if it contains private tokens or personal data.", PrimaryButtonText = "Open existing report search", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await Windows.System.Launcher.LaunchUriAsync(new Uri($"https://www.virustotal.com/gui/search/{Uri.EscapeDataString(result.NormalizedUrl)}"));
        if (_currentCase is null) return;
        _currentCase.UpdatedAt = DateTimeOffset.Now;
        _currentCase.Timeline.Add(new CaseEvent(_currentCase.UpdatedAt, "Website lookup", $"User approved an external existing-report search for {result.Host}."));
        await _cases.SaveAsync(_currentCase);
    }

    private void LoadCaseWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null) { WebsiteResultBar.IsOpen = true; WebsiteResultBar.Title = "No active case"; WebsiteResultBar.Message = "Open a case containing an extracted URL first."; WebsiteResultBar.Severity = InfoBarSeverity.Warning; return; }
        var url = _indicatorExtractor.Extract(_currentCase.Messages).FirstOrDefault(item => item.Type == IndicatorType.Url)?.Value;
        if (string.IsNullOrWhiteSpace(url)) { WebsiteResultBar.IsOpen = true; WebsiteResultBar.Title = "No website found"; WebsiteResultBar.Message = "The active case contains no extracted URL."; WebsiteResultBar.Severity = InfoBarSeverity.Warning; return; }
        WebsiteAddressBox.Text = url; CheckWebsite_Click(sender, e);
    }

    private async void NewCase_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _analysis is null) { await ShowMessage("Select an inbox message first, then choose New case."); return; }
        _currentCase = null;
        await SaveCaseAsync("New case created locally.");
    }

    private async void SaveCase_Click(object sender, RoutedEventArgs e) =>
        await SaveCaseAsync("Case saved locally.");

    private async Task SaveCaseAsync(string confirmation)
    {
        if (_selected is null || _analysis is null) { await ShowMessage("Select a message before saving a case."); return; }
        var now = DateTimeOffset.Now;
        _currentCase ??= new CaseRecord
        {
            Title = _selected.Subject,
            CreatedAt = now,
            Messages = ConversationService.FindConversation(_selected, _messages).ToList(),
            Timeline = [new CaseEvent(now, "Created", "Case created from the dedicated inbox.")],
            Checklist = CaseRepository.NewChecklist()
        };
        _currentCase.Analysis = _analysis;
        _currentCase.DraftReply = DraftBox.Text;
        _currentCase.Notes = NotesBox.Text;
        _currentCase.Status = StatusFromIndex(StatusBox.SelectedIndex);
        _currentCase.PersonaId = (PersonaBox.SelectedItem as PersonaProfile)?.Id;
        _currentCase.UpdatedAt = now;
        _currentCase.Timeline.Add(new CaseEvent(now, "Saved", $"Case saved with {_currentCase.Messages.Count} message(s)."));
        await _cases.SaveAsync(_currentCase);
        NavCaseTitle.Text = _currentCase.Title;
        NavCaseStatus.Text = _currentCase.Summary;
        await LoadCasesAsync();
        ShowStoppedState();
        TimelineList.ItemsSource = _currentCase.Timeline.OrderByDescending(item => item.At);
        RefreshGuidance();
        await ShowMessage(confirmation);
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
        NavCaseTitle.Text = _currentCase.Title;
        NavCaseStatus.Text = _currentCase.Summary;
        _selected = _currentCase.Messages.OrderByDescending(message => message.ReceivedAt).FirstOrDefault();
        _analysis = _currentCase.Analysis;
        SubjectText.Text = _currentCase.Title;
        SenderText.Text = $"{_currentCase.Messages.Count} message(s) · created {_currentCase.CreatedAt.LocalDateTime:g}";
        MessageBody.Text = string.Join("\n\n──────────\n\n", _currentCase.Messages.OrderBy(message => message.ReceivedAt).Select(message => $"{message.ReceivedDisplay} · {message.Sender}\n{ScamAnalysisService.Redact(message.Body)}"));
        DraftBox.Text = _currentCase.DraftReply;
        RecipientText.Text = _selected is not null && TryGetCounterpartyAddress(_selected, out var recipient) ? $"Locked recipient: {recipient}" : "Counterparty address could not be parsed";
        OutboundList.ItemsSource = _currentCase.OutboundMessages.OrderByDescending(item => item.SentAt);
        PersonaBox.SelectedItem = _personas.FirstOrDefault(item => item.Id == _currentCase.PersonaId);
        ShowStoppedState();
        NotesBox.Text = _currentCase.Notes;
        PriorityBox.SelectedIndex = _currentCase.Priority switch { "Low" => 0, "High" => 2, "Urgent" => 3, _ => 1 };
        TagsBox.Text = string.Join(", ", _currentCase.Tags);
        SignalList.ItemsSource = _analysis?.Signals;
        TimelineList.ItemsSource = _currentCase.Timeline.OrderByDescending(item => item.At);
        StatusBox.SelectedIndex = IndexFromStatus(_currentCase.Status);
        ShowForensics(_selected);
        ShowIndicators(_currentCase.Messages);
        RefreshCaseTools();
        RefreshGuidance();
        RefreshEngagementPlan();
        RefreshConversationSummary();
        RefreshCaseBriefing();
        RefreshCallWorkspace();
        _ = RestoreDraftAsync();
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
        NavCaseStatus.Text = _currentCase.Summary;
        await LoadCasesAsync();
        TimelineList.ItemsSource = _currentCase.Timeline.OrderByDescending(item => item.At);
    }

    private async void Reputation_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !TryGetDomain(_selected.IsOutbound ? _selected.Recipient : _selected.Sender, out var domain)) { await ShowMessage("No counterparty domain was found."); return; }
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

    private void DraftBox_TextChanged(object sender, TextChangedEventArgs e) { ReviewDraft(); _draftTimer?.Stop(); _draftTimer?.Start(); }

    private async void DraftTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        var key = DraftKey(); if (key is not null) await _draftRecovery.SaveAsync(key, DraftBox.Text ?? string.Empty);
    }

    private async Task RestoreDraftAsync()
    {
        var key = DraftKey(); if (key is null) return;
        var recovered = await _draftRecovery.LoadAsync(key);
        if (!string.IsNullOrWhiteSpace(recovered) && recovered != DraftBox.Text) DraftBox.Text = recovered;
    }

    private string? DraftKey() => _currentCase is not null ? $"case:{_currentCase.Id}" : _selected is not null ? $"message:{_selected.Id}" : null;

    private void ReviewDraft()
    {
        if (PrivacyBar is null || DraftBox is null || PrivacyFindings is null || SendButton is null) return;
        var review = _engagementSafety.Review(DraftBox.Text ?? string.Empty);
        PrivacyBar.Title = review.CanSend ? "Privacy guard passed" : "Privacy guard blocked sending";
        PrivacyBar.Message = review.Summary;
        PrivacyBar.Severity = !review.CanSend ? InfoBarSeverity.Error : review.Findings.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        PrivacyFindings.ItemsSource = review.Findings;
        var personaReview = EngagementWorkspaceService.CheckPersonaConsistency(DraftBox.Text ?? string.Empty, PersonaBox?.SelectedItem as PersonaProfile);
        ConsistencyBar.Title = personaReview.Findings.Count == 0 ? "Persona consistency passed" : "Review persona consistency";
        ConsistencyBar.Message = personaReview.Summary;
        ConsistencyBar.Severity = personaReview.Findings.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        ConsistencyFindings.ItemsSource = personaReview.Findings;
        var budgetAvailable = _currentCase is null || _currentCase.OutboundMessages.Count < _currentCase.OutboundMessageBudget;
        var beforeDeadline = _currentCase?.EngagementDeadline is null || DateTimeOffset.Now <= _currentCase.EngagementDeadline;
        var activeStage = _currentCase?.EngagementStage != "Ended";
        SendButton.IsEnabled = review.CanSend && !string.IsNullOrWhiteSpace(DraftBox.Text) && _selected is not null && _currentCase?.EngagementStopped != true && !_globalEmergencyStop && budgetAvailable && beforeDeadline && activeStage;
    }

    private async void SendReply_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !TryGetCounterpartyAddress(_selected, out var recipient)) { await ShowMessage("The selected counterparty address could not be parsed."); return; }
        var review = _engagementSafety.Review(DraftBox.Text);
        if (!review.CanSend) { await ShowMessage("Sending is blocked until all privacy-guard issues are removed."); return; }
        if (_currentCase is null) { await ShowMessage("Save or open the case before sending so the engagement has an audit trail."); return; }
        if (_currentCase.EngagementStopped) { await ShowMessage("This engagement was permanently stopped. Sending is disabled for this case."); return; }
        if (_globalEmergencyStop) { await ShowMessage("The global emergency stop is enabled. Disable it before any outbound message can be sent."); return; }
        if (_currentCase.OutboundMessages.Count >= _currentCase.OutboundMessageBudget) { await ShowMessage("This case has reached its total outbound-message budget. Review the engagement plan before continuing."); return; }
        if (_currentCase.EngagementDeadline is DateTimeOffset deadline && DateTimeOffset.Now > deadline) { await ShowMessage("This case's engagement deadline has passed. Review the plan before continuing."); return; }
        if (_currentCase.EngagementStage == "Ended") { await ShowMessage("This engagement plan is marked Ended. Change the plan only after a deliberate review."); return; }

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
            var credential = await GetMailCredentialAsync(settings);
            var subject = _selected.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? _selected.Subject : $"Re: {_selected.Subject}";
            var messageId = await _smtp.SendAsync(settings, credential, recipient, subject, DraftBox.Text, _selected, useOAuth: settings.Authentication == MailAuthentication.GmailOAuth);
            var now = DateTimeOffset.Now;
            _currentCase.DraftReply = DraftBox.Text;
            _currentCase.OutboundMessages.Add(new OutboundMessageRecord(now, recipient, subject, ScamAnalysisService.Redact(DraftBox.Text), messageId));
            _currentCase.UpdatedAt = now;
            _currentCase.Timeline.Add(new CaseEvent(now, "Manual outbound", $"Sent one plain-text message to {recipient}. Message-ID: {messageId}."));
            await _cases.SaveAsync(_currentCase);
            _draftRecovery.Delete($"case:{_currentCase.Id}");
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

    private static bool TryGetCounterpartyAddress(InboxMessage message, out string address) =>
        TryGetSenderAddress(message.IsOutbound ? message.Recipient : message.Sender, out address);

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
        ReviewDraft();
    }

    private void ApplyTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateBox.SelectedItem is not ReplyTemplate template) return;
        var persona = PersonaBox.SelectedItem as PersonaProfile;
        var signoff = persona is null ? string.Empty : $"\n\n{persona.Name}";
        DraftBox.Text = template.Body + signoff;
    }

    private async void AddTemplate_Click(object sender, RoutedEventArgs e)
    {
        var name = new TextBox { Header = "Template name" };
        var category = new TextBox { Header = "Category", Text = "Custom" };
        var body = new TextBox { Header = "Safe reply text", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 160 };
        var panel = new StackPanel { Spacing = 10 }; panel.Children.Add(name); panel.Children.Add(category); panel.Children.Add(body);
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Add local reply template", Content = panel, PrimaryButtonText = "Save locally", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(body.Text)) return;
        if (!_engagementSafety.Review(body.Text).CanSend) { await ShowMessage("Remove blocked private or financial material before saving this template."); return; }
        await _workspace.SaveTemplateAsync(new ReplyTemplate(name.Text.Trim(), string.IsNullOrWhiteSpace(category.Text) ? "Custom" : category.Text.Trim(), body.Text.Trim()));
        await LoadTemplatesAsync();
    }

    private async void ManageTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateBox.SelectedItem is not ReplyTemplate selected) { await ShowMessage("Select a template first."); return; }
        var name = new TextBox { Header = "Template name", Text = selected.Name };
        var category = new TextBox { Header = "Category", Text = selected.ScamType };
        var body = new TextBox { Header = "Safe reply text", Text = selected.Body, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 160 };
        var panel = new StackPanel { Spacing = 10 }; panel.Children.Add(name); panel.Children.Add(category); panel.Children.Add(body);
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Manage local template", Content = panel, PrimaryButtonText = "Save changes", SecondaryButtonText = "Delete", CloseButtonText = "Cancel" };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            if (!await _workspace.DeleteTemplateAsync(selected)) { await ShowMessage("Built-in templates cannot be deleted."); return; }
            await LoadTemplatesAsync(); return;
        }
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(body.Text)) return;
        if (!_engagementSafety.Review(body.Text).CanSend) { await ShowMessage("Remove blocked private or financial material before saving this template."); return; }
        var replacement = new ReplyTemplate(name.Text.Trim(), string.IsNullOrWhiteSpace(category.Text) ? "Custom" : category.Text.Trim(), body.Text.Trim());
        if (!await _workspace.UpdateTemplateAsync(selected, replacement)) { await ShowMessage("Built-in templates are read-only. Use Add to create a local version."); return; }
        await LoadTemplatesAsync();
    }

    private void SuggestLocalReply_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null) { DraftBox.Text = "Save or open the case before generating a conversation-aware suggestion."; return; }
        var persona = PersonaBox.SelectedItem as PersonaProfile;
        DraftBox.Text = _intelligence.SuggestLocalReply(_currentCase) + (persona is null ? string.Empty : $"\n\n{persona.Name}");
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

    private async void VerifyEvidence_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".zip");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync(); if (file is null) return;
        try
        {
            await using var stream = await file.OpenStreamForReadAsync();
            var result = _evidenceVerifier.Verify(stream);
            EvidenceVerificationBar.IsOpen = true;
            EvidenceVerificationBar.Title = result.Success ? "Evidence package verified" : "Evidence package changed";
            EvidenceVerificationBar.Message = result.Success ? result.Summary : $"{result.Summary} {string.Join("; ", result.Problems.Take(5))}";
            EvidenceVerificationBar.Severity = result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        }
        catch (Exception ex)
        {
            EvidenceVerificationBar.IsOpen = true; EvidenceVerificationBar.Title = "Verification failed";
            EvidenceVerificationBar.Message = ex.Message; EvidenceVerificationBar.Severity = InfoBarSeverity.Error;
        }
    }

    private void RefreshDashboard()
    {
        if (DashboardList is null || PendingList is null) return;
        DashboardList.ItemsSource = _intelligence.Dashboard(_caseRecords);
        NextActionList.ItemsSource = _intelligence.NextActions(_caseRecords);
        PendingList.ItemsSource = _caseRecords.Where(record =>
            record.Status != CaseStatus.Closed || record.Reminders.Any(reminder => !reminder.Completed && reminder.DueAt <= DateTimeOffset.Now))
            .OrderByDescending(record => record.Reminders.Any(reminder => !reminder.Completed && reminder.DueAt <= DateTimeOffset.Now))
            .ThenByDescending(record => record.Analysis?.Score ?? 0).ToList();
        RecentActivityList.ItemsSource = _caseRecords.SelectMany(record => record.Timeline.Select(item => new ActivityItem(item.At, record.Title, item.Kind, item.Detail)))
            .OrderByDescending(item => item.At).Take(20).ToList();
    }

    private void RefreshCaseTools()
    {
        if (_currentCase is null) return;
        AttachmentList.ItemsSource = _currentCase.Messages.SelectMany(message => message.Attachments).ToList();
        DuplicateList.ItemsSource = _intelligence.FindDuplicates(_currentCase, _caseRecords);
        ReminderList.ItemsSource = _currentCase.Reminders.OrderBy(reminder => reminder.Completed).ThenBy(reminder => reminder.DueAt).ToList();
        ChecklistList.ItemsSource = _currentCase.Checklist;
    }

    private void RefreshEngagementPlan()
    {
        if (_currentCase is null) return;
        var stages = new[] { "Initial review", "Verification questions", "Claims under review", "Awaiting response", "Ready to report", "Ended" };
        EngagementStageBox.SelectedIndex = Math.Max(0, Array.IndexOf(stages, _currentCase.EngagementStage));
        EngagementObjectiveBox.Text = _currentCase.EngagementObjective;
        MessageBudgetBox.Value = _currentCase.OutboundMessageBudget;
        EngagementDeadlinePicker.Date = _currentCase.EngagementDeadline ?? DateTimeOffset.Now.AddDays(7);
        ClaimList.ItemsSource = _currentCase.SenderClaims.OrderByDescending(item => item.RecordedAt).ToList();
        SourcedIndicatorList.ItemsSource = _currentCase.ImportedIndicators.OrderByDescending(item => item.AddedAt).ToList();
    }

    private void RefreshConversationSummary()
    {
        if (_currentCase is null) return;
        var summary = _conversationSummary.Summarize(_currentCase);
        ConversationOverview.Text = summary.Overview;
        ConversationFactList.ItemsSource = summary.Facts;
        ContradictionList.ItemsSource = summary.Contradictions;
        UnansweredQuestionList.ItemsSource = summary.UnansweredQuestions;
    }

    private void RefreshCaseBriefing()
    {
        if (_currentCase is null) { CaseBriefingText.Text = "Open a case to generate a briefing."; return; }
        var due = _currentCase.Reminders.Count(item => !item.Completed && item.DueAt <= DateTimeOffset.Now);
        var contradicted = _currentCase.SenderClaims.Count(item => item.VerificationStatus == "Contradicted");
        var next = _intelligence.NextActions([_currentCase]).FirstOrDefault()?.Action ?? "Review the case and choose a controlled next step";
        CaseBriefingText.Text = $"{_currentCase.Priority} priority · risk {_currentCase.Analysis?.Score ?? 0}/100 · {_currentCase.EngagementStage}. {_currentCase.Messages.Count} message(s), {_currentCase.OutboundMessages.Count} manual reply/replies, {contradicted} contradicted claim(s), {due} overdue reminder(s). Next: {next}.";
    }

    private void PlaybookBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        PlaybookStepList.ItemsSource = (PlaybookBox.SelectedItem as EngagementPlaybook)?.Steps;

    private async void ApplyPlaybook_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null || PlaybookBox.SelectedItem is not EngagementPlaybook playbook) { await ShowMessage("Open a case and select a playbook first."); return; }
        _currentCase.EngagementStage = playbook.Stage;
        _currentCase.EngagementObjective = playbook.Objective;
        _currentCase.UpdatedAt = DateTimeOffset.Now;
        _currentCase.Timeline.Add(new CaseEvent(_currentCase.UpdatedAt, "Playbook", $"Applied {playbook.Name}; no message sent."));
        await _cases.SaveAsync(_currentCase); RefreshEngagementPlan(); RefreshCaseBriefing(); await LoadCasesAsync();
    }

    private void CopyBriefing_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CaseBriefingText.Text)) return;
        var package = new DataPackage(); package.SetText(CaseBriefingText.Text); Clipboard.SetContent(package);
    }

    private void RefreshCallWorkspace()
    {
        if (_currentCase is null) { CallNumberBox.ItemsSource = null; CallLogList.ItemsSource = null; return; }
        CallNumberBox.ItemsSource = _indicatorExtractor.Extract(_currentCase.Messages)
            .Where(item => item.Type == IndicatorType.Phone).Select(item => item.Value).Distinct().ToList();
        if (CallNumberBox.Items.Count > 0) CallNumberBox.SelectedIndex = 0;
        CallLogList.ItemsSource = _currentCase.Calls.OrderByDescending(call => call.At).ToList();
    }

    private async void OpenVoip_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null || CallNumberBox.SelectedItem is not string number) { await ShowMessage("Open a case containing an extracted phone number first."); return; }
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Open dedicated VoIP app?", Content = $"Open this case-sourced number in your default Windows calling app?\n\n{number}\n\nUse only a dedicated bait number. This does not start recording.", PrimaryButtonText = "Open calling app", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var dialNumber = new string(number.Where(character => char.IsDigit(character) || character == '+').ToArray());
        if (!await Windows.System.Launcher.LaunchUriAsync(new Uri($"tel:{dialNumber}"))) { await ShowMessage("Windows has no default app registered for telephone links."); return; }
        var now = DateTimeOffset.Now;
        _currentCase.Calls.Add(new CallLogRecord(Guid.NewGuid(), now, number, "VoIP app opened", "Manual call launch requested; connection not verified.", false));
        _currentCase.UpdatedAt = now;
        _currentCase.Timeline.Add(new CaseEvent(now, "VoIP call", "Opened one case-sourced number in the default calling app; recording was not started."));
        await _cases.SaveAsync(_currentCase); RefreshCallWorkspace();
    }

    private async void LogCallOutcome_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null || CallNumberBox.SelectedItem is not string number) { await ShowMessage("Open a case containing an extracted phone number first."); return; }
        var now = DateTimeOffset.Now;
        var outcome = CallOutcomeBox.SelectedItem?.ToString() ?? "Other";
        var notes = ScamAnalysisService.Redact(CallNotesBox.Text ?? string.Empty);
        _currentCase.Calls.Add(new CallLogRecord(Guid.NewGuid(), now, number, outcome, notes, RecordingConsentBox.IsChecked == true));
        _currentCase.UpdatedAt = now;
        _currentCase.Timeline.Add(new CaseEvent(now, "Call outcome", $"{outcome}; notes stored redacted; recording consent confirmed: {RecordingConsentBox.IsChecked == true}."));
        await _cases.SaveAsync(_currentCase); CallNotesBox.Text = string.Empty; RefreshCallWorkspace(); await LoadCasesAsync();
    }

    private async void SaveEngagementPlan_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null) { await ShowMessage("Open or save a case first."); return; }
        _currentCase.EngagementStage = EngagementStageBox.SelectedItem?.ToString() ?? "Initial review";
        _currentCase.EngagementObjective = string.IsNullOrWhiteSpace(EngagementObjectiveBox.Text) ? "Request independently verifiable information" : EngagementObjectiveBox.Text.Trim();
        _currentCase.OutboundMessageBudget = Math.Max(1, (int)MessageBudgetBox.Value);
        _currentCase.EngagementDeadline = EngagementDeadlinePicker.Date.Date.AddDays(1).AddTicks(-1);
        _currentCase.UpdatedAt = DateTimeOffset.Now;
        _currentCase.Timeline.Add(new CaseEvent(_currentCase.UpdatedAt, "Engagement plan", $"Stage: {_currentCase.EngagementStage}; reply budget: {_currentCase.OutboundMessageBudget}; deadline: {_currentCase.EngagementDeadline?.LocalDateTime:g}."));
        await _cases.SaveAsync(_currentCase); ReviewDraft(); await LoadCasesAsync();
    }

    private void ApplyQuestion_Click(object sender, RoutedEventArgs e)
    {
        if (QuestionBox.SelectedItem is not SafeQuestion question) return;
        DraftBox.Text = string.IsNullOrWhiteSpace(DraftBox.Text) ? question.Text : $"{DraftBox.Text.Trim()}\n\n{question.Text}";
    }

    private async void AddClaim_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null) { await ShowMessage("Open or save a case first."); return; }
        var category = new ComboBox { Header = "Category", SelectedIndex = 0, ItemsSource = new[] { "Identity", "Organisation", "Payment", "Urgency", "Location", "Relationship", "Other" } };
        var claim = new TextBox { Header = "What the sender claimed", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var status = new ComboBox { Header = "Verification", SelectedIndex = 0, ItemsSource = new[] { "Unverified", "Inconsistent", "Independently verified", "Contradicted" } };
        var panel = new StackPanel { Spacing = 10 }; panel.Children.Add(category); panel.Children.Add(claim); panel.Children.Add(status);
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Record sender claim", Content = panel, PrimaryButtonText = "Record", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(claim.Text)) return;
        var item = new SenderClaim(Guid.NewGuid(), DateTimeOffset.Now, category.SelectedItem?.ToString() ?? "Other", ScamAnalysisService.Redact(claim.Text.Trim()), status.SelectedItem?.ToString() ?? "Unverified");
        _currentCase.SenderClaims.Add(item); _currentCase.UpdatedAt = item.RecordedAt;
        _currentCase.Timeline.Add(new CaseEvent(item.RecordedAt, "Sender claim", $"{item.Category}: {item.VerificationStatus}."));
        await _cases.SaveAsync(_currentCase); RefreshEngagementPlan();
    }

    private async void ContradictClaim_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null || ClaimList.SelectedItem is not SenderClaim selected) { await ShowMessage("Select a sender claim first."); return; }
        var index = _currentCase.SenderClaims.FindIndex(item => item.Id == selected.Id); if (index < 0) return;
        _currentCase.SenderClaims[index] = selected with { VerificationStatus = "Contradicted" };
        _currentCase.UpdatedAt = DateTimeOffset.Now;
        _currentCase.Timeline.Add(new CaseEvent(_currentCase.UpdatedAt, "Claim contradicted", selected.Category));
        await _cases.SaveAsync(_currentCase); RefreshEngagementPlan();
    }

    private async void AddSourcedIndicator_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null) { await ShowMessage("Open or save a case first."); return; }
        var value = new TextBox { Header = "Email address or domain" };
        var source = new TextBox { Header = "Source", PlaceholderText = "For example: message received in dedicated inbox" };
        var evidence = new TextBox { Header = "Why you are authorised to investigate it", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var confirmation = new CheckBox { Content = "I will not use this entry for unsolicited first contact." };
        var panel = new StackPanel { Spacing = 10 }; panel.Children.Add(value); panel.Children.Add(source); panel.Children.Add(evidence); panel.Children.Add(confirmation);
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Add sourced indicator", Content = panel, PrimaryButtonText = "Add to case", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(value.Text) || string.IsNullOrWhiteSpace(source.Text) || string.IsNullOrWhiteSpace(evidence.Text) || confirmation.IsChecked != true) { await ShowMessage("Value, provenance, authorization note, and confirmation are required."); return; }
        var normalized = value.Text.Trim().ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^(?:[a-z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-z0-9.-]+|[a-z0-9.-]+\.[a-z]{2,})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) { await ShowMessage("Enter one valid email address or domain."); return; }
        if (_currentCase.ImportedIndicators.Any(item => item.Value.Equals(normalized, StringComparison.OrdinalIgnoreCase))) { await ShowMessage("That indicator is already attached to this case."); return; }
        var item = new ProvenanceIndicator(Guid.NewGuid(), DateTimeOffset.Now, normalized, source.Text.Trim(), ScamAnalysisService.Redact(evidence.Text.Trim()));
        _currentCase.ImportedIndicators.Add(item); _currentCase.UpdatedAt = item.AddedAt;
        _currentCase.Timeline.Add(new CaseEvent(item.AddedAt, "Sourced indicator", $"Added {normalized} with recorded provenance; no contact initiated."));
        await _cases.SaveAsync(_currentCase); RefreshEngagementPlan();
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

    private async void QuickReminder_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null) { await ShowMessage("Open or save a case first."); return; }
        var due = DateTimeOffset.Now.AddHours(24);
        _currentCase.Reminders.Add(new FollowUpReminder(Guid.NewGuid(), due, "Review for a reply", false));
        _currentCase.UpdatedAt = DateTimeOffset.Now;
        _currentCase.Timeline.Add(new CaseEvent(_currentCase.UpdatedAt, "Reminder", $"Quick follow-up scheduled for {due.LocalDateTime:g}. No message will be sent automatically."));
        await _cases.SaveAsync(_currentCase); RefreshCaseTools(); await LoadCasesAsync();
    }

    private async void SaveCaseDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null) { await ShowMessage("Open or save a case first."); return; }
        _currentCase.Priority = PriorityBox.SelectedItem?.ToString() ?? "Normal";
        _currentCase.Tags = TagsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => tag.Length <= 30).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
        _currentCase.UpdatedAt = DateTimeOffset.Now;
        _currentCase.Timeline.Add(new CaseEvent(_currentCase.UpdatedAt, "Case details", $"Priority set to {_currentCase.Priority}; {_currentCase.Tags.Count} tag(s)."));
        await _cases.SaveAsync(_currentCase); NavCaseStatus.Text = _currentCase.Summary; await LoadCasesAsync();
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

    private async void ToggleChecklist_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null || ChecklistList.SelectedItem is not CaseChecklistItem selected) { await ShowMessage("Select a checklist item first."); return; }
        var index = _currentCase.Checklist.FindIndex(item => item.Id == selected.Id); if (index < 0) return;
        _currentCase.Checklist[index] = selected with { Completed = !selected.Completed };
        _currentCase.UpdatedAt = DateTimeOffset.Now;
        _currentCase.Timeline.Add(new CaseEvent(_currentCase.UpdatedAt, "Checklist", $"{_currentCase.Checklist[index].Label}: {(_currentCase.Checklist[index].Completed ? "complete" : "reopened")}."));
        await _cases.SaveAsync(_currentCase); RefreshCaseTools();
    }

    private async void ResetChecklist_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null) { await ShowMessage("Open or save a case first."); return; }
        _currentCase.Checklist = CaseRepository.NewChecklist(); _currentCase.UpdatedAt = DateTimeOffset.Now;
        _currentCase.Timeline.Add(new CaseEvent(_currentCase.UpdatedAt, "Checklist", "Investigation checklist reset."));
        await _cases.SaveAsync(_currentCase); RefreshCaseTools();
    }

    private void DefangIndicators_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null) { DefangedIndicatorsBox.Text = "Open or save a case first."; return; }
        var values = _indicatorExtractor.Extract(_currentCase.Messages)
            .Where(item => item.Type is IndicatorType.Url or IndicatorType.Domain or IndicatorType.IpAddress)
            .Select(item => item.Value.Replace("https://", "hxxps://", StringComparison.OrdinalIgnoreCase).Replace("http://", "hxxp://", StringComparison.OrdinalIgnoreCase).Replace(".", "[.]"))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        DefangedIndicatorsBox.Text = values.Count == 0 ? "No URL, domain, or IP indicators found." : string.Join(Environment.NewLine, values);
    }

    private void StartSession_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCase is null) { _ = ShowMessage("Open or save a case first."); return; }
        _sessionCase = _currentCase; _sessionStartedAt = DateTimeOffset.Now; _sessionTimer.Start(); StartSessionButton.IsEnabled = false; StopSessionButton.IsEnabled = true; SessionTimerText.Text = "00:00:00";
    }

    private void SessionTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (_sessionStartedAt is not null) SessionTimerText.Text = (DateTimeOffset.Now - _sessionStartedAt.Value).ToString(@"hh\:mm\:ss");
    }

    private async void StopSession_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionCase is null || _sessionStartedAt is null) return;
        var stopped = DateTimeOffset.Now; var duration = stopped - _sessionStartedAt.Value;
        _sessionTimer.Stop(); _sessionStartedAt = null; StartSessionButton.IsEnabled = true; StopSessionButton.IsEnabled = false;
        var target = _sessionCase; _sessionCase = null;
        target.EngagementSeconds += Math.Max(1, (long)duration.TotalSeconds); target.UpdatedAt = stopped;
        var durationText = duration.ToString(@"hh\:mm\:ss");
        var totalText = TimeSpan.FromSeconds(target.EngagementSeconds).ToString(@"hh\:mm\:ss");
        target.Timeline.Add(new CaseEvent(stopped, "Work session", $"Logged {durationText} of manual case work; total {totalText}."));
        await _cases.SaveAsync(target); await LoadCasesAsync();
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

    private async void ExportCases_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary, SuggestedFileName = $"ScamBaitDesk-cases-{DateTime.Now:yyyyMMdd}" };
        picker.FileTypeChoices.Add("ScamBait Desk case backup", new List<string> { ".json" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync(); if (file is null) return;
        await using var stream = await file.OpenStreamForWriteAsync(); stream.SetLength(0);
        await _cases.ExportAllAsync(stream); await ShowMessage($"Exported {_caseRecords.Count} local case(s).");
    }

    private async void ImportCases_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync(); if (file is null) return;
        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size > 25 * 1024 * 1024) { await ShowMessage("The backup is larger than the 25 MB import limit."); return; }
        try
        {
            await using var stream = await file.OpenStreamForReadAsync();
            var count = await _cases.ImportAsync(stream); await LoadCasesAsync();
            await ShowMessage($"Imported or updated {count} case(s). Newer local cases were preserved.");
        }
        catch (Exception ex) { await ShowMessage($"The case backup could not be imported: {ex.Message}"); }
    }

    private async Task ShowMessage(string message) => await new ContentDialog
    {
        XamlRoot = Content.XamlRoot, Title = "ScamBait Desk", Content = message, CloseButtonText = "OK"
    }.ShowAsync();
}
