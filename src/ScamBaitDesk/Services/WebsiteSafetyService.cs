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
                        AddFinding(findings, "Cross-domain redirect", $"The request moved from {current.IdnHost} to {next.IdnHost}.", 16);
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
        if (Regex.IsMatch(html, @"<input\b[^>]*type\s*=\s*['""]?password", RegexOptions.IgnoreCase)) AddFinding(findings, "Password collection", "The page contains a password input field.", 34);
        if (Regex.IsMatch(html, @"<form\b", RegexOptions.IgnoreCase) && Regex.IsMatch(html, @"\b(user(name)?|email|account|login)\b", RegexOptions.IgnoreCase)) AddFinding(findings, "Account sign-in form", "The page combines a form with account or login fields.", 18);
        foreach (Match match in Regex.Matches(html, @"<form\b[^>]*\baction\s*=\s*['""](?<url>[^'""]+)", RegexOptions.IgnoreCase))
        {
            if (!Uri.TryCreate(page, WebUtility.HtmlDecode(match.Groups["url"].Value), out var action)) continue;
            if (!action.IdnHost.Equals(page.IdnHost, StringComparison.OrdinalIgnoreCase)) AddFinding(findings, "External form destination", $"A form submits information to {action.IdnHost}.", 28);
            if (page.Scheme == Uri.UriSchemeHttps && action.Scheme == Uri.UriSchemeHttp) AddFinding(findings, "Insecure form submission", "A form on an HTTPS page submits over unencrypted HTTP.", 30);
        }
        if (Regex.IsMatch(html, @"<meta\b[^>]*http-equiv\s*=\s*['""]?refresh", RegexOptions.IgnoreCase)) AddFinding(findings, "Automatic page redirect", "The page contains a browser refresh redirect.", 16);
        if (Regex.IsMatch(html, @"\b(eval\s*\(|atob\s*\(|fromCharCode\s*\()", RegexOptions.IgnoreCase)) AddFinding(findings, "Obfuscated script indicator", "Scripts contain functions commonly used to conceal generated content. This can also occur on legitimate sites.", 12);
        if (Regex.IsMatch(visible, @"\b(seed phrase|recovery phrase|private key|wallet phrase)\b", RegexOptions.IgnoreCase)) AddFinding(findings, "Wallet secret request", "The page refers to wallet recovery secrets or private keys.", 38);
        if (Regex.IsMatch(visible, @"\b(one[- ]time (password|code)|OTP|verification code|security code)\b", RegexOptions.IgnoreCase)) AddFinding(findings, "Authentication-code request", "The page refers to one-time or verification codes.", 28);
        if (Regex.IsMatch(visible, @"\b(gift card|steam card|apple card|google play card)\b", RegexOptions.IgnoreCase)) AddFinding(findings, "Gift-card payment language", "The page mentions gift cards, a common irreversible payment request.", 24);
        if (Regex.IsMatch(visible, @"\b(bitcoin|cryptocurrency|crypto wallet|USDT|ethereum)\b", RegexOptions.IgnoreCase)) AddFinding(findings, "Cryptocurrency language", "The page mentions cryptocurrency or wallets.", 16);
        if (Regex.IsMatch(visible, @"\b(anydesk|teamviewer|remote desktop|screen connect|quick assist)\b", RegexOptions.IgnoreCase)) AddFinding(findings, "Remote-access software", "The page refers to tools that can give another person control of a device.", 26);
        if (Regex.IsMatch(visible, @"\b(act now|immediately|within 24 hours|account (will be|is) (closed|suspended|locked)|urgent action|required immediately)\b", RegexOptions.IgnoreCase)) AddFinding(findings, "Urgency or account threat", "The page pressures the visitor to act quickly or risk account loss.", 17);
        if (Regex.IsMatch(visible, @"\b(card number|CVV|bank account|sort code|wire transfer|processing fee|release fee)\b", RegexOptions.IgnoreCase)) AddFinding(findings, "Financial information or fee request", "The page asks about payment details, bank information, or advance fees.", 22);
        if (Regex.IsMatch(html, @"<iframe\b[^>]*(hidden|display\s*:\s*none|width\s*=\s*['""]?0)", RegexOptions.IgnoreCase)) AddFinding(findings, "Hidden embedded page", "A hidden or zero-width iframe is present.", 15);
    }

    private static void AddFinding(List<WebsiteFinding> findings, string label, string detail, int weight)
    {
        if (findings.All(item => !item.Label.Equals(label, StringComparison.OrdinalIgnoreCase))) findings.Add(new WebsiteFinding(label, detail, weight));
    }

    private static string CleanText(string value, int maximum)
    {
        var text = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(value, @"<[^>]+>", " ")), @"\s+", " ").Trim();
        return text.Length <= maximum ? text : text[..maximum] + "…";
    }
}
