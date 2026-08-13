using System.Security.Cryptography;
using System.Text;

namespace ScamBaitDesk.Services;

public sealed class DraftRecoveryService
{
    private readonly string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScamBaitDesk", "DraftRecovery");
    public async Task SaveAsync(string key, string draft)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(PathFor(key), draft);
    }
    public async Task<string?> LoadAsync(string key) => File.Exists(PathFor(key)) ? await File.ReadAllTextAsync(PathFor(key)) : null;
    public void Delete(string key) { var path = PathFor(key); if (File.Exists(path)) File.Delete(path); }
    private string PathFor(string key) => Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant() + ".txt");
}
