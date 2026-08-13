using System.Text.RegularExpressions;

namespace ScamBaitDesk.Services;

public sealed class ConversationCoachService
{
    private sealed record PlaybookRule(string Name, string[] Patterns);

    private static readonly PlaybookRule[] PlaybookRules =
    [
        new("Parcel or delivery scam", [@"\b(parcel|delivery|courier|postage|customs fee|redelivery)\b"]),
        new("Advance-fee or inheritance scam", [@"\b(inheritance|beneficiary|lottery|prize|advance fee|release fee|processing fee)\b"]),
        new("Romance or relationship scam", [@"\b(love|relationship|deployed|widow(?:ed)?|come to visit|hospital emergency)\b"]),
        new("Fake technical support", [@"\b(anydesk|teamviewer|quick assist|remote access|virus|technical support)\b"]),
        new("Investment or crypto scam", [@"\b(investment|trading platform|guaranteed return|profit|bitcoin|crypto|USDT|wallet)\b"]),
        new("Job or task scam", [@"\b(job offer|recruiter|vacancy|task job|commission|equipment cheque|work from home)\b"]),
        new("Organisation impersonation", [@"\b(bank|police|HMRC|tax office|government|fraud department|security team)\b"])
    ];

    public EngagementPlaybook RecommendPlaybook(CaseRecord record, IReadOnlyList<EngagementPlaybook> playbooks)
    {
        var text = ConversationText(record);
        var detected = PlaybookRules
            .Select(rule => new { Rule = rule, Score = rule.Patterns.Sum(pattern => Math.Min(10, Regex.Matches(text, pattern, RegexOptions.IgnoreCase).Count)) })
            .OrderByDescending(item => item.Score).FirstOrDefault(item => item.Score > 0)?.Rule.Name;
        return playbooks.FirstOrDefault(playbook => playbook.Name.Equals(detected, StringComparison.OrdinalIgnoreCase))
            ?? playbooks.First(playbook => playbook.Name == "Identity verification");
    }

    public IReadOnlyList<ConversationMemoryItem> BuildMemory(CaseRecord record)
    {
        var memory = new List<ConversationMemoryItem>();
        foreach (var claim in record.SenderClaims.OrderByDescending(item => item.RecordedAt))
            Add(memory, $"Claim: {claim.Category}", claim.Claim, "Claim ledger", claim.VerificationStatus);

        var inbound = string.Join("\n", record.Messages.Where(message => !message.IsOutbound).Select(message => message.Body));
        AddMatches(memory, inbound, "Money requested", @"(?:£|\$|€)\s?\d[\d,.]*|\b\d[\d,.]*\s?(?:pounds?|dollars?|euros?)\b", "Received messages");
        AddMatches(memory, inbound, "Deadline or pressure", @"\b(?:today|tomorrow|immediately|urgent|within \d+ (?:hours?|days?)|by [A-Za-z]+(?: \d{1,2})?)\b", "Received messages");
        AddMatches(memory, inbound, "Reference", @"\b(?:reference|case|invoice|claim)\s*(?:number|no\.?|#|:)?\s*[A-Z0-9-]{4,24}\b", "Received messages");
        AddMatches(memory, inbound, "Organisation wording", @"\b(?:from|represent(?:ing)?|on behalf of)\s+([A-Z][A-Za-z0-9& .'-]{2,45})", "Received messages");
        foreach (var message in record.Messages.Where(message => !message.IsOutbound).OrderByDescending(message => message.ReceivedAt).Take(4))
            Add(memory, "Sender address", ScamAnalysisService.Redact(message.Sender), message.ReceivedDisplay, "Observed");
        return memory.Take(30).ToList();
    }

