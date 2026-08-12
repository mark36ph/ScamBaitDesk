using System.Text.Json;

namespace ScamBaitDesk.Services;

public sealed class CaseRepository
{
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
                record.OutboundMessages ??= [];
                records.Add(record);
            }
        }
        return records.OrderByDescending(record => record.UpdatedAt).ToList();
    }

    private sealed record LegacyCaseRecord(
        Guid Id,
        DateTimeOffset CreatedAt,
        InboxMessage Message,
        AnalysisResult Analysis,
        string DraftReply,
        string Notes);
}
