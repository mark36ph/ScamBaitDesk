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
}

public sealed record RiskSignal(string Label, string Detail, int Weight);

public sealed record AnalysisResult(int Score, string Rating, IReadOnlyList<RiskSignal> Signals, string RedactedText)
{
    public string Summary => $"{Rating} risk · {Score}/100 · {Signals.Count} signal(s)";
}

public sealed record CaseRecord(
    Guid Id,
    DateTimeOffset CreatedAt,
    InboxMessage Message,
    AnalysisResult Analysis,
    string DraftReply,
    string Notes);
