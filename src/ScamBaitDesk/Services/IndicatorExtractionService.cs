using System.Text.RegularExpressions;

namespace ScamBaitDesk.Services;

public sealed partial class IndicatorExtractionService
{
    public IReadOnlyList<IndicatorRecord> Extract(IEnumerable<InboxMessage> messages)
    {
        var occurrences = new List<(IndicatorType Type, string Value, string Source)>();
        foreach (var message in messages)
        {
            var source = $"{message.ReceivedDisplay} · {message.Subject}";
            var text = $"{message.Sender}\n{message.Subject}\n{message.Body}";
            AddMatches(occurrences, IndicatorType.Url, UrlRegex(), text, source, NormalizeUrl);
            AddMatches(occurrences, IndicatorType.Email, EmailRegex(), text, source, value => value.ToLowerInvariant());
            AddMatches(occurrences, IndicatorType.IpAddress, IpRegex(), text, source, value => value.Trim('[', ']'));
            AddMatches(occurrences, IndicatorType.Phone, PhoneRegex(), text, source, NormalizeWhitespace);
            AddMatches(occurrences, IndicatorType.CryptoWallet, CryptoRegex(), text, source, value => value);
            AddMatches(occurrences, IndicatorType.PaymentHandle, PaymentHandleRegex(), text, source, value => value.ToLowerInvariant());
            AddMatches(occurrences, IndicatorType.AccountNumber, AccountNumberRegex(), text, source, NormalizeWhitespace);

            foreach (Match match in EmailRegex().Matches(text))
            {
                var email = match.Value;
                Add(occurrences, IndicatorType.Domain, email[(email.LastIndexOf('@') + 1)..].ToLowerInvariant(), source);
            }
            foreach (Match match in UrlRegex().Matches(text))
            {
                var url = NormalizeUrl(match.Value);
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) Add(occurrences, IndicatorType.Domain, uri.IdnHost.ToLowerInvariant(), source);
            }
        }

        return occurrences
            .GroupBy(item => $"{item.Type}|{item.Value}", StringComparer.OrdinalIgnoreCase)
            .Select(group => new IndicatorRecord(group.First().Type, group.First().Value, group.Count(), group.Select(item => item.Source).Distinct().ToList()))
            .OrderBy(item => item.Type).ThenByDescending(item => item.Occurrences).ThenBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddMatches(List<(IndicatorType, string, string)> target, IndicatorType type, Regex regex, string text, string source, Func<string, string> normalize)
    {
        foreach (Match match in regex.Matches(text)) Add(target, type, normalize(match.Value.TrimEnd('.', ',', ';', ':', ')', ']', '}')), source);
    }

    private static void Add(List<(IndicatorType, string, string)> target, IndicatorType type, string value, string source)
    {
        if (!string.IsNullOrWhiteSpace(value)) target.Add((type, value, source));
    }

    private static string NormalizeUrl(string value) => value.Trim().TrimEnd('.', ',', ';', ':', ')', ']', '}');
    private static string NormalizeWhitespace(string value) => Regex.Replace(value.Trim(), @"\s+", " ");

    [GeneratedRegex("https?://[^\\s<>\\\"']+", RegexOptions.IgnoreCase)] private static partial Regex UrlRegex();
    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase)] private static partial Regex EmailRegex();
    [GeneratedRegex(@"(?<![\dA-F:])(?:\d{1,3}\.){3}\d{1,3}(?![\dA-F:])|\[[0-9A-F:]{2,}\]", RegexOptions.IgnoreCase)] private static partial Regex IpRegex();
    [GeneratedRegex(@"(?<!\w)(?:\+?\d[\d .()-]{7,}\d)(?!\w)")] private static partial Regex PhoneRegex();
    [GeneratedRegex(@"\b(?:bc1[a-z0-9]{25,62}|[13][a-km-zA-HJ-NP-Z1-9]{25,34}|0x[a-fA-F0-9]{40})\b")] private static partial Regex CryptoRegex();
    [GeneratedRegex(@"(?<!\w)(?:\$[A-Za-z][A-Za-z0-9_]{2,19}|@[A-Za-z][A-Za-z0-9_.-]{2,30})(?!\w)")] private static partial Regex PaymentHandleRegex();
    [GeneratedRegex(@"\b\d{8,24}\b")] private static partial Regex AccountNumberRegex();
}
