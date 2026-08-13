using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ScamBaitDesk.Services;

public sealed record EvidenceExportResult(int EvidenceFileCount, string ManifestSha256);

public sealed class EvidenceExportService
{
    private readonly IndicatorExtractionService _indicatorExtractor = new();

    public EvidenceExportResult Export(CaseRecord record, Stream destination)
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        using (var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, hashes, "summary.html", BuildSummary(record));
            Add(archive, hashes, "case.json", JsonSerializer.Serialize(new
            {
                record.Id,
                Title = ScamAnalysisService.Redact(record.Title),
                Status = record.StatusDisplay,
                record.CreatedAt,
                record.UpdatedAt,
                Risk = record.Analysis?.Summary,
                MessageCount = record.Messages.Count,
                OutboundMessageCount = record.OutboundMessages.Count,
                record.PersonaId,
                record.EngagementStopped,
                record.EngagementStoppedAt,
                EngagementStopReason = ScamAnalysisService.Redact(record.EngagementStopReason)
            }, JsonOptions));
            Add(archive, hashes, "notes.txt", ScamAnalysisService.Redact(record.Notes));
            Add(archive, hashes, "draft-reply.txt", ScamAnalysisService.Redact(record.DraftReply));
            Add(archive, hashes, "timeline.json", JsonSerializer.Serialize(record.Timeline, JsonOptions));
            Add(archive, hashes, "reminders.json", JsonSerializer.Serialize(record.Reminders, JsonOptions));
            Add(archive, hashes, "engagement-plan.json", JsonSerializer.Serialize(new
            {
                record.EngagementStage,
                Objective = ScamAnalysisService.Redact(record.EngagementObjective),
                record.OutboundMessageBudget,
                record.EngagementDeadline,
                OutboundMessagesUsed = record.OutboundMessages.Count
            }, JsonOptions));
            Add(archive, hashes, "sender-claims.json", JsonSerializer.Serialize(record.SenderClaims, JsonOptions));
            Add(archive, hashes, "attachment-metadata.json", JsonSerializer.Serialize(record.Messages.SelectMany(message => message.Attachments.Select(attachment => new
            {
                MessageId = message.Id,
                attachment.FileName,
                attachment.MediaType,
                attachment.Size,
                attachment.ContentId
            })), JsonOptions));
            Add(archive, hashes, "outbound-log.json", JsonSerializer.Serialize(record.OutboundMessages.Select(item => new
            {
                item.SentAt,
                Recipient = ScamAnalysisService.Redact(item.Recipient),
                Subject = ScamAnalysisService.Redact(item.Subject),
                item.RedactedBody,
                item.MessageId
            }), JsonOptions));
            Add(archive, hashes, "indicators.json", JsonSerializer.Serialize(_indicatorExtractor.Extract(record.Messages), JsonOptions));

            var index = 0;
            foreach (var message in record.Messages.OrderBy(message => message.ReceivedAt))
            {
                index++;
                var prefix = $"messages/{index:000}-{SafeName(ScamAnalysisService.Redact(message.Subject))}";
                var transcript = $"Received: {message.ReceivedAt:O}\r\nFrom: {ScamAnalysisService.Redact(message.Sender)}\r\nSubject: {ScamAnalysisService.Redact(message.Subject)}\r\nMessage-ID: {message.Id}\r\n\r\n{ScamAnalysisService.Redact(message.Body)}";
                Add(archive, hashes, $"{prefix}.txt", transcript);
                Add(archive, hashes, $"{prefix}-headers.txt", FormatHeaders(message));
            }

            var manifest = JsonSerializer.Serialize(new
            {
                Format = "ScamBait Desk evidence export v4",
                ExportedAtUtc = DateTimeOffset.UtcNow,
                CaseId = record.Id,
                Redaction = "Message bodies, sender display, subjects, notes, and drafts were processed by the local redactor. Headers are preserved as evidence and may contain personal data.",
                Files = hashes.Select(pair => new { Path = pair.Key, Sha256 = pair.Value })
            }, JsonOptions);
            AddWithoutHash(archive, "manifest.json", manifest);
            return new EvidenceExportResult(hashes.Count, Sha256(manifest));
        }
    }

    private static string BuildSummary(CaseRecord record)
    {
        static string H(string? value) => HtmlEncoder.Default.Encode(value ?? string.Empty);
        var timeline = string.Join("", record.Timeline.OrderBy(item => item.At).Select(item => $"<li><time>{H(item.At.ToString("u"))}</time> <strong>{H(item.Kind)}</strong> — {H(item.Detail)}</li>"));
        var messages = string.Join("", record.Messages.OrderBy(item => item.ReceivedAt).Select(item => $"<article><h3>{H(ScamAnalysisService.Redact(item.Subject))}</h3><p>{H(item.ReceivedAt.ToString("u"))} · {H(ScamAnalysisService.Redact(item.Sender))}</p><pre>{H(ScamAnalysisService.Redact(item.Body))}</pre></article>"));
        return $"<!doctype html><html><head><meta charset=\"utf-8\"><title>{H(ScamAnalysisService.Redact(record.Title))}</title><style>body{{font:15px system-ui;max-width:900px;margin:40px auto;color:#172033}}pre{{white-space:pre-wrap;background:#f3f5f8;padding:16px;border-radius:8px}}article{{border-top:1px solid #ccd3dc;padding-top:12px}}.badge{{display:inline-block;padding:4px 10px;background:#e8eefc;border-radius:16px}}</style></head><body><h1>{H(ScamAnalysisService.Redact(record.Title))}</h1><p class=\"badge\">{H(record.StatusDisplay)}</p><p>Case {record.Id} · created {H(record.CreatedAt.ToString("u"))} · updated {H(record.UpdatedAt.ToString("u"))}</p><h2>Assessment</h2><p>{H(record.Analysis?.Summary ?? "Not available")}</p><h2>Notes</h2><pre>{H(ScamAnalysisService.Redact(record.Notes))}</pre><h2>Timeline</h2><ol>{timeline}</ol><h2>Redacted messages</h2>{messages}</body></html>";
    }

    private static string FormatHeaders(InboxMessage message) => string.Join("\r\n", message.Headers
        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
        .SelectMany(pair => pair.Value.Select(value => $"{pair.Key}: {value}")));

    private static string SafeName(string value)
    {
        var cleaned = new string(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "message" : cleaned[..Math.Min(60, cleaned.Length)];
    }

    private static void Add(ZipArchive archive, IDictionary<string, string> hashes, string path, string content)
    {
        AddWithoutHash(archive, path, content);
        hashes[path] = Sha256(content);
    }

    private static void AddWithoutHash(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Sha256(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
