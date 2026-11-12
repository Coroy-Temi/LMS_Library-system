using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Mail;

namespace LibraryManagementSystem.Services;

public interface IEmailSender
{
    Task SendVerificationCodeAsync(string recipientEmail, string recipientName, string code,
        TimeSpan validFor, CancellationToken cancellationToken = default);
}

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendVerificationCodeAsync(string recipientEmail, string recipientName, string code,
        TimeSpan validFor, CancellationToken cancellationToken = default)
    {
        var host = configuration["Email:SmtpHost"];
        var senderEmail = configuration["Email:SenderEmail"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(senderEmail))
            throw new InvalidOperationException("Email delivery is not configured. Add the SMTP settings before accepting local registrations.");

        var senderName = configuration["Email:SenderName"] ?? "LibraryForge";
        using var message = new MailMessage
        {
            From = new MailAddress(senderEmail, senderName),
            Subject = $"{code} is your LibraryForge verification code",
            Body = $"""
                Hello {recipientName},

                Your LibraryForge verification code is:

                {code}

                This code expires in {(int)validFor.TotalMinutes} minutes. If you did not create this account, you can ignore this message.

                LibraryForge
                """,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(recipientEmail, recipientName));

        using var client = new SmtpClient(host, configuration.GetValue("Email:SmtpPort", 587))
        {
            EnableSsl = configuration.GetValue("Email:EnableSsl", true),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(configuration["Email:Username"] ?? senderEmail,
                configuration["Email:Password"] ?? string.Empty)
        };
        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }
}
