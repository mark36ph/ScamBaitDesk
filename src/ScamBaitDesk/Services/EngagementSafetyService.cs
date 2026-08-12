using System.Text.RegularExpressions;

namespace ScamBaitDesk.Services;

public sealed class EngagementSafetyService
{
    private static readonly (string Label, string Pattern, string Detail)[] BlockingPatterns =
    [
        ("Password or secret", @"\b(password|passcode|pin|secret)\s*[:=]\s*\S+", "Remove passwords, PINs, and secrets."),
        ("One-time code", @"\b(otp|one[- ]?time|verification|security)\s*(code)?\s*[:=]?\s*\d{4,8}\b", "Remove authentication and verification codes."),
        ("Payment-card number", @"\b(?:\d[ -]*?){13,19}\b", "Remove card or long account numbers."),
        ("Bank details", @"\b(iban|swift|bic|routing|sort code|account number)\b", "Remove financial account details."),
        ("Private key or recovery phrase", @"\b(private key|seed phrase|recovery phrase|mnemonic)\b", "Never disclose wallet recovery material.")
    ];

    private static readonly (string Label, string Pattern, string Detail)[] ReviewPatterns =
    [
        ("Location", @"\b(address|postcode|zip code|where i live|my location)\b", "Check that no real address or location is included."),
        ("Phone number", @"(?<!\w)(?:\+?\d[\d ()-]{7,}\d)(?!\w)", "Use only fictional persona details."),
        ("External link", @"https?://\S+", "Do not send tracking, credential-capture, or malicious links."),
        ("Threatening language", @"\b(threat|hurt|kill|attack|police will|arrest you)\b", "Remove threats and coercive language."),
        ("Authority claim", @"\b(i am|i'm|we are|we're)\s+(the\s+)?(police|fbi|interpol|bank|government|tax office)\b", "Do not impersonate authorities or institutions.")
    ];

    public PrivacyReview Review(string text)
    {
        var findings = new List<PrivacyFinding>();
        foreach (var item in BlockingPatterns)
            if (Regex.IsMatch(text, item.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                findings.Add(new PrivacyFinding(item.Label, item.Detail, true));
        foreach (var item in ReviewPatterns)
            if (Regex.IsMatch(text, item.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                findings.Add(new PrivacyFinding(item.Label, item.Detail, false));
        return new PrivacyReview(findings);
    }
}
