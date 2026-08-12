using System.Net;
using System.Text.RegularExpressions;

namespace ScamBaitDesk.Services;

public sealed partial class EmailForensicsService
{
    public EmailForensicsReport Analyze(InboxMessage message)
    {
        var findings = new List<ForensicFinding>();
        var warnings = new List<string>();
        var from = First(message, "From", message.Sender);
        var replyTo = First(message, "Reply-To", "Not present");
        var returnPath = First(message, "Return-Path", "Not present");
        var auth = string.Join(" ", Values(message, "Authentication-Results"));

        AddIdentity(findings, "From", from);
        AddIdentity(findings, "Reply-To", replyTo);
        AddIdentity(findings, "Return-Path", returnPath);

        var fromDomain = Domain(from);
        var replyDomain = Domain(replyTo);
        var returnDomain = Domain(returnPath);
        if (replyDomain is not null && fromDomain is not null && !SameOrganizationalDomain(fromDomain, replyDomain))
            warnings.Add($"Reply-To domain {replyDomain} differs from From domain {fromDomain}.");
        if (returnDomain is not null && fromDomain is not null && !SameOrganizationalDomain(fromDomain, returnDomain))
            warnings.Add($"Return-Path domain {returnDomain} differs from From domain {fromDomain}.");

        AddAuth(findings, warnings, "SPF", auth, SpfRegex());
        AddAuth(findings, warnings, "DKIM", auth, DkimRegex());
        AddAuth(findings, warnings, "DMARC", auth, DmarcRegex());

        var receivedSpf = First(message, "Received-SPF", "Not present");
        if (auth.Length == 0 && receivedSpf != "Not present")
            findings.Add(new ForensicFinding("Received-SPF", receivedSpf, ResultAssessment(receivedSpf)));

        var originatingIp = FindOriginatingIp(message);
        findings.Add(new ForensicFinding("Originating IP", originatingIp ?? "Not identified", originatingIp is null ? "Unavailable" : "Informational"));
        findings.Add(new ForensicFinding("Message-ID", First(message, "Message-ID", message.Id), "Informational"));

        if (auth.Length == 0) warnings.Add("Authentication-Results header is absent; the receiving server may not expose SPF, DKIM, or DMARC results.");
        var verdict = warnings.Count switch { 0 => "No obvious header mismatch", <= 2 => "Review recommended", _ => "Multiple identity concerns" };
        return new EmailForensicsReport(verdict, findings, warnings, FormatHeaders(message));
    }

    private static void AddIdentity(List<ForensicFinding> findings, string label, string value) =>
        findings.Add(new ForensicFinding(label, value, value == "Not present" ? "Unavailable" : "Informational"));

    private static void AddAuth(List<ForensicFinding> findings, List<string> warnings, string label, string auth, Regex pattern)
    {
        var match = pattern.Match(auth);
        var result = match.Success ? match.Groups[1].Value.ToLowerInvariant() : "not present";
        findings.Add(new ForensicFinding(label, result, ResultAssessment(result)));
        if (result is "fail" or "softfail" or "temperror" or "permerror") warnings.Add($"{label} result is {result}.");
    }

    private static string ResultAssessment(string value) => value.ToLowerInvariant() switch
    {
        var text when text.Contains("pass") => "Pass",
        var text when text.Contains("fail") || text.Contains("error") => "Warning",
        _ => "Unavailable"
    };

    private static string? FindOriginatingIp(InboxMessage message)
    {
        foreach (var value in Values(message, "X-Originating-IP").Concat(Values(message, "Received")).Reverse())
        {
            foreach (Match match in IpRegex().Matches(value))
                if (IPAddress.TryParse(match.Value.Trim('[', ']'), out var ip) && !IPAddress.IsLoopback(ip)) return ip.ToString();
        }
        return null;
    }

    private static string First(InboxMessage message, string name, string fallback) =>
        Values(message, name).FirstOrDefault() ?? fallback;

    private static IEnumerable<string> Values(InboxMessage message, string name) =>
        message.Headers.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value ?? [];

    private static string? Domain(string value)
    {
        var match = DomainRegex().Match(value);
        return match.Success ? match.Groups[1].Value.TrimEnd('.').ToLowerInvariant() : null;
    }

    private static bool SameOrganizationalDomain(string left, string right) =>
        left == right || left.EndsWith($".{right}", StringComparison.OrdinalIgnoreCase) || right.EndsWith($".{left}", StringComparison.OrdinalIgnoreCase);

    private static string FormatHeaders(InboxMessage message) => string.Join("\r\n", message.Headers
        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
        .SelectMany(pair => pair.Value.Select(value => $"{pair.Key}: {value}")));

    [GeneratedRegex(@"\bspf=(pass|fail|softfail|neutral|none|temperror|permerror)\b", RegexOptions.IgnoreCase)] private static partial Regex SpfRegex();
    [GeneratedRegex(@"\bdkim=(pass|fail|neutral|none|temperror|permerror|policy)\b", RegexOptions.IgnoreCase)] private static partial Regex DkimRegex();
    [GeneratedRegex(@"\bdmarc=(pass|fail|bestguesspass|none|temperror|permerror)\b", RegexOptions.IgnoreCase)] private static partial Regex DmarcRegex();
    [GeneratedRegex(@"@([A-Z0-9.-]+)", RegexOptions.IgnoreCase)] private static partial Regex DomainRegex();
    [GeneratedRegex(@"(?<![\dA-F:])(?:\d{1,3}\.){3}\d{1,3}(?![\dA-F:])|\[[0-9A-F:]{2,}\]", RegexOptions.IgnoreCase)] private static partial Regex IpRegex();
}
