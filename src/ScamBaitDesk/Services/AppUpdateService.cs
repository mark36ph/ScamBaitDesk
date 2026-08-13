using System.Diagnostics;

namespace ScamBaitDesk.Services;

public sealed class AppUpdateService
{
    public sealed record UpdateCheckResult(bool IsAvailable, string CurrentCommit, string LatestCommit, string Message);

    private string? FindRepository()
    {
        var updater = FindUpdater();
        return updater is null ? null : Directory.GetParent(Directory.GetParent(updater)!.FullName)!.FullName;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var repository = FindRepository() ?? throw new InvalidOperationException("The development repository could not be found.");
        await RunGitAsync(repository, cancellationToken, "fetch", "--quiet", "origin", "main");
        var current = await RunGitAsync(repository, cancellationToken, "rev-parse", "--short", "HEAD");
        var latest = await RunGitAsync(repository, cancellationToken, "rev-parse", "--short", "origin/main");
        var behindText = await RunGitAsync(repository, cancellationToken, "rev-list", "--count", "HEAD..origin/main");
        var isAvailable = int.TryParse(behindText, out var behind) && behind > 0;
        return new UpdateCheckResult(isAvailable, current, latest,
            isAvailable ? $"A newer version is available ({behind} commit{(behind == 1 ? "" : "s")})." : "ScamBait Desk is up to date.");
    }

    private static async Task<string> RunGitAsync(string repository, CancellationToken cancellationToken, params string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = "git.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = repository
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "The update check failed." : error);
        return output;
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
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = FindRepository() ?? Environment.CurrentDirectory
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-STA");
        start.ArgumentList.Add("-WindowStyle");
        start.ArgumentList.Add("Hidden");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(scriptPath);
        Process.Start(start);
    }
}
