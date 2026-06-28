using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Atendefy.API.Infrastructure.Email;

public record SmtpSettings(
    string Host, int Port, string User, string Password, string FromAddress, string FromName);

/// <summary>
/// Envio de e-mail via SMTP (MailKit). Provider-agnóstico: funciona com Gmail, Zoho, Amazon SES,
/// Resend SMTP, etc. Em dev (SMTP não configurado) NÃO envia — apenas loga o conteúdo, para que o
/// link de verificação fique acessível no log da API.
/// </summary>
public class SmtpEmailSender(SmtpSettings settings, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private bool IsConfigured => !string.IsNullOrWhiteSpace(settings.Host)
        && !string.IsNullOrWhiteSpace(settings.FromAddress);

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        if (!IsConfigured)
        {
            logger.LogWarning(
                "SMTP não configurado — e-mail NÃO enviado (dev). Para: {To} | Assunto: {Subject}\n{Body}",
                to, subject, htmlBody);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        // 465 = SSL implícito; demais portas (587/25) usam STARTTLS quando disponível.
        var socketOption = settings.Port == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTlsWhenAvailable;

        await client.ConnectAsync(settings.Host, settings.Port, socketOption);
        if (!string.IsNullOrWhiteSpace(settings.User))
            await client.AuthenticateAsync(settings.User, settings.Password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);

        logger.LogInformation("E-mail enviado para {To} (assunto: {Subject})", to, subject);
    }
}
