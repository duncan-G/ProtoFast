using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Options;
using ProtoFast.Auth.Api.Configuration;

namespace ProtoFast.Auth.Api.Email;

/// <summary>
/// <see cref="IEmailSender"/> over plain SMTP submission.
///
/// <para>A client is built per message rather than held: <see cref="SmtpClient"/> serialises
/// sends on one instance and a shared one would turn concurrent requests into a queue. The
/// volume here is one message per account change, so the connection cost is irrelevant next to
/// that.</para>
/// </summary>
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _smtp = options.Value;

    public bool IsConfigured => _smtp.IsConfigured;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!_smtp.IsConfigured)
        {
            throw new InvalidOperationException("No SMTP relay is configured; auth-svc cannot send mail.");
        }

        using var client = new SmtpClient(_smtp.Host, _smtp.Port)
        {
            EnableSsl = _smtp.StartTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };

        if (!string.IsNullOrEmpty(_smtp.User))
        {
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(_smtp.User, _smtp.Password);
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(_smtp.From, _smtp.FromDisplayName),
            Subject = message.Subject,
            Body = message.Text,
            IsBodyHtml = false,
        };
        mail.To.Add(message.To);

        // multipart/alternative, text part first: that order is what tells a client the HTML is
        // the richer rendering of the same message rather than an attachment to it. Body above
        // stays set so a relay or client that ignores the views still has the text.
        if (!string.IsNullOrEmpty(message.Html))
        {
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.Text, Encoding.UTF8, MediaTypeNames.Text.Plain));
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.Html, Encoding.UTF8, MediaTypeNames.Text.Html));
        }

        await client.SendMailAsync(mail, ct).ConfigureAwait(false);

        // The address is the whole point of the message and never goes in a log line; the fact
        // that a send succeeded is what an operator needs when a user says nothing arrived.
        logger.LogInformation("Sent {Subject} via {Host}", message.Subject, _smtp.Host);
    }
}
