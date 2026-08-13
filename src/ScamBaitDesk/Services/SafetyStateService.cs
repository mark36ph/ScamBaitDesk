using System.Text.Json;

namespace ScamBaitDesk.Services;

public sealed class SafetyStateService
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScamBaitDesk", "safety.json");
    public async Task<bool> IsEmergencyStopEnabledAsync() => File.Exists(_path) && (JsonSerializer.Deserialize<SafetyState>(await File.ReadAllTextAsync(_path))?.EmergencyStop ?? false);
    public async Task SetEmergencyStopAsync(bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(new SafetyState(enabled, DateTimeOffset.Now), new JsonSerializerOptions { WriteIndented = true }));
    }
    private sealed record SafetyState(bool EmergencyStop, DateTimeOffset ChangedAt);
}
