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

    private static HashSet<string> Tokens(string text) => Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9]{3,}")
        .Select(match => match.Value).Where(value => !StopWords.Contains(value)).ToHashSet();
    private static readonly HashSet<string> StopWords = ["the", "and", "from", "your", "this", "with", "re", "fwd"];
}
