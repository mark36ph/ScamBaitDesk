namespace ScamBaitDesk;

public enum MailAuthentication { AppPassword, GmailOAuth }

public sealed record InboxSettings(
    string Host,
    int Port,
    string Username,
    string SmtpHost = "",
    int SmtpPort = 587,
    bool SmtpUseSsl = false,
    MailAuthentication Authentication = MailAuthentication.AppPassword,
    string OAuthClientId = "");

public sealed record InboxMessage(
    string Id,
    string Subject,
    string Sender,
    DateTimeOffset ReceivedAt,
    string Body)
{
    public string ReceivedDisplay => ReceivedAt.LocalDateTime.ToString("g");
    public string ConversationKey => ConversationService.GetKey(this);
    public Dictionary<string, List<string>> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AttachmentRecord> Attachments { get; init; } = [];
    public bool IsOutbound { get; init; }
    public string Recipient { get; init; } = string.Empty;
    public string DirectionDisplay => IsOutbound ? "Sent" : "Received";
}

public sealed record AttachmentRecord(string FileName, string MediaType, long? Size, string ContentId)
{
    public string SizeDisplay => Size is long size ? $"{size:N0} bytes" : "Size unavailable";
    public string Display => $"{FileName} · {MediaType} · {SizeDisplay}";
}

public sealed record RiskSignal(string Label, string Detail, int Weight);

public sealed record AnalysisResult(int Score, string Rating, IReadOnlyList<RiskSignal> Signals, string RedactedText)
{
    public string Summary => $"{Rating} risk · {Score}/100 · {Signals.Count} signal(s)";
}

public enum CaseStatus { New, Investigating, AwaitingVerification, Reported, Closed }

public sealed record CaseEvent(DateTimeOffset At, string Kind, string Detail)
{
    public string Display => $"{At.LocalDateTime:g} · {Kind} — {Detail}";
}

public sealed record OutboundMessageRecord(
    DateTimeOffset SentAt,
    string Recipient,
    string Subject,
    string RedactedBody,
    string MessageId)
{
    public string Display => $"{SentAt.LocalDateTime:g} · To {Recipient} · {Subject}";
}

public sealed record PrivacyFinding(string Label, string Detail, bool BlocksSend)
{
    public string Display => $"{(BlocksSend ? "BLOCKED" : "Review")} · {Label}: {Detail}";
}

public sealed record PrivacyReview(IReadOnlyList<PrivacyFinding> Findings)
{
    public bool CanSend => Findings.All(finding => !finding.BlocksSend);
    public string Summary => Findings.Count == 0
        ? "No obvious personal or high-risk data detected."
        : $"{Findings.Count} finding(s) · {Findings.Count(finding => finding.BlocksSend)} blocking";
}

public sealed class PersonaProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Fictional persona";
    public string TimeZone { get; set; } = "Europe/London";
    public string Backstory { get; set; } = string.Empty;
    public string SafeDetails { get; set; } = string.Empty;
    public string Display => $"{Name} · {TimeZone}";
}

public sealed record ReplyTemplate(string Name, string ScamType, string Body)
{
    public string Display => $"{Name} · {ScamType}";
}

public sealed record FollowUpReminder(Guid Id, DateTimeOffset DueAt, string Note, bool Completed)
{
    public string Display => $"{DueAt.LocalDateTime:g} · {(Completed ? "Done" : DueAt <= DateTimeOffset.Now ? "DUE" : "Pending")} · {Note}";
}

public sealed record DuplicateCaseMatch(Guid CaseId, string Title, int Score, string Reason)
{
    public string Display => $"{Score}% · {Title} — {Reason}";
}

public sealed record DashboardMetric(string Label, string Value);

public sealed record ActivityItem(DateTimeOffset At, string CaseTitle, string Kind, string Detail)
{
    public string Display => $"{At.LocalDateTime:g} · {CaseTitle} · {Kind} — {Detail}";
}

