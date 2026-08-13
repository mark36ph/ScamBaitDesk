using System.Diagnostics;

namespace ScamBaitDesk.Services;

public sealed class AppUpdateService
{
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
            WorkingDirectory = Directory.GetParent(Directory.GetParent(scriptPath)!.FullName)!.FullName
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-WindowStyle");
        start.ArgumentList.Add("Hidden");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(scriptPath);
        Process.Start(start);
    }
}
