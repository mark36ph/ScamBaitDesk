using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

namespace ScamBaitDesk.Services;

public sealed class ImapInboxService
{
    public async Task<IReadOnlyList<InboxMessage>> FetchAsync(
        InboxSettings settings,
        string password,
        int maximum = 50,
        CancellationToken cancellationToken = default)
    {
        using var client = new ImapClient();
        client.ServerCertificateValidationCallback = (_, _, _, errors) => errors == System.Net.Security.SslPolicyErrors.None;
        await client.ConnectAsync(settings.Host, settings.Port, SecureSocketOptions.SslOnConnect, cancellationToken);
        await client.AuthenticateAsync(settings.Username, password, cancellationToken);
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var start = Math.Max(0, client.Inbox.Count - maximum);
        var messages = new List<InboxMessage>();
        for (var index = client.Inbox.Count - 1; index >= start; index--)
        {
            var item = await client.Inbox.GetMessageAsync(index, cancellationToken);
            var body = item.TextBody ?? StripHtml(item.HtmlBody ?? string.Empty);
            messages.Add(new InboxMessage(
                item.MessageId ?? $"imap-{index}",
                item.Subject ?? "(No subject)",
                item.From.Mailboxes.FirstOrDefault()?.Address ?? item.From.ToString(),
                item.Date,
                body.Length > 30_000 ? body[..30_000] : body));
        }

        await client.DisconnectAsync(true, cancellationToken);
        return messages;
    }

    private static string StripHtml(string html) =>
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " "));
}
