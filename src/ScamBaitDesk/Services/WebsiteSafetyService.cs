using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace ScamBaitDesk.Services;

public sealed class WebsiteSafetyService
{
    private const int MaximumDownloadBytes = 1_000_000;
    private const int MaximumRedirects = 4;
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

        var host = uri.IdnHost.TrimEnd('.');
        var findings = new List<WebsiteFinding>();
        void Add(string label, string detail, int weight) => findings.Add(new WebsiteFinding(
            label, detail, weight, "Address structure", $"Analysed scheme {uri.Scheme.ToUpperInvariant()} and host {host}.",
            "Confirm the real domain through an independent source before opening the page or entering any information."));
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

    public async Task<WebsiteLiveScanResult> ScanPageAsync(string input, CancellationToken cancellationToken = default)
    {
        var initial = Check(input);
        var current = new Uri(initial.NormalizedUrl);
        var redirects = 0;
        var findings = new List<WebsiteFinding>();

        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            ConnectCallback = ConnectToPublicAddressAsync
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ScamBaitDesk-SafetyScanner/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html, application/xhtml+xml, text/plain;q=0.8");

        HttpResponseMessage? response = null;
        try
        {
            while (true)
            {
                ValidatePublicWebUri(current);
                response?.Dispose();
                response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    if (redirects++ >= MaximumRedirects) throw new InvalidOperationException($"The page exceeded the {MaximumRedirects}-redirect safety limit.");
                    var location = response.Headers.Location ?? throw new InvalidOperationException("The website returned a redirect without a destination.");
                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    ValidatePublicWebUri(next);
                    if (!next.IdnHost.Equals(current.IdnHost, StringComparison.OrdinalIgnoreCase))
                        AddFinding(findings, "Cross-domain redirect", "Redirecting to another domain can hide the organisation actually receiving the visit.", 16, "Navigation", $"Observed redirect: {current.IdnHost} → {next.IdnHost}.", "Compare the final domain with the organisation's address obtained from an independent trusted source.");
                    current = next;
                    continue;
                }
                break;
            }

            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"The website returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!(mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"The website returned {(string.IsNullOrWhiteSpace(mediaType) ? "an unsupported content type" : mediaType)} instead of a text page. It was not downloaded.");
            if (response.Content.Headers.ContentLength > MaximumDownloadBytes)
                throw new InvalidOperationException("The page is larger than the 1 MB scan limit.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var memory = new MemoryStream();
            var buffer = new byte[16_384];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                if (memory.Length + read > MaximumDownloadBytes) throw new InvalidOperationException("The page exceeded the 1 MB scan limit and the download was stopped.");
                memory.Write(buffer, 0, read);
            }
            var html = Encoding.UTF8.GetString(memory.ToArray());
            AnalyzePage(current, html, findings);
            var titleMatch = Regex.Match(html, @"<title\b[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var title = titleMatch.Success ? CleanText(titleMatch.Groups[1].Value, 140) : "No page title";
            return new WebsiteLiveScanResult(current.AbsoluteUri, title, (int)memory.Length, redirects, findings);
        }
        finally { response?.Dispose(); }
    }

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var publicAddresses = addresses.Where(IsPublicAddress).ToList();
        if (publicAddresses.Count == 0) throw new InvalidOperationException("The website resolves only to a local, private, reserved, or otherwise unsafe network address.");
        Exception? lastError = null;
        foreach (var address in publicAddresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) { lastError = ex; socket.Dispose(); }
        }
        throw new HttpRequestException("No public address for the website could be reached.", lastError);
    }

    private static void ValidatePublicWebUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || string.IsNullOrWhiteSpace(uri.Host))
            throw new InvalidOperationException("Only complete HTTP or HTTPS website addresses can be scanned.");
        if (!string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidOperationException("Addresses containing embedded usernames or passwords cannot be scanned.");
        if (!uri.IsDefaultPort && uri.Port is not 80 and not 443) throw new InvalidOperationException("Only standard web ports 80 and 443 can be scanned.");
        if (IPAddress.TryParse(uri.IdnHost, out var address) && !IsPublicAddress(address))
            throw new InvalidOperationException("Local, private, reserved, and loopback network addresses cannot be scanned.");
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None)) return false;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (bytes[0] & 0xFE) == 0xFC) return false;
            if (bytes.Take(12).All(value => value == 0)) return IsPublicAddress(new IPAddress(bytes[12..]));
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8) return false;
            if (bytes[..12].SequenceEqual(new byte[] { 0x00, 0x64, 0xFF, 0x9B, 0, 0, 0, 0, 0, 0, 0, 0 })) return IsPublicAddress(new IPAddress(bytes[12..]));
            return true;
        }
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        if (b[0] is 0 or 10 or 127 || b[0] >= 224) return false;
        if (b[0] == 100 && b[1] is >= 64 and <= 127) return false;
        if (b[0] == 169 && b[1] == 254) return false;
        if (b[0] == 172 && b[1] is >= 16 and <= 31) return false;
        if (b[0] == 192 && b[1] == 168) return false;
        if (b[0] == 192 && b[1] == 0) return false;
        if (b[0] == 192 && b[1] == 88 && b[2] == 99) return false;
        if (b[0] == 198 && b[1] is 18 or 19) return false;
        if (b[0] == 198 && b[1] == 51 && b[2] == 100) return false;
        if (b[0] == 203 && b[1] == 0 && b[2] == 113) return false;
        return true;
    }

    private static void AnalyzePage(Uri page, string html, List<WebsiteFinding> findings)
    {
        var visible = WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, @"<(script|style)\b[^>]*>.*?</\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline), @"<[^>]+>", " "));
        var passwordFields = Regex.Matches(html, @"<input\b[^>]*type\s*=\s*['""]?password", RegexOptions.IgnoreCase).Count;
        if (passwordFields > 0) AddFinding(findings, "Password collection", "Password fields can be legitimate, but on an unverified domain they are a primary credential-theft risk.", 34, "Credentials", $"Found {passwordFields} HTML password input field(s). No values were read or submitted.", "Do not enter a password. Reach the organisation through a bookmarked or independently verified address and compare its domain.");

        var formCount = Regex.Matches(html, @"<form\b", RegexOptions.IgnoreCase).Count;
        var accountMarker = Regex.Match(html, @"\b(user(name)?|email|account|login)\b", RegexOptions.IgnoreCase);
        if (formCount > 0 && accountMarker.Success) AddFinding(findings, "Account sign-in form", "A form paired with account wording may be designed to collect identity or login details.", 18, "Credentials", $"Found {formCount} form(s) and the account marker “{CleanText(accountMarker.Value, 40)}”.", "Treat the form as untrusted until the domain and organisation are independently verified.");

        foreach (Match match in Regex.Matches(html, @"<form\b[^>]*\baction\s*=\s*['""](?<url>[^'""]+)", RegexOptions.IgnoreCase))
        {
            if (!Uri.TryCreate(page, WebUtility.HtmlDecode(match.Groups["url"].Value), out var action)) continue;
            if (!action.IdnHost.Equals(page.IdnHost, StringComparison.OrdinalIgnoreCase)) AddFinding(findings, "External form destination", "Information entered on the page would be sent to a different domain, which can conceal the real recipient.", 28, "Data destination", $"Page domain: {page.IdnHost}; form destination: {action.IdnHost}.", "Do not submit the form. Verify both domains and report the destination as a separate indicator.");
            if (page.Scheme == Uri.UriSchemeHttps && action.Scheme == Uri.UriSchemeHttp) AddFinding(findings, "Insecure form submission", "The form would remove transport encryption before sending entered information.", 30, "Transport security", $"HTTPS page form action uses HTTP: {action.GetLeftPart(UriPartial.Path)}", "Do not enter or submit any information on this form.");
        }

        var refreshCount = Regex.Matches(html, @"<meta\b[^>]*http-equiv\s*=\s*['""]?refresh", RegexOptions.IgnoreCase).Count;
        if (refreshCount > 0) AddFinding(findings, "Automatic page redirect", "Browser-refresh redirects can move visitors without an obvious click and obscure the eventual destination.", 16, "Navigation", $"Found {refreshCount} meta-refresh directive(s). The scanner did not execute them.", "Inspect the destination independently and do not follow it from an untrusted page.");

        var scriptMarker = Regex.Match(html, @"\b(eval\s*\(|atob\s*\(|fromCharCode\s*\()", RegexOptions.IgnoreCase);
        if (scriptMarker.Success) AddFinding(findings, "Obfuscated script indicator", "These functions can construct or decode hidden page behaviour, although legitimate sites sometimes use them too.", 12, "Page code", $"Found script marker “{CleanText(scriptMarker.Value, 40)}”. JavaScript was not executed.", "Use this only as a supporting signal; compare it with credential, redirect, and domain findings.");

        AddPhraseFinding(findings, visible, @"\b(seed phrase|recovery phrase|private key|wallet phrase)\b", "Wallet secret request", "Wallet recovery words or private keys give complete control of funds and legitimate support staff should never request them.", 38, "Wallet credentials", "Never disclose wallet recovery material. End engagement and preserve the page address as evidence.");
        AddPhraseFinding(findings, visible, @"\b(one[- ]time (password|code)|OTP|verification code|security code)\b", "Authentication-code request", "One-time codes can authorize logins, payments, or account recovery and should never be relayed to another person.", 28, "Credentials", "Do not enter or send a code. Contact the provider using independently verified details.");
        AddPhraseFinding(findings, visible, @"\b(gift card|steam card|apple card|google play card)\b", "Gift-card payment language", "Gift cards are difficult to reverse and are commonly demanded because their codes can be redeemed anonymously.", 24, "Payment", "Do not buy or disclose gift-card codes. Preserve the request for reporting.");
        AddPhraseFinding(findings, visible, @"\b(bitcoin|cryptocurrency|crypto wallet|USDT|ethereum)\b", "Cryptocurrency language", "Crypto references are not proof of fraud, but irreversible transfers are common in scam payment demands.", 16, "Payment", "Do not transfer funds. Look for corroborating pressure, fee, identity, and wallet-secret warnings.");
        AddPhraseFinding(findings, visible, @"\b(anydesk|teamviewer|remote desktop|screen connect|quick assist)\b", "Remote-access software", "Remote-access tools can let another person view the screen, control the device, and access accounts.", 26, "Device access", "Do not install or launch remote-access software at a stranger's request.");
        AddPhraseFinding(findings, visible, @"\b(act now|immediately|within 24 hours|account (will be|is) (closed|suspended|locked)|urgent action|required immediately)\b", "Urgency or account threat", "Artificial deadlines reduce the time available to verify a story independently.", 17, "Pressure tactic", "Pause the interaction and verify the claim through an official channel you locate yourself.");
        AddPhraseFinding(findings, visible, @"\b(card number|CVV|bank account|sort code|wire transfer|processing fee|release fee)\b", "Financial information or fee request", "Requests for payment details or advance fees can lead to theft or irreversible transfers.", 22, "Financial", "Do not provide financial details or pay a fee. Confirm the request directly with the named organisation.");

        var hiddenFrames = Regex.Matches(html, @"<iframe\b[^>]*(hidden|display\s*:\s*none|width\s*=\s*['""]?0)", RegexOptions.IgnoreCase).Count;
        if (hiddenFrames > 0) AddFinding(findings, "Hidden embedded page", "A concealed frame can load another page or tracking content without being visible to the visitor.", 15, "Embedded content", $"Found {hiddenFrames} hidden or zero-width iframe(s). Embedded pages were not loaded.", "Treat this as a supporting technical warning and investigate the domain separately.");
    }

    private static void AddPhraseFinding(List<WebsiteFinding> findings, string text, string pattern, string label, string detail, int weight, string category, string recommendation)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        if (match.Success) AddFinding(findings, label, detail, weight, category, $"Matched visible wording: “{CleanText(match.Value, 80)}”.", recommendation);
    }

    private static void AddFinding(List<WebsiteFinding> findings, string label, string detail, int weight, string category = "Page content", string evidence = "", string recommendation = "")
    {
        if (findings.All(item => !item.Label.Equals(label, StringComparison.OrdinalIgnoreCase))) findings.Add(new WebsiteFinding(label, detail, weight, category, evidence, recommendation));
    }

    private static string CleanText(string value, int maximum)
    {
        var text = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(value, @"<[^>]+>", " ")), @"\s+", " ").Trim();
        return text.Length <= maximum ? text : text[..maximum] + "…";
    }
}
