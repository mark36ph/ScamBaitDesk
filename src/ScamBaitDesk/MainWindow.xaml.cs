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
    private readonly ConversationCoachService _conversationCoach = new();
    private readonly MailDiagnosticService _mailDiagnostic = new();
    private readonly AppUpdateService _appUpdate = new();
    private readonly VpnIntegrationService _vpn = new();
    private readonly VoiceProtectionService _voiceProtection = new();
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
    private VoiceProtectionSettings _voiceSettings = VoiceProtectionSettings.Default;
    private bool _microphoneTestActive;
    private bool _voiceOutputActive;
    private bool _isInitializing = true;

    public MainWindow()
    {
        InitializeComponent();
        _isInitializing = false;
        SetWindowIcon();
        Activated += (_, _) => SetWindowIcon();
        NavigateShell("Home");
        _monitorTimer = DispatcherQueue.CreateTimer(); _monitorTimer.Interval = TimeSpan.FromSeconds(60); _monitorTimer.Tick += MonitorTimer_Tick;
        _draftTimer = DispatcherQueue.CreateTimer(); _draftTimer.Interval = TimeSpan.FromSeconds(2); _draftTimer.IsRepeating = false; _draftTimer.Tick += DraftTimer_Tick;
        _sessionTimer = DispatcherQueue.CreateTimer(); _sessionTimer.Interval = TimeSpan.FromSeconds(1); _sessionTimer.Tick += SessionTimer_Tick;
        Closed += (_, _) => { _monitorTimer.Stop(); _draftTimer.Stop(); _sessionTimer.Stop(); _voiceProtection.Dispose(); };
        _voiceProtection.LevelChanged += VoiceProtection_LevelChanged;
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
        _ = LoadVoiceProtectionSettingsAsync();
        _ = LoadCasesAsync();
    }

    private async Task LoadVoiceProtectionSettingsAsync()
    {
        _voiceSettings = await _voiceProtection.LoadAsync();
        ProtectedNumberBox.Text = _voiceSettings.ProtectedNumber;
        ProtectedNumberOwnershipCheck.IsChecked = _voiceSettings.OwnershipConfirmed;
        VoiceProfileBox.SelectedIndex = (int)_voiceSettings.Profile;
        VoiceStrengthSlider.Value = _voiceSettings.Strength;
        NoiseSuppressionToggle.IsOn = _voiceSettings.NoiseSuppressionEnabled;
        VoiceProtectionToggle.IsOn = _voiceSettings.IsEnabled;
        RefreshVirtualOutputs();
        RefreshVoiceProtectionStatus();
    }

    private void RefreshVoiceProtectionStatus()
    {
        var ready = _voiceSettings.IsEnabled && _voiceSettings.OwnershipConfirmed && !string.IsNullOrWhiteSpace(_voiceSettings.ProtectedNumber) && !string.IsNullOrWhiteSpace(_voiceSettings.VirtualMicrophoneOutputId);
        VoiceProtectionStatusText.Text = _voiceOutputActive ? $"PROTECTION ACTIVE · {_voiceSettings.Profile}" : ready ? "PROTECTION READY" : "PROTECTION OFF";
        VoiceProtectionStatusBadge.Background = _voiceOutputActive
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBackgroundBrush"];
    }

    private VoiceProtectionSettings ReadVoiceProtectionSettings() => new(
        ProtectedNumberBox.Text?.Trim() ?? string.Empty,
        ProtectedNumberOwnershipCheck.IsChecked == true,
        Enum.TryParse<VoiceProfile>(VoiceProfileBox.SelectedItem?.ToString(), out var profile) ? profile : VoiceProfile.Neutral,
        (int)Math.Round(VoiceStrengthSlider.Value),
        NoiseSuppressionToggle.IsOn,
        VoiceProtectionToggle.IsOn,
        (VirtualMicrophoneOutputBox.SelectedItem as VirtualMicrophoneOutput)?.Id ?? _voiceSettings.VirtualMicrophoneOutputId);

    private void RefreshVirtualOutputs()
    {
        var outputs = _voiceProtection.FindVirtualMicrophoneOutputs();
        VirtualMicrophoneOutputBox.ItemsSource = outputs;
        VirtualMicrophoneOutputBox.SelectedItem = outputs.FirstOrDefault(output => output.Id == _voiceSettings.VirtualMicrophoneOutputId);
        VirtualMicrophoneOutputStatus.Text = outputs.Count == 0
            ? "No compatible virtual-audio output was detected. Install/configure one yourself; ScamBait Desk does not install drivers."
            : "Choose an output, then select the matching virtual cable recording endpoint as the microphone in your VoIP app.";
    }

    private void RefreshVirtualOutputs_Click(object sender, RoutedEventArgs e) => RefreshVirtualOutputs();

    private static bool IsControlledPhoneNumber(string value)
    {
        var normalized = new string(value.Where(character => char.IsDigit(character) || character == '+').ToArray());
        return normalized.Count(char.IsDigit) is >= 7 and <= 15 && normalized.Count(character => character == '+') <= 1 && (normalized.Length == 0 || normalized[0] != '+' || normalized.IndexOf('+', 1) < 0);
    }

    private async void SaveVoiceProtectionSettings_Click(object sender, RoutedEventArgs e)
    {
        var settings = ReadVoiceProtectionSettings();
        if (!IsControlledPhoneNumber(settings.ProtectedNumber) || !settings.OwnershipConfirmed)
        {
            await ShowMessage("Enter a valid secondary phone number and confirm that you legitimately control it. This feature cannot be used for caller-ID spoofing or impersonation.");
            return;
        }
        _voiceSettings = settings;
        await _voiceProtection.SaveAsync(settings);
        RefreshVoiceProtectionStatus();
        await ShowMessage("Protected-number configuration saved locally.");
    }

    private void VoiceProtectionChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
            return;

        _voiceSettings = ReadVoiceProtectionSettings();
        RefreshVoiceProtectionStatus();
    }

    private void VoiceStrengthSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing)
            return;

        _voiceSettings = ReadVoiceProtectionSettings();
        RefreshVoiceProtectionStatus();
    }

    private void MicrophoneTest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_microphoneTestActive)
            {
                _voiceProtection.StopAudio();
                _microphoneTestActive = false;
                MicrophoneTestButton.Content = "Test microphone";
                MicrophoneTestStatus.Text = "Microphone test is off. No audio is recorded or sent.";
            }
            else
            {
                _voiceSettings = ReadVoiceProtectionSettings();
                _voiceProtection.StartMicrophoneTest(_voiceSettings);
                _microphoneTestActive = true;
                MicrophoneTestButton.Content = "Stop test";
                MicrophoneTestStatus.Text = $"Testing the local {_voiceSettings.Profile} profile at {_voiceSettings.Strength}%. Audio is processed in memory only and is not recorded or sent.";
            }
        }
        catch (Exception exception)
        {
            MicrophoneTestStatus.Text = $"Could not start microphone test: {exception.Message}";
        }
    }

    private async void EmergencyMute_Click(object sender, RoutedEventArgs e)
    {
        _voiceProtection.StopAudio();
        _microphoneTestActive = false;
        _voiceOutputActive = false;
        MicrophoneTestButton.Content = "Test microphone";
        VoiceProtectionToggle.IsOn = false;
        _voiceSettings = ReadVoiceProtectionSettings();
        await _voiceProtection.SaveAsync(_voiceSettings);