public sealed record WebsiteFinding(
    string Label,
    string Detail,
    int Weight,
    string Category = "Address structure",
    string Evidence = "",
    string Recommendation = "")
{
    public string Severity => Weight switch { >= 25 => "High", >= 15 => "Medium", _ => "Low" };
    public string SeverityDisplay => $"{Severity} concern · +{Weight} points · {Category}";
    public string EvidenceDisplay => $"Evidence: {(string.IsNullOrWhiteSpace(Evidence) ? "Detected from the submitted website address." : Evidence)}";
    public string RecommendationDisplay => $"Recommended action: {(string.IsNullOrWhiteSpace(Recommendation) ? "Verify the domain independently and do not enter personal, login, or payment information based on this page." : Recommendation)}";
    public string Display => $"{Label} · {SeverityDisplay} — {Detail}";
}

public sealed record WebsiteCheckResult(string NormalizedUrl, string Host, int Score, string Rating, IReadOnlyList<WebsiteFinding> Findings)
{
    public string Summary => $"{Rating} · {Score}/100 · {Findings.Count} local signal(s)";
}

public sealed record WebsiteLiveScanResult(string FinalUrl, string PageTitle, int DownloadBytes, int RedirectCount, IReadOnlyList<WebsiteFinding> Findings)
{
    public int Score => Math.Min(100, Findings.Sum(finding => finding.Weight));
    public string Rating => Score switch { >= 55 => "High concern", >= 25 => "Suspicious", >= 10 => "Review advised", _ => "No obvious page-content warning" };
    public string Summary => $"{Rating} · {Score}/100 · {Findings.Count} live content signal(s)";
}

public sealed record SenderClaim(Guid Id, DateTimeOffset RecordedAt, string Category, string Claim, string VerificationStatus)
{
    public string Display => $"{Category} · {VerificationStatus} — {Claim}";
}

public sealed record SafeQuestion(string Category, string Text)
{
    public string Display => $"{Category} · {Text}";
}

public sealed record ConversationMemoryItem(string Category, string Value, string Source, string Status)
{
    public string Display => $"{Category} · {Value} — {Status} · {Source}";
}

public sealed record PlannedQuestion(string Priority, string Category, string Question, string Reason)
{
    public string Display => $"{Priority} · {Category} — {Question}";
}

public sealed record ReplyCoachOption(string Name, string Objective, string Draft, string Rationale, string Caution)
{
    public string Display => $"{Name} · {Objective}";
    public string Guidance => $"Why: {Rationale}\nSafety note: {Caution}";
}

public sealed record EngagementPlaybook(string Name, string Stage, string Objective, IReadOnlyList<string> Steps)
{
    public string Display => $"{Name} · {Stage}";
}

public sealed record CaseChecklistItem(Guid Id, string Label, bool Completed)
{
    public string Display => $"{(Completed ? "✓" : "○")} {Label}";
}

public sealed record NextActionItem(string Priority, string CaseTitle, string Action, Guid CaseId)
{
    public string Display => $"{Priority} · {CaseTitle} — {Action}";
}

public sealed record ProvenanceIndicator(Guid Id, DateTimeOffset AddedAt, string Value, string Source, string EvidenceNote)
{
    public string Display => $"{Value} · source: {Source}";
}

public sealed record ConversationFact(string Category, string Value)
{
    public string Display => $"{Category} — {Value}";
}

public sealed record ConversationSummary(string Overview, IReadOnlyList<ConversationFact> Facts, IReadOnlyList<string> Contradictions, IReadOnlyList<string> UnansweredQuestions);

public sealed record ConnectionDiagnostic(string Component, bool Success, string Detail)
{
    public string Display => $"{(Success ? "PASS" : "FAIL")} · {Component} — {Detail}";
}

public sealed record CallLogRecord(Guid Id, DateTimeOffset At, string Number, string Outcome, string Notes, bool RecordingConsentConfirmed)
{
    public string Display => $"{At.LocalDateTime:g} · {Outcome} · {Number}{(RecordingConsentConfirmed ? " · recording consent confirmed" : string.Empty)}";
}

