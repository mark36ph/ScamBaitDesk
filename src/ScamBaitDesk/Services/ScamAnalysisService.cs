using System.Text.RegularExpressions;

namespace ScamBaitDesk.Services;

public sealed partial class ScamAnalysisService
{
    private static readonly (Regex Pattern, string Label, string Detail, int Weight)[] Rules =
    [
        (UrgencyRegex(), "Urgency or threat", "Pushes for immediate action or warns of consequences.", 20),
        (PaymentRegex(), "Unusual payment", "Mentions gift cards, crypto, wire transfer, or advance fees.", 30),
        (CredentialRegex(), "Credential request", "Requests a password, verification code, PIN, or login.", 35),
        (PrizeRegex(), "Unexpected reward", "Promises a prize, refund, inheritance, or guaranteed return.", 20),
        (LinkRegex(), "External link", "Contains a link; do not open it in the review environment.", 10),
        (SecrecyRegex(), "Secrecy pressure", "Asks to keep the interaction secret or bypass normal checks.", 15)
    ];

    public AnalysisResult Analyze(string text)
    {
        var signals = Rules
            .Where(rule => rule.Pattern.IsMatch(text))
            .Select(rule => new RiskSignal(rule.Label, rule.Detail, rule.Weight))
            .ToList();
        var score = Math.Min(100, signals.Sum(signal => signal.Weight));
        var rating = score switch { >= 70 => "High", >= 35 => "Medium", _ => "Low" };
        return new AnalysisResult(score, rating, signals, Redact(text));
    }

    public static string CreateSafeDraft(InboxMessage message) =>
        "Hello,\n\nBefore I can consider this request, please provide the registered business name, " +
        "a public telephone number listed on the organisation's official website, and a reference number " +
        "that the organisation can verify independently. I will not use links or contact details contained " +
        "in this message, share security codes, or make a payment.\n\nRegards";

    public static string Redact(string text)
    {
        var value = EmailRegex().Replace(text, "[EMAIL REDACTED]");
        value = LinkRegex().Replace(value, "[URL REDACTED]");
        value = PhoneRegex().Replace(value, "[PHONE REDACTED]");
        return LongNumberRegex().Replace(value, "[NUMBER REDACTED]");
    }

    [GeneratedRegex(@"\b(urgent|immediately|act now|final warning|account (will be )?(closed|suspended)|police|arrest)\b", RegexOptions.IgnoreCase)] private static partial Regex UrgencyRegex();
    [GeneratedRegex(@"\b(gift ?card|bitcoin|crypto(currency)?|wire transfer|western union|processing fee|advance fee)\b", RegexOptions.IgnoreCase)] private static partial Regex PaymentRegex();
    [GeneratedRegex(@"\b(password|passcode|verification code|one[- ]time code|otp|pin|login credentials?)\b", RegexOptions.IgnoreCase)] private static partial Regex CredentialRegex();
    [GeneratedRegex(@"\b(prize|winner|lottery|inheritance|refund|guaranteed return)\b", RegexOptions.IgnoreCase)] private static partial Regex PrizeRegex();
    [GeneratedRegex(@"\b(secret|do not tell|don't tell|confidential between us|bypass)\b", RegexOptions.IgnoreCase)] private static partial Regex SecrecyRegex();
    [GeneratedRegex("https?://[^\\s<>\\\"]+", RegexOptions.IgnoreCase)] private static partial Regex LinkRegex();
    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase)] private static partial Regex EmailRegex();
    [GeneratedRegex(@"(?<!\w)(?:\+?\d[\d .()-]{7,}\d)(?!\w)")] private static partial Regex PhoneRegex();
    [GeneratedRegex(@"\b\d{8,}\b")] private static partial Regex LongNumberRegex();
}
