using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace ScamBaitDesk.Services;

public sealed record EvidenceVerificationResult(bool Success, int VerifiedFiles, IReadOnlyList<string> Problems)
{
    public string Summary => Success
        ? $"Verified {VerifiedFiles} evidence file(s); every SHA-256 value matches."
        : $"Verification failed with {Problems.Count} problem(s).";
}

public sealed class EvidenceVerificationService
{
    public EvidenceVerificationResult Verify(Stream source)
    {
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException("The ZIP does not contain manifest.json.");
        if (manifestEntry.Length > 2 * 1024 * 1024) throw new InvalidDataException("The evidence manifest is unexpectedly large.");
        using var reader = new StreamReader(manifestEntry.Open());
        using var document = JsonDocument.Parse(reader.ReadToEnd());
        if (!document.RootElement.TryGetProperty("Files", out var files) || files.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The evidence manifest has no file list.");
        if (files.GetArrayLength() > 1000) throw new InvalidDataException("The evidence manifest contains too many files to verify safely.");

        var problems = new List<string>();
        var listedPaths = new HashSet<string>(StringComparer.Ordinal);
        var verified = 0;
        foreach (var item in files.EnumerateArray())
        {
            var path = item.GetProperty("Path").GetString() ?? string.Empty;
            var expected = item.GetProperty("Sha256").GetString() ?? string.Empty;
            if (!listedPaths.Add(path)) { problems.Add($"Duplicate manifest path: {path}"); continue; }
            var entry = archive.GetEntry(path);
            if (entry is null) { problems.Add($"Missing: {path}"); continue; }
            if (entry.Length > 100 * 1024 * 1024) { problems.Add($"Too large to verify safely: {path}"); continue; }
            using var content = entry.Open();
            var actual = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) problems.Add($"Changed: {path}");
            else verified++;
        }
        foreach (var entry in archive.Entries.Where(entry => entry.FullName != "manifest.json" && !listedPaths.Contains(entry.FullName)))
            problems.Add($"Unlisted file: {entry.FullName}");
        return new EvidenceVerificationResult(problems.Count == 0, verified, problems);
    }
}
