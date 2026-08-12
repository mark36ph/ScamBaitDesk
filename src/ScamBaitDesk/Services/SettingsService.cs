using System.Text.Json;
using Windows.Security.Credentials;

namespace ScamBaitDesk.Services;

public sealed class SettingsService
{
    private const string VaultResource = "ScamBaitDesk.Imap";
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScamBaitDesk", "inbox.json");

    public async Task<InboxSettings?> LoadAsync()
    {
        if (!File.Exists(_path)) return null;
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<InboxSettings>(stream);
    }

    public string? LoadPassword(string username)
    {
        try
        {
            var credential = new PasswordVault().Retrieve(VaultResource, username);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch { return null; }
    }

    public async Task SaveAsync(InboxSettings settings, string password)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        var vault = new PasswordVault();
        try { vault.Remove(vault.Retrieve(VaultResource, settings.Username)); } catch { }
        vault.Add(new PasswordCredential(VaultResource, settings.Username, password));
    }
}
