using System.Diagnostics;
using System.Net.NetworkInformation;

namespace ScamBaitDesk.Services;

public sealed record VpnStatus(bool IsConnected, bool IsFastVpnRunning, string Detail);

public sealed class VpnIntegrationService
{
    public VpnStatus GetStatus()
    {
        var running = Process.GetProcesses().Any(process =>
        {
            try { return process.ProcessName.Contains("FastVPN", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        });
        var active = NetworkInterface.GetAllNetworkInterfaces().Where(adapter =>
            adapter.OperationalStatus == OperationalStatus.Up &&
            (adapter.NetworkInterfaceType is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp ||
             adapter.Name.Contains("FastVPN", StringComparison.OrdinalIgnoreCase) ||
             adapter.Description.Contains("FastVPN", StringComparison.OrdinalIgnoreCase) ||
             adapter.Description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) ||
             adapter.Description.Contains("TAP", StringComparison.OrdinalIgnoreCase))).ToList();
        return new VpnStatus(active.Count > 0, running, active.Count > 0
            ? $"Active tunnel: {string.Join(", ", active.Select(adapter => adapter.Name).Take(3))}."
            : running ? "FastVPN is running, but no active VPN tunnel was detected." : "FastVPN is not running and no active VPN tunnel was detected.");
    }

    public bool OpenFastVpn()
    {
        var running = Process.GetProcesses().FirstOrDefault(process =>
        {
            try { return process.ProcessName.Contains("FastVPN", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        });
        if (running is not null) return true;

        var shortcuts = new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) };
        foreach (var root in shortcuts.Where(Directory.Exists))
        {
            var candidate = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories)
                .FirstOrDefault(path => Path.GetFileName(path).Contains("FastVPN", StringComparison.OrdinalIgnoreCase));
            if (candidate is null) continue;
            Process.Start(new ProcessStartInfo(candidate) { UseShellExecute = true });
            return true;
        }
        var executables = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FastVPN", "FastVPN.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FastVPN", "FastVPN.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "FastVPN", "FastVPN.exe")
        };
        var executable = executables.FirstOrDefault(File.Exists);
        if (executable is not null) { Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true }); return true; }
        return false;
    }
}
