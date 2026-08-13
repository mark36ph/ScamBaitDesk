using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ScamBaitDesk.Services;

public sealed class SmtpEngagementService
{
    public async Task<string> SendAsync(
        InboxSettings settings,
        string credential,
        string recipient,
        string subject,
        string body,
        InboxMessage replyTo,
        CancellationToken cancellationToken = default,
        bool useOAuth = false)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
            throw new InvalidOperationException("Configure the SMTP server in Inbox settings first.");

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(settings.Username));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };
        message.Headers.Add("X-ScamBaitDesk-Manual-Send", "true");
        if (!string.IsNullOrWhiteSpace(replyTo.Id))
        {
            message.InReplyTo = replyTo.Id;
            if (replyTo.Headers.TryGetValue("References", out var references))
                foreach (var value in references.SelectMany(value => value.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
                    if (!message.References.Contains(value)) message.References.Add(value);
            if (!message.References.Contains(replyTo.Id)) message.References.Add(replyTo.Id);
        }

        using var client = new SmtpClient();
        var socket = settings.SmtpUseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, socket, cancellationToken);
        if (useOAuth) await client.AuthenticateAsync(new SaslMechanismOAuth2(settings.Username, credential), cancellationToken);
        else await client.AuthenticateAsync(settings.Username, credential, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        return message.MessageId ?? string.Empty;
    }
}
