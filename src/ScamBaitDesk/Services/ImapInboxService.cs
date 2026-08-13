using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

namespace ScamBaitDesk.Services;

public sealed class ImapInboxService
{
    public async Task<IReadOnlyList<InboxMessage>> FetchAsync(
        InboxSettings settings,
        string credential,
        bool useOAuth = false,
        int maximum = 50,
        CancellationToken cancellationToken = default)
    {
        using var client = new ImapClient();
        client.ServerCertificateValidationCallback = (_, _, _, errors) => errors == System.Net.Security.SslPolicyErrors.None;
        await client.ConnectAsync(settings.Host, settings.Port, SecureSocketOptions.SslOnConnect, cancellationToken);
        if (useOAuth) await client.AuthenticateAsync(new SaslMechanismOAuth2(settings.Username, credential), cancellationToken);
        else await client.AuthenticateAsync(settings.Username, credential, cancellationToken);
        var messages = new List<InboxMessage>();
        await FetchFolderAsync(client.Inbox, messages, maximum, false, cancellationToken);
        try
        {
            var sent = client.GetFolder(SpecialFolder.Sent);
            await FetchFolderAsync(sent, messages, maximum, true, cancellationToken);
        }
        catch (FolderNotFoundException) { }
        await client.DisconnectAsync(true, cancellationToken);
        return messages.OrderByDescending(message => message.ReceivedAt).ToList();
    }

    private static async Task FetchFolderAsync(IMailFolder folder, List<InboxMessage> messages, int maximum, bool outbound, CancellationToken cancellationToken)
    {
        await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        var start = Math.Max(0, folder.Count - maximum);
        for (var index = folder.Count - 1; index >= start; index--)
        {
            var item = await folder.GetMessageAsync(index, cancellationToken);
            var body = item.TextBody ?? StripHtml(item.HtmlBody ?? string.Empty);
            messages.Add(new InboxMessage(
                item.MessageId ?? $"imap-{index}",
                item.Subject ?? "(No subject)",
                item.From.Mailboxes.FirstOrDefault()?.Address ?? item.From.ToString(),
                item.Date,
                body.Length > 30_000 ? body[..30_000] : body)
            {
                IsOutbound = outbound,
                Recipient = item.To.Mailboxes.FirstOrDefault()?.Address ?? item.To.ToString(),
                Attachments = item.Attachments.Select(entity => new AttachmentRecord(
                    entity.ContentDisposition?.FileName ?? entity.ContentType.Name ?? "unnamed-attachment",
                    entity.ContentType.MimeType,
                    entity.ContentDisposition?.Size,
                    entity.ContentId ?? string.Empty)).ToList(),
                Headers = item.Headers
                    .GroupBy(header => header.Field, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(header => header.Value).ToList(),
                        StringComparer.OrdinalIgnoreCase)
            });
        }
    }

    private static string StripHtml(string html) =>
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " "));
}
