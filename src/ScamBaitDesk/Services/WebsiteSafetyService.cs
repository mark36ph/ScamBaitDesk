using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace ScamBaitDesk.Services;

public sealed class WebsiteSafetyService
{
    private static readonly HashSet<string> Shorteners = new(StringComparer.OrdinalIgnoreCase)
    { "bit.ly", "tinyurl.com", "t.co", "is.gd", "cutt.ly", "rb.gy", "shorturl.at", "rebrand.ly" };

    private static readonly string[] BaitTerms =
    { "login", "signin", "verify", "secure", "account", "wallet", "payment", "invoice", "refund", "gift", "crypto", "bank", "support", "update" };

    public WebsiteCheckResult Check(string input)
    {
        var cleaned = (input ?? string.Empty).Trim()
            .Replace("hxxps://", "https://", StringComparison.OrdinalIgnoreCase)
            .Replace("hxxp://", "http://", StringComparison.OrdinalIgnoreCase)
            .Replace("[.]", ".", StringComparison.Ordinal);
        if (!cleaned.Contains("://", StringComparison.Ordinal)) cleaned = "https://" + cleaned;
        if (!Uri.TryCreate(cleaned, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
            throw new ArgumentException("Enter a complete web address or domain, such as https://example.com.");

        var findings = new List<WebsiteFinding>();
        void Add(string label, string detail, int weight) => findings.Add(new WebsiteFinding(label, detail, weight));
        var host = uri.IdnHost.TrimEnd('.');
        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (uri.Scheme == "http") Add("No HTTPS", "The address uses unencrypted HTTP.", 18);
        if (IPAddress.TryParse(host, out _)) Add("IP address host", "The address uses a raw IP instead of a named domain.", 22);
        if (!string.IsNullOrEmpty(uri.UserInfo)) Add("Hidden user information", "Text before @ can disguise the real destination host.", 28);
        if (labels.Any(label => label.StartsWith("xn--", StringComparison.OrdinalIgnoreCase))) Add("Internationalised domain", "Punycode can be legitimate but is also used for look-alike names.", 18);
        if (labels.Length > 4) Add("Deep subdomain chain", "Many nested labels can obscure the registrable domain.", 12);
        if (host.Length > 55) Add("Long hostname", "An unusually long hostname is harder to inspect accurately.", 10);
        if (!uri.IsDefaultPort) Add("Unusual port", $"The address explicitly uses port {uri.Port}.", 12);
        if (Shorteners.Contains(host)) Add("Shortened address", "The visible address hides the final destination.", 24);
        if (Regex.IsMatch(uri.OriginalString, @"%[0-9a-f]{2}", RegexOptions.IgnoreCase)) Add("Encoded characters", "Percent encoding can conceal parts of an address.", 9);
        if (uri.AbsolutePath.Count(character => character == '/') > 5) Add("Deep path", "A long nested path can make the destination harder to review.", 6);
        if (Regex.IsMatch(host, @"\d{5,}")) Add("Numeric hostname", "A long number sequence in the hostname is unusual.", 9);
        var termCount = BaitTerms.Count(term => host.Contains(term, StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (termCount >= 2) Add("Sensitive-action wording", "The address combines multiple account, payment, or verification terms.", Math.Min(20, 6 + termCount * 3));
        if (labels.Any(label => label.Count(character => character == '-') >= 3)) Add("Hyphen-heavy label", "Multiple hyphens can be used in brand look-alike domains.", 8);

        var unicodeHost = new IdnMapping().GetUnicode(host);
        if (unicodeHost.Any(character => character > 127)) Add("Non-ASCII hostname", "Review international characters carefully for look-alike letters.", 12);

        var score = Math.Min(100, findings.Sum(finding => finding.Weight));
        var rating = score switch { >= 55 => "High concern", >= 25 => "Suspicious", >= 10 => "Review advised", _ => "No obvious structural warning" };
        return new WebsiteCheckResult(uri.AbsoluteUri, host, score, rating, findings);
    }
}
