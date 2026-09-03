using System.Text.RegularExpressions;

namespace ScamBaitDesk.Services;

public sealed class PhoneScamAnalysisService
{
    private static readonly (string Label, string Detail, string Pattern, int Weight)[] Signals =
    [
        ("Urgency or pressure", "The caller appears to create a deadline or pressure you to act immediately.", @"\b(urgent|immediately|right now|act now|within \d+ (minutes?|hours?)|final warning|last chance|don't hang up)\b", 18),
        ("Authority impersonation", "The transcript contains language associated with pretending to be a bank, government body, police, delivery service, or technical-support organisation.", @"\b(bank|police|hmrc|tax|government|customs|courier|delivery|microsoft|apple|amazon|paypal|support|fraud team|security team)\b", 16),
        ("One-time code request", "A caller appears to ask for a verification, one-time passcode, recovery code, or similar secret.", @"\b(otp|one[- ]time (pass)?code|verification code|security code|passcode|authentication code|recovery code)\b", 35),
        ("Payment request", "The caller appears to request money, card details, a bank transfer, gift cards, cryptocurrency, or another payment method.", @"\b(bank transfer|wire transfer|gift card|crypto(currency)?|bitcoin|ethereum|card number|credit card|debit card|payment|pay us|send money)\b", 30),
        ("Remote-access request", "The caller appears to ask for remote-control software, screen sharing, or access to the computer or phone.", @"\b(remote (access|desktop|control)|screen ?share|anydesk|teamviewer|quick ?assist|remote support|let me control)\b", 30),
        ("Secrecy instruction", "The caller appears to tell the recipient not to tell family, a bank, colleagues, or another trusted person.", @"\b(don't tell|do not tell|keep this (secret|private)|don't contact (your )?(bank|police)|stay on the line|don't hang up)\b", 24),
        ("Safe-account or money-moving story", "The caller appears to claim that money must be moved to a safe, protected, or temporary account.", @"\b(safe account|protected account|temporary account|move your money|transfer your funds|secure account)\b", 32),
        ("Callback pressure", "The caller appears to direct the recipient to call a number supplied during the suspicious interaction.", @"\b(call (this|the following)|callback|call back|return (the )?call|dial this number)\b", 12),
        ("Link or app installation request", "The caller appears to direct the recipient to a link, app, download, or installation.", @"\b(click|open|download|install|app|link|website|browser)\b", 16)
    ];

    public PhoneScamAnalysisResult Analyze(string phoneNumber, string transcript)
    {
        var number = (phoneNumber ?? string.Empty).Trim();
        var text = (transcript ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(number) && string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Enter a phone number, a call transcript, or both.");

        var findings = new List<PhoneScamFinding>();
        var score = 0;

        if (!string.IsNullOrWhiteSpace(number))
        {
            var normalized = NormalizeNumber(number);
            if (normalized.Length < 7 || normalized.Length > 15)
            {
                findings.Add(new PhoneScamFinding("Unusual number length", "The supplied number is outside a common international-number length range. This is only a formatting signal, not proof of fraud.", 8, "Phone number", "Verify the number independently rather than trusting caller ID."));
                score += 8;
            }
            if (Regex.IsMatch(number, @"[A-Za-z]"))
            {
                findings.Add(new PhoneScamFinding("Letters in phone field", "The phone field contains letters or words and should be checked against the original call record.", 4, "Phone number", "Copy the number from the call log rather than from a message supplied by the caller."));
                score += 4;
            }
            findings.Add(new PhoneScamFinding("Caller ID is not identity proof", "Caller ID can be spoofed. A familiar number or organisation name does not establish who called.", 10, "Identity", "Use a trusted, independently sourced number to verify the organisation."));
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (var signal in Signals)
            {
                var match = Regex.Match(text, signal.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success) continue;
                var evidence = $"Matched wording: “{TrimEvidence(match.Value)}”.";
                findings.Add(new PhoneScamFinding(signal.Label, signal.Detail, signal.Weight, "Call content", evidence + " The scanner did not record or contact the caller."));
                score += signal.Weight;
            }

            var extractedNumbers = Regex.Matches(text, @"(?<!\w)(?:\+?\d[\d ()-]{5,}\d)(?!\w)")
                .Select(match => NormalizeNumber(match.Value))
                .Where(value => value.Length >= 7 && value.Length <= 15)
                .Distinct(StringComparer.Ordinal)
                .Take(10)
                .ToList();
            if (extractedNumbers.Count > 0)
            {
                findings.Add(new PhoneScamFinding("Phone number mentioned in transcript", $"The transcript contains {extractedNumbers.Count} phone-number-like value(s). Treat them as untrusted indicators.", 8, "Indicator", $"Extracted: {string.Join(", ", extractedNumbers.Select(DefangNumber))}", "Do not call a number supplied by the suspicious caller. Verify the organisation using a trusted source."));
                score += 8;
            }
        }

        score = Math.Min(100, score);
        var rating = score switch { >= 60 => "High concern", >= 30 => "Suspicious", >= 15 => "Review advised", _ => "No strong scam pattern detected" };
        var guidance = score >= 30
            ? "Do not disclose passwords, one-time codes, payment details, remote access, or recovery phrases. End the call if pressure continues and independently contact the claimed organisation."
            : "Treat this as an indicator review, not a verdict. Verify the caller independently before sharing information or returning a call.";

        return new PhoneScamAnalysisResult(number, score, rating, guidance, findings);
    }

    private static string NormalizeNumber(string value) => new(value.Where(char.IsDigit).ToArray());
    private static string DefangNumber(string value) => value.Length <= 4 ? value : value[..^3] + "[xxx]";
    private static string TrimEvidence(string value) => value.Length > 80 ? value[..80] + "…" : value;
}

public sealed record PhoneScamFinding(string Label, string Detail, int Weight, string Category, string Evidence, string Recommendation)
{
    public PhoneScamFinding(string label, string detail, int weight, string category, string evidence)
        : this(label, detail, weight, category, evidence, "Verify the caller or organisation through an independent trusted source before taking action.") { }

    public string Severity => Weight >= 30 ? "High" : Weight >= 15 ? "Medium" : "Low";
    public string SeverityDisplay => $"{Severity} concern · +{Weight} points · {Category}";
    public string EvidenceDisplay => $"Evidence: {Evidence}";
    public string RecommendationDisplay => $"Recommended action: {Recommendation}";
}

public sealed record PhoneScamAnalysisResult(
    string PhoneNumber,
    int Score,
    string Rating,
    string Guidance,
    IReadOnlyList<PhoneScamFinding> Findings)
{
    public string Summary => $"{Rating} · {Score}/100 · {Findings.Count} signal(s)";
}
