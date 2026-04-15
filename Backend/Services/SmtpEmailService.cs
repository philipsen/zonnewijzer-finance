using System.Net;
using System.Net.Mail;

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

        public async Task SendAsync(string toEmail, string subject, string htmlBody)
        {
            var host = _configuration["Smtp:Host"];
            var username = _configuration["Smtp:Username"];
            var password = _configuration["Smtp:Password"];
            var fromEmail = _configuration["Smtp:FromEmail"];
            var fromName = _configuration["Smtp:FromName"] ?? "Zonnewijzer Finance";
            var portText = _configuration["Smtp:Port"];
            var enableSslText = _configuration["Smtp:EnableSsl"];

            if (string.IsNullOrWhiteSpace(host) ||
                string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(fromEmail))
            {
                throw new InvalidOperationException("SMTP settings are incomplete. Configure Smtp:Host/Username/Password/FromEmail.");
            }

            var port = int.TryParse(portText, out var parsedPort) ? parsedPort : 587;
            var enableSsl = !bool.TryParse(enableSslText, out var parsedEnableSsl) || parsedEnableSsl;

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                Credentials = new NetworkCredential(username, password)
            };

            using var message = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(toEmail);

            _logger.LogInformation("Sending email to {ToEmail} via SMTP host {Host}", toEmail, host);
            await client.SendMailAsync(message);
        }
    }
}
