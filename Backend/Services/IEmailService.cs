namespace Backend.Services
{
    public record EmailAttachment(string FileName, byte[] Content, string ContentType = "application/pdf");

    public interface IEmailService
    {
        Task SendAsync(string toEmail, string subject, string htmlBody, EmailAttachment? attachment = null, string? bccEmail = null);
    }
}
