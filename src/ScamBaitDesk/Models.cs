namespace ScamBaitDesk;

public sealed record InboxSettings(string Host, int Port, string Username);

public sealed record InboxMessage(
    string Id,
    string Subject,
    string Sender,
    DateTimeOffset ReceivedAt,
    string Body)
{
    public string ReceivedDisplay => ReceivedAt.LocalDateTime.ToString("g");
    public string ConversationKey => ConversationService.GetKey(this);
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

public sealed class CaseRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string Title { get; set; } = "Untitled case";
    public CaseStatus Status { get; set; } = CaseStatus.New;
    public List<InboxMessage> Messages { get; set; } = [];
    public AnalysisResult? Analysis { get; set; }
    public string DraftReply { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public List<CaseEvent> Timeline { get; set; } = [];
    public string StatusDisplay => Status switch
    {
        CaseStatus.AwaitingVerification => "Awaiting verification",
        _ => Status.ToString()
    };
    public string UpdatedDisplay => UpdatedAt.LocalDateTime.ToString("g");
    public string Summary => $"{StatusDisplay} · {Messages.Count} message(s) · updated {UpdatedDisplay}";
}

public static class ConversationService
{
    public static string GetKey(InboxMessage message)
    {
        var sender = message.Sender.Trim().ToLowerInvariant();
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
