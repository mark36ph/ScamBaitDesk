using System.Text.RegularExpressions;

namespace ScamBaitDesk.Services;

public sealed class CaseIntelligenceService
{
    public IReadOnlyList<DuplicateCaseMatch> FindDuplicates(CaseRecord current, IEnumerable<CaseRecord> records)
    {
        var currentTokens = Tokens(current.Title + " " + string.Join(' ', current.Messages.Select(message => message.Sender)));
        return records.Where(record => record.Id != current.Id).Select(record =>
        {
            var other = Tokens(record.Title + " " + string.Join(' ', record.Messages.Select(message => message.Sender)));
            var union = currentTokens.Union(other).Count();
            var score = union == 0 ? 0 : (int)Math.Round(100d * currentTokens.Intersect(other).Count() / union);
            var senderMatch = current.Messages.Select(message => message.Sender).Intersect(record.Messages.Select(message => message.Sender), StringComparer.OrdinalIgnoreCase).Any();
            if (senderMatch) score = Math.Max(score, 80);
            return new DuplicateCaseMatch(record.Id, record.Title, score, senderMatch ? "same sender" : "similar subject terms");
        }).Where(match => match.Score >= 35).OrderByDescending(match => match.Score).ToList();
    }

    public IReadOnlyList<DashboardMetric> Dashboard(IEnumerable<CaseRecord> records)
    {
        var cases = records.ToList();
        return
        [
            new("Active cases", cases.Count(item => item.Status != CaseStatus.Closed).ToString()),
            new("High risk", cases.Count(item => item.Analysis?.Score >= 70).ToString()),
            new("Replies sent", cases.Sum(item => item.OutboundMessages.Count).ToString()),
            new("Stopped", cases.Count(item => item.EngagementStopped).ToString()),
            new("Reminders due", cases.Sum(item => item.Reminders.Count(reminder => !reminder.Completed && reminder.DueAt <= DateTimeOffset.Now)).ToString())
        ];
    }

    public IReadOnlyList<NextActionItem> NextActions(IEnumerable<CaseRecord> records) => records.SelectMany(record =>
    {
        var actions = new List<NextActionItem>();
        if (record.EngagementStopped || record.Status == CaseStatus.Closed) return actions;
        if (record.Reminders.Any(reminder => !reminder.Completed && reminder.DueAt <= DateTimeOffset.Now)) actions.Add(new("High", record.Title, "Follow-up reminder is due", record.Id));
        if (record.Analysis?.Score >= 70 && record.Status != CaseStatus.Reported) actions.Add(new("High", record.Title, "Review evidence and prepare a report", record.Id));
        if (record.SenderClaims.GroupBy(claim => claim.Category).Any(group => group.Select(claim => claim.Claim).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)) actions.Add(new("Medium", record.Title, "Review potentially contradictory sender claims", record.Id));
        if (record.PersonaId is null && record.OutboundMessages.Count == 0) actions.Add(new("Medium", record.Title, "Assign a fictional persona before engaging", record.Id));
        if (record.OutboundMessages.Count >= record.OutboundMessageBudget) actions.Add(new("High", record.Title, "Reply budget exhausted; end or review the plan", record.Id));
        return actions;
    }).OrderBy(action => action.Priority == "High" ? 0 : 1).ThenBy(action => action.CaseTitle).ToList();

    public string SuggestLocalReply(CaseRecord record)
    {
        var last = record.Messages.OrderByDescending(message => message.ReceivedAt).FirstOrDefault(message => !message.IsOutbound);
        var opening = last is null ? "Thanks for the message." : $"Thanks for your message about {ScamAnalysisService.Redact(last.Subject)}.";
        var contradictory = record.SenderClaims.GroupBy(claim => claim.Category).FirstOrDefault(group => group.Select(claim => claim.Claim).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
        if (contradictory is not null) return $"{opening}\n\nI need clarification because the information provided about {contradictory.Key.ToLowerInvariant()} does not appear consistent. Please state the correct details and provide a reference I can verify independently.";
        if (record.Messages.Any(message => message.Body.Contains("payment", StringComparison.OrdinalIgnoreCase) || message.Body.Contains("fee", StringComparison.OrdinalIgnoreCase))) return $"{opening}\n\nBefore I consider any payment, please provide an itemised invoice, your organisation's full legal name, and public contact details I can verify independently.";
        return $"{opening}\n\nPlease provide the organisation's full legal name, registration number, public switchboard, and a reference I can verify independently. I will not use links or contact details supplied only in this conversation.";
    }

    private static HashSet<string> Tokens(string text) => Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9]{3,}")
        .Select(match => match.Value).Where(value => !StopWords.Contains(value)).ToHashSet();
    private static readonly HashSet<string> StopWords = ["the", "and", "from", "your", "this", "with", "re", "fwd"];
}
