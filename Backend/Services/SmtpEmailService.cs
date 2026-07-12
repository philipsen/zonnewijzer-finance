using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Backend.Services
{
    public class SmtpEmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SmtpEmailService> _logger;

        public SmtpEmailService(IConfiguration configuration, ILogger<SmtpEmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendAsync(string toEmail, string subject, string htmlBody, EmailAttachment? attachment = null, string? bccEmail = null)
        {
            var host = _configuration["Smtp:Host"];
            var username = _configuration["Smtp:Username"];
            var password = _configuration["Smtp:Password"];
            var fromEmail = _configuration["Smtp:FromEmail"];
            var fromName = _configuration["Smtp:FromName"] ?? "Zonnewijzer Finance";
            var portText = _configuration["Smtp:Port"];
            var enableSslText = _configuration["Smtp:EnableSsl"];
            var skipCertValidation = string.Equals(
                _configuration["Smtp:SkipCertificateValidation"],
                "true",
                StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(host) ||
                string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(fromEmail))
            {
                throw new InvalidOperationException("SMTP settings are incomplete. Configure Smtp:Host/Username/Password/FromEmail.");
            }

            var port = int.TryParse(portText, out var parsedPort) ? parsedPort : 587;
            var enableSsl = !bool.TryParse(enableSslText, out var parsedEnableSsl) || parsedEnableSsl;
            var secureSocketOptions = enableSsl
                ? (port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
                : SecureSocketOptions.None;

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            if (!string.IsNullOrWhiteSpace(bccEmail))
                message.Bcc.Add(MailboxAddress.Parse(bccEmail.Trim()));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            if (attachment != null)
                bodyBuilder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            if (skipCertValidation)
            {
                _logger.LogWarning("SMTP certificate validation is disabled for host {Host}", host);
                client.ServerCertificateValidationCallback = (_, _, _, _) => true;
            }

            _logger.LogInformation("Sending email to {ToEmail} via SMTP host {Host}", toEmail, host);
            await client.ConnectAsync(host, port, secureSocketOptions);
            await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}
