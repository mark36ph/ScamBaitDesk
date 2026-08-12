using System.Text.Json;

namespace ScamBaitDesk.Services;

public sealed class CaseRepository
{
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScamBaitDesk", "Cases");

    public async Task SaveAsync(CaseRecord record)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{record.CreatedAt:yyyyMMdd-HHmmss}-{record.Id:N}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
    }

    public async Task<IReadOnlyList<CaseRecord>> LoadAsync()
    {
        if (!Directory.Exists(_directory)) return [];
        var records = new List<CaseRecord>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json").OrderDescending())
        {
            await using var stream = File.OpenRead(path);
            var record = await JsonSerializer.DeserializeAsync<CaseRecord>(stream);
            if (record is not null) records.Add(record);
        }
        return records;
    }
}
