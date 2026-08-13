using System.Text.RegularExpressions;

namespace ScamBaitDesk.Services;

public sealed class ConversationSummaryService
{
    public ConversationSummary Summarize(CaseRecord record)
    {
        var received = record.Messages.Where(message => !message.IsOutbound).OrderBy(message => message.ReceivedAt).ToList();
        var facts = new List<ConversationFact>();
        AddMatches(facts, received, "Money request", @"(?:£|\$|€)\s?\d[\d,.]*|\b\d[\d,.]*\s?(?:pounds?|dollars?|euros?)\b");
        AddMatches(facts, received, "Deadline", @"\b(?:today|tomorrow|immediately|within \d+ (?:hours?|days?)|by [A-Za-z]+(?: \d{1,2})?)\b");
        AddMatches(facts, received, "Claimed organisation", @"\b(?:from|represent(?:ing)?|on behalf of)\s+([A-Z][A-Za-z0-9& .'-]{2,40})");
        facts.AddRange(record.SenderClaims.Select(claim => new ConversationFact($"Claim: {claim.Category}", $"{claim.Claim} [{claim.VerificationStatus}]")));
        var contradictions = record.SenderClaims.GroupBy(claim => claim.Category).Where(group => group.Select(claim => claim.Claim).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1).Select(group => $"Multiple different {group.Key.ToLowerInvariant()} claims were recorded.").ToList();
        var unanswered = new List<string>();
        var combined = string.Join(' ', received.Select(message => message.Body));
        if (!Regex.IsMatch(combined, @"\b(registration|company number)\b", RegexOptions.IgnoreCase)) unanswered.Add("What is the organisation's registration number?");
        if (!Regex.IsMatch(combined, @"\b(address|registered office)\b", RegexOptions.IgnoreCase)) unanswered.Add("What is the registered postal address?");
        if (!Regex.IsMatch(combined, @"\b(reference|case number)\b", RegexOptions.IgnoreCase)) unanswered.Add("What reference can be verified independently?");
        var overview = $"{received.Count} received, {record.Messages.Count(message => message.IsOutbound)} synced sent, {record.OutboundMessages.Count} sent through ScamBait Desk, {record.SenderClaims.Count} recorded claims.";
        return new ConversationSummary(overview, facts.Distinct().Take(25).ToList(), contradictions, unanswered);
    }

    private static void AddMatches(List<ConversationFact> facts, IEnumerable<InboxMessage> messages, string category, string pattern)
    {
        foreach (var match in messages.SelectMany(message => Regex.Matches(message.Body, pattern, RegexOptions.IgnoreCase).Select(item => item.Value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8))
            facts.Add(new ConversationFact(category, ScamAnalysisService.Redact(match)));
    }
}
