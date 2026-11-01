using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Options;
using ProtoFast.Auth.Api.Configuration;
using ProtoFast.ProtoStrap;

namespace ProtoFast.Auth.Api.Email;

/// <summary>
/// <see cref="IEmailSender"/> over SES's v2 API with auth's own role, which ProtoStrap grants
/// for the spec's <c>uses: [email]</c> (its docs/email.md): no SMTP password exists in an
/// account. It sends from <c>Smtp:From</c> when that is set, otherwise from
/// <c>no-reply@</c> the protofast host's hostname, which each account's email domains include.
/// </summary>
public sealed class SesEmailSender(
    IAmazonSimpleEmailServiceV2 ses,
    IOptions<SmtpOptions> options,
    IConfiguration configuration,
    ILogger<SesEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _smtp = options.Value;

    public bool IsConfigured => From is not null;

    private string? From =>
        configuration["Smtp:From"] is { Length: > 0 } from ? from
        : Replica.Hostname("protofast") is { } host ? "no-reply@" + host
        : null;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var from = From ?? throw new InvalidOperationException("No sender: set Smtp:From or HOST_PROTOFAST.");
        var body = new Body { Text = new Content { Data = message.Text, Charset = "UTF-8" } };
        if (!string.IsNullOrEmpty(message.Html))
        {
            body.Html = new Content { Data = message.Html, Charset = "UTF-8" };
        }

        await ses.SendEmailAsync(new SendEmailRequest
        {
            FromEmailAddress = $"{_smtp.FromDisplayName} <{from}>",
            Destination = new Destination { ToAddresses = [message.To] },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = message.Subject, Charset = "UTF-8" },
                    Body = body,
                },
            },
        }, ct).ConfigureAwait(false);

        // As over SMTP, the address never goes in a log line.
        logger.LogInformation("Sent {Subject} via SES", message.Subject);
    }
}
