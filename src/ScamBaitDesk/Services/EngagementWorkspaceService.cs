using System.Text;
using System.Text.Json;

namespace ScamBaitDesk.Services;

public sealed class EngagementWorkspaceService
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScamBaitDesk", "personas.json");
    private readonly string _templatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScamBaitDesk", "reply-templates.json");

    public IReadOnlyList<ReplyTemplate> Templates { get; } =
    [
        new("Request verifiable details", "General", "Thanks for the message. Before I continue, please provide your organisation's full legal name, public switchboard number, postal address, and a reference I can verify independently."),
        new("Slow the payment request", "Payment", "I cannot make a payment yet. Please explain the charge in writing, provide an itemised invoice, and tell me where the terms can be verified on your organisation's public website."),
        new("Question account warning", "Account alert", "I will not use links or telephone numbers from this message. Please provide the case reference, the department name, and the public webpage where I can independently verify this notice."),
        new("Question prize or inheritance", "Advance fee", "Please provide the legal entity handling this matter, its registration number, the jurisdiction involved, and public contact details that I can verify independently. I will not pay an advance fee."),
        new("End the conversation", "Stop", "Do not contact this address again. Further messages will be retained as evidence and reported through the appropriate channels.")
    ];

    public IReadOnlyList<SafeQuestion> Questions { get; } =
    [
        new("Identity", "What is the organisation's full legal name and registration number?"),
        new("Identity", "Which department are you contacting me from, and what is the public switchboard number?"),
        new("Verification", "What reference can I verify through contact details published independently?"),
        new("Payment", "Please provide an itemised explanation of the amount and the contractual basis for it."),
        new("Payment", "Why is the requested payment method different from the organisation's published payment process?"),
        new("Timeline", "What exact date was this matter opened, and when does your stated deadline expire?"),
        new("Location", "What is the organisation's registered postal address and jurisdiction?"),
        new("Account alert", "Which public webpage explains this alert without requiring me to use a link from your message?")
    ];

    public IReadOnlyList<EngagementPlaybook> Playbooks { get; } =
    [
        new("Identity verification", "Verification questions", "Obtain independently verifiable organisation and identity details", ["Ask for the full legal entity and registration number", "Request a public switchboard and department", "Verify through independently sourced contact details", "Record discrepancies in the claim ledger"]),
        new("Payment evidence", "Claims under review", "Document the payment story without sending money or financial details", ["Request an itemised written explanation", "Record the requested payment method and recipient", "Compare it with the organisation's published process", "Stop if asked for credentials, codes, or real financial data"]),
        new("Delay and observe", "Awaiting response", "Slow the conversation while preserving evidence and boundaries", ["Use one neutral clarification question", "Do not promise payment or access links", "Schedule a manual follow-up reminder", "Keep all outbound activity within the case budget"]),
        new("Prepare to report", "Ready to report", "Consolidate evidence and end engagement safely", ["Review redactions and message headers", "Mark contradicted claims", "Generate the local reporting draft", "Export the hashed evidence package", "Stop engagement permanently"])
    ];

    public static PrivacyReview CheckPersonaConsistency(string draft, PersonaProfile? persona)
    {
        if (persona is null) return new PrivacyReview([new PrivacyFinding("Persona not assigned", "Assign a fictional persona before engaging so replies remain consistent.", false)]);
        var findings = new List<PrivacyFinding>();
        if (!string.IsNullOrWhiteSpace(persona.Name) && !draft.Contains(persona.Name, StringComparison.OrdinalIgnoreCase))
            findings.Add(new PrivacyFinding("Persona signature", $"The assigned persona is {persona.Name}; check the sign-off and point of view.", false));
        foreach (var line in persona.SafeDetails.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var marker = line.Split(':', 2)[0].Trim();
            if (marker.Length > 2 && draft.Contains(marker, StringComparison.OrdinalIgnoreCase) && !draft.Contains(line.Trim(), StringComparison.OrdinalIgnoreCase))
                findings.Add(new PrivacyFinding("Possible persona contradiction", $"Review the stored fictional detail: {line.Trim()}", false));
        }
        return new PrivacyReview(findings);
    }

    public async Task<IReadOnlyList<PersonaProfile>> LoadPersonasAsync()
    {
        if (!File.Exists(_path)) return [];
        return JsonSerializer.Deserialize<List<PersonaProfile>>(await File.ReadAllTextAsync(_path)) ?? [];
    }

    public async Task SavePersonaAsync(PersonaProfile persona)
    {
        var personas = (await LoadPersonasAsync()).ToList();
        var index = personas.FindIndex(item => item.Id == persona.Id);
        if (index >= 0) personas[index] = persona; else personas.Add(persona);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(personas, new JsonSerializerOptions { WriteIndented = true }));
    }

    public async Task<IReadOnlyList<ReplyTemplate>> LoadAllTemplatesAsync()
    {
        if (!File.Exists(_templatePath)) return Templates;
        var local = JsonSerializer.Deserialize<List<ReplyTemplate>>(await File.ReadAllTextAsync(_templatePath)) ?? [];
        return Templates.Concat(local).ToList();
    }

    public async Task SaveTemplateAsync(ReplyTemplate template)
    {
        var local = File.Exists(_templatePath)
            ? JsonSerializer.Deserialize<List<ReplyTemplate>>(await File.ReadAllTextAsync(_templatePath)) ?? []
            : [];
        local.Add(template);
        Directory.CreateDirectory(Path.GetDirectoryName(_templatePath)!);
        await File.WriteAllTextAsync(_templatePath, JsonSerializer.Serialize(local, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string BuildReport(CaseRecord record, string destination)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"SCAM REPORT — {destination}");
        builder.AppendLine($"Case ID: {record.Id}");
        builder.AppendLine($"Created: {record.CreatedAt:u}");
        builder.AppendLine($"Status: {record.StatusDisplay}");
        builder.AppendLine($"Risk assessment: {record.Analysis?.Summary ?? "Not available"}");
        builder.AppendLine($"Messages received: {record.Messages.Count}");
        builder.AppendLine($"Replies sent: {record.OutboundMessages.Count}");
        builder.AppendLine();
        builder.AppendLine("SUMMARY");
        builder.AppendLine(ScamAnalysisService.Redact(record.Notes));
        builder.AppendLine();
        builder.AppendLine("MESSAGE SUBJECTS AND DATES");
        foreach (var message in record.Messages.OrderBy(item => item.ReceivedAt))
            builder.AppendLine($"- {message.ReceivedAt:u} — {ScamAnalysisService.Redact(message.Subject)} — from {ScamAnalysisService.Redact(message.Sender)}");
        builder.AppendLine();
        builder.AppendLine("Attach the app's evidence ZIP separately if the reporting channel permits it. Review this redacted draft before submission.");
        return builder.ToString();
    }
}
