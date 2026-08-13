using System.Text;
using System.Text.Json;

namespace ScamBaitDesk.Services;

public sealed class EngagementWorkspaceService
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScamBaitDesk", "personas.json");

    public IReadOnlyList<ReplyTemplate> Templates { get; } =
    [
        new("Request verifiable details", "General", "Thanks for the message. Before I continue, please provide your organisation's full legal name, public switchboard number, postal address, and a reference I can verify independently."),
        new("Slow the payment request", "Payment", "I cannot make a payment yet. Please explain the charge in writing, provide an itemised invoice, and tell me where the terms can be verified on your organisation's public website."),
        new("Question account warning", "Account alert", "I will not use links or telephone numbers from this message. Please provide the case reference, the department name, and the public webpage where I can independently verify this notice."),
        new("Question prize or inheritance", "Advance fee", "Please provide the legal entity handling this matter, its registration number, the jurisdiction involved, and public contact details that I can verify independently. I will not pay an advance fee."),
        new("End the conversation", "Stop", "Do not contact this address again. Further messages will be retained as evidence and reported through the appropriate channels.")
    ];

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