public sealed record ForensicFinding(string Label, string Value, string Assessment)
{
    public string Display => $"{Label}: {Value}";
}

public sealed record EmailForensicsReport(
    string Verdict,
    IReadOnlyList<ForensicFinding> Findings,
    IReadOnlyList<string> Warnings,
    string RawHeaders)
{
    public string Summary => $"{Verdict} · {Warnings.Count} warning(s)";
}

public enum IndicatorType { Url, Domain, Email, IpAddress, Phone, CryptoWallet, PaymentHandle, AccountNumber }

public sealed record IndicatorRecord(
    IndicatorType Type,
    string Value,
    int Occurrences,
    IReadOnlyList<string> Sources)
{
    public string TypeDisplay => Type switch
    {
        IndicatorType.IpAddress => "IP address",
        IndicatorType.CryptoWallet => "Crypto wallet",
        IndicatorType.PaymentHandle => "Payment handle",
        IndicatorType.AccountNumber => "Account-like number",
        _ => Type.ToString()
    };
    public string OccurrenceDisplay => $"{Occurrences} occurrence(s) · {Sources.Count} message(s)";
    public string PrimarySource => Sources.FirstOrDefault() ?? "Unknown source";
    public bool SupportsLookup => Type is IndicatorType.Url or IndicatorType.Domain or IndicatorType.IpAddress;
}

public sealed class CaseRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string Title { get; set; } = "Untitled case";
    public CaseStatus Status { get; set; } = CaseStatus.New;
    public string Priority { get; set; } = "Normal";
    public List<string> Tags { get; set; } = [];
    public List<InboxMessage> Messages { get; set; } = [];
    public AnalysisResult? Analysis { get; set; }
    public string DraftReply { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public List<CaseEvent> Timeline { get; set; } = [];
    public List<OutboundMessageRecord> OutboundMessages { get; set; } = [];
    public Guid? PersonaId { get; set; }
    public bool EngagementStopped { get; set; }
    public DateTimeOffset? EngagementStoppedAt { get; set; }
    public string EngagementStopReason { get; set; } = string.Empty;
    public List<FollowUpReminder> Reminders { get; set; } = [];
    public List<CallLogRecord> Calls { get; set; } = [];
    public List<CaseChecklistItem> Checklist { get; set; } = [];
    public long EngagementSeconds { get; set; }
    public string EngagementStage { get; set; } = "Initial review";
    public string EngagementObjective { get; set; } = "Request independently verifiable information";
    public int OutboundMessageBudget { get; set; } = 10;
    public DateTimeOffset? EngagementDeadline { get; set; }
    public List<SenderClaim> SenderClaims { get; set; } = [];
    public List<ProvenanceIndicator> ImportedIndicators { get; set; } = [];
    public string StatusDisplay => Status switch
    {
        CaseStatus.AwaitingVerification => "Awaiting verification",
        _ => Status.ToString()
    };
    public string UpdatedDisplay => UpdatedAt.LocalDateTime.ToString("g");
    public string Summary => $"{Priority} priority · {StatusDisplay}{(EngagementStopped ? " · STOPPED" : string.Empty)} · {Messages.Count} message(s) · updated {UpdatedDisplay}";
}

public static class ConversationService
{
    public static string GetKey(InboxMessage message)
    {
        var sender = (message.IsOutbound ? message.Recipient : message.Sender).Trim().ToLowerInvariant();
        var subject = System.Text.RegularExpressions.Regex.Replace(
            message.Subject, @"^\s*((re|fw|fwd)\s*:\s*)+", string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim().ToLowerInvariant();
        return $"{sender}|{subject}";
    }

    public static IReadOnlyList<InboxMessage> FindConversation(
        InboxMessage selected, IEnumerable<InboxMessage> inbox) =>
        inbox.Where(message => message.ConversationKey == selected.ConversationKey)
            .OrderBy(message => message.ReceivedAt).ToList();
}
