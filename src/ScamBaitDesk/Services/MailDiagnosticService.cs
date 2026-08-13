using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace ScamBaitDesk.Services;

public sealed class MailDiagnosticService
{
    public async Task<IReadOnlyList<ConnectionDiagnostic>> RunAsync(InboxSettings settings, string credential, bool useOAuth, CancellationToken cancellationToken = default)
    {
        var results = new List<ConnectionDiagnostic>();
        try
        {
            using var imap = new ImapClient();
            await imap.ConnectAsync(settings.Host, settings.Port, SecureSocketOptions.SslOnConnect, cancellationToken);
            results.Add(new("IMAP TLS", true, $"Connected to {settings.Host}:{settings.Port}."));
            if (useOAuth) await imap.AuthenticateAsync(new SaslMechanismOAuth2(settings.Username, credential), cancellationToken); else await imap.AuthenticateAsync(settings.Username, credential, cancellationToken);
            results.Add(new("IMAP authentication", true, "Dedicated mailbox authenticated."));
            await imap.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex) { results.Add(new("IMAP", false, ex.Message)); }
        try
        {
            using var smtp = new SmtpClient();
            var socket = settings.SmtpUseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await smtp.ConnectAsync(settings.SmtpHost, settings.SmtpPort, socket, cancellationToken);
            results.Add(new("SMTP TLS", true, $"Connected to {settings.SmtpHost}:{settings.SmtpPort}."));
            if (useOAuth) await smtp.AuthenticateAsync(new SaslMechanismOAuth2(settings.Username, credential), cancellationToken); else await smtp.AuthenticateAsync(settings.Username, credential, cancellationToken);
            results.Add(new("SMTP authentication", true, "Outbound service authenticated; no message was sent."));
            await smtp.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex) { results.Add(new("SMTP", false, ex.Message)); }
        return results;
    }
}