    public IReadOnlyList<PlannedQuestion> PlanQuestions(CaseRecord record)
    {
        var conversation = string.Join("\n", record.Messages.Where(message => !message.IsOutbound).Select(message => message.Body));
        var outbound = string.Join("\n", record.Messages.Where(message => message.IsOutbound).Select(message => message.Body)
            .Concat(record.OutboundMessages.Select(message => message.RedactedBody)));
        var candidates = new List<(string Priority, string Category, string Question, string Reason, string[] AnswerMarkers, string[] AskedMarkers)>
        {
            ("1 · High", "Identity", "What is the organisation's full legal name and registration number?", "Establishes an identity that can be checked independently.", ["registration", "company number"], ["legal name", "registration number"]),
            ("2 · High", "Verification", "What case or reference number can I verify through contact details published independently?", "Creates a reference without using links or numbers supplied by the sender.", ["case number", "reference number", "reference:"], ["verify independently", "case or reference"]),
            ("3 · Medium", "Contact", "Which department are you contacting me from, and what is the public switchboard number?", "Provides a route that can be compared with an independently sourced number.", ["department", "switchboard"], ["which department", "public switchboard"]),
            ("4 · Medium", "Payment", "Please provide an itemised written explanation of the amount and its contractual basis.", "Documents the payment story without promising or sending money.", ["itemised", "contractual basis"], ["itemised", "contractual basis"]),
            ("5 · Medium", "Location", "What is the organisation's registered postal address and jurisdiction?", "A claimed legal entity should have independently verifiable registration details.", ["registered office", "postal address", "jurisdiction"], ["registered postal address", "jurisdiction"]),
            ("6 · Low", "Timeline", "What exact date was this matter opened, and when does the stated deadline expire?", "Pins down changing urgency claims for later comparison.", ["opened on", "deadline", "expires"], ["exact date", "deadline expire"])
        };
        return candidates
            .Where(candidate => !candidate.AnswerMarkers.Any(marker => conversation.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .Where(candidate => !candidate.AskedMarkers.Any(marker => outbound.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .Select(candidate => new PlannedQuestion(candidate.Priority, candidate.Category, candidate.Question, candidate.Reason)).ToList();
    }

    public IReadOnlyList<ReplyCoachOption> SuggestReplies(CaseRecord record)
    {
        var questions = PlanQuestions(record);
        var first = questions.FirstOrDefault()?.Question ?? "Please clarify the reference and the independently verifiable details supporting your claim.";
        var contradicted = record.SenderClaims.Count(claim => claim.VerificationStatus is "Contradicted" or "Inconsistent");
        var options = new List<ReplyCoachOption>
        {
            new("Verify one claim", "Obtain one independently checkable detail", $"Thanks for the update. {first}", "Asks one focused question and avoids giving the sender new personal information.", "Verify any answer using contact details you find independently; do not use their links or telephone numbers."),
            new("Slow and document", "Reduce pressure and request written evidence", "I cannot take action or make a payment based on this message. Please provide the complete explanation in writing, including the legal entity, reference, and itemised basis for the request.", "Creates time and a written record without promising payment.", "Do not invent delays involving real banks, illnesses, authorities, or other people."),
            new("Set a boundary", "Continue only with verifiable information", "I will not provide passwords, security codes, financial details, remote access, or payment. If this is legitimate, please provide information that can be verified through independently published sources.", "States the safety boundary explicitly and redirects the conversation toward verification.", "End engagement if the sender persists with secrets, threats, remote access, or payment demands."),
            new("End and preserve", "Stop the conversation safely", "Do not contact this address again. Further messages will be retained as evidence and reported through the appropriate channels.", "Closes an unproductive or unsafe exchange without threats or impersonation.", "Use the permanent Stop engagement control after choosing this option.")
        };
        if (contradicted > 0)
            options.Insert(1, new("Clarify contradiction", "Ask the sender to reconcile changing claims", $"The information provided so far is inconsistent in {contradicted} recorded area(s). Please explain the differences in writing and provide one independently verifiable reference.", "Surfaces recorded inconsistencies without disclosing how they were investigated.", "Do not reveal private research data or accuse the sender of a crime."));
        return options;
    }

    private static string ConversationText(CaseRecord record) => string.Join("\n", record.Messages.Where(message => !message.IsOutbound).Select(message => $"{message.Subject}\n{message.Body}"));

    private static void AddMatches(List<ConversationMemoryItem> memory, string text, string category, string pattern, string source)
    {
        foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase).Cast<Match>().Where(match => match.Success).Take(8))
            Add(memory, category, ScamAnalysisService.Redact(match.Value), source, "Observed");
    }

    private static void Add(List<ConversationMemoryItem> memory, string category, string value, string source, string status)
    {
        if (string.IsNullOrWhiteSpace(value) || memory.Any(item => item.Category == category && item.Value.Equals(value, StringComparison.OrdinalIgnoreCase))) return;
        memory.Add(new ConversationMemoryItem(category, value.Trim(), source, status));
    }
}
