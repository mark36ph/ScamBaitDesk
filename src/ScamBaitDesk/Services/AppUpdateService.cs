using System.Diagnostics;

namespace ScamBaitDesk.Services;

public sealed class AppUpdateService
{
    public const int CurrentBuild = 33;
    private const string BuildNumberUrl = "https://raw.githubusercontent.com/mark36ph/ScamBaitDesk/main/build-number.txt";
    public sealed record UpdateCheckResult(bool IsAvailable, int CurrentBuild, int LatestBuild, string Message);

    private string? FindRepository()
    {
        var updater = FindUpdater();
        return updater is null ? null : Directory.GetParent(Directory.GetParent(updater)!.FullName)!.FullName;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var text = (await client.GetStringAsync($"{BuildNumberUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}", cancellationToken)).Trim();
        if (!int.TryParse(text, out var latestBuild)) throw new InvalidOperationException("GitHub returned an invalid build number.");
        var isAvailable = latestBuild > CurrentBuild;
        return new UpdateCheckResult(isAvailable, CurrentBuild, latestBuild,
            isAvailable ? "A newer build is available." : "ScamBait Desk is up to date.");
    }

    public string? FindUpdater()
    {
        var starts = new[] { AppContext.BaseDirectory, Environment.CurrentDirectory };
        foreach (var start in starts)
        {
            var directory = new DirectoryInfo(start);
            for (var depth = 0; directory is not null && depth < 10; depth++, directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "scripts", "Update-ScamBaitDesk.ps1");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    public void Launch(string scriptPath)
    {
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = FindRepository() ?? Environment.CurrentDirectory
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-STA");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(scriptPath);
        Process.Start(start);
    }
}
