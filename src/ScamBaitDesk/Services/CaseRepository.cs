using System.Text.Json;

namespace ScamBaitDesk.Services;

public sealed class CaseRepository
{
    public static List<CaseChecklistItem> NewChecklist() =>
    [
        new(Guid.NewGuid(), "Review sender identity and authentication headers", false),
        new(Guid.NewGuid(), "Record payment, urgency, and organisation claims", false),
        new(Guid.NewGuid(), "Extract and review inert indicators", false),
        new(Guid.NewGuid(), "Check the reply for private or real-world details", false),
        new(Guid.NewGuid(), "Export evidence and prepare a report", false),
        new(Guid.NewGuid(), "Stop engagement when the objective is complete", false)
    ];
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScamBaitDesk", "Cases");

    public async Task SaveAsync(CaseRecord record)
    {
        Directory.CreateDirectory(_directory);
        var id = record.Id.ToString("N");
        var path = Directory.EnumerateFiles(_directory, $"*{id}*.json").FirstOrDefault()
            ?? Path.Combine(_directory, $"{id}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
    }

    public async Task ExportAllAsync(Stream destination) =>
        await JsonSerializer.SerializeAsync(destination, await LoadAsync(), new JsonSerializerOptions { WriteIndented = true });

    public async Task<int> ImportAsync(Stream source)
    {
        var imported = await JsonSerializer.DeserializeAsync<List<CaseRecord>>(source) ?? [];
        var existing = (await LoadAsync()).ToDictionary(record => record.Id);
        var saved = 0;
        foreach (var record in imported.Where(record => record.Id != Guid.Empty))
        {
            if (existing.TryGetValue(record.Id, out var current) && current.UpdatedAt > record.UpdatedAt) continue;
            Normalize(record);
            record.UpdatedAt = DateTimeOffset.Now;
            record.Timeline.Add(new CaseEvent(record.UpdatedAt, "Backup import", "Case restored or merged from a local backup file."));
            await SaveAsync(record);
            saved++;
        }
        return saved;
    }

    public async Task<IReadOnlyList<CaseRecord>> LoadAsync()
    {
        if (!Directory.Exists(_directory)) return [];
        var records = new List<CaseRecord>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            var json = await File.ReadAllTextAsync(path);
            using var document = JsonDocument.Parse(json);
            CaseRecord? record;
            if (!document.RootElement.TryGetProperty("Messages", out _) && document.RootElement.TryGetProperty("Message", out _))
            {
                var legacy = JsonSerializer.Deserialize<LegacyCaseRecord>(json);
                record = legacy is null ? null : new CaseRecord
                {
                    Id = legacy.Id,
                    CreatedAt = legacy.CreatedAt,
                    UpdatedAt = legacy.CreatedAt,
                    Title = legacy.Message.Subject,
                    Messages = [legacy.Message],
                    Analysis = legacy.Analysis,
                    DraftReply = legacy.DraftReply,
                    Notes = legacy.Notes,
                    Timeline = [new CaseEvent(legacy.CreatedAt, "Imported", "Migrated from the original case format.")]
                };
                if (record is not null) await SaveAsync(record);
            }
            else record = JsonSerializer.Deserialize<CaseRecord>(json);
            if (record is not null)
            {
                Normalize(record);
                records.Add(record);
            }
        }
        return records.OrderByDescending(record => record.UpdatedAt).ToList();
    }

    private static void Normalize(CaseRecord record)
    {
        record.Messages ??= [];
        record.Timeline ??= [];
        record.OutboundMessages ??= [];
        record.Priority = string.IsNullOrWhiteSpace(record.Priority) ? "Normal" : record.Priority;
        record.Tags ??= [];
        record.EngagementStopReason ??= string.Empty;
        record.Reminders ??= [];
        record.Calls ??= [];
        record.Checklist ??= [];
        if (record.Checklist.Count == 0) record.Checklist = NewChecklist();
        record.EngagementStage ??= "Initial review";
        record.EngagementObjective ??= "Request independently verifiable information";
        record.SenderClaims ??= [];
        record.ImportedIndicators ??= [];
        if (record.OutboundMessageBudget <= 0) record.OutboundMessageBudget = 10;
    }

    private sealed record LegacyCaseRecord(
        Guid Id,
        DateTimeOffset CreatedAt,
        InboxMessage Message,
        AnalysisResult Analysis,
        string DraftReply,
        string Notes);
}
