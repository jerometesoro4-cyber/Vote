using System.Net;
using System.Net.Mail;
using System.Text;

namespace voting.Services;

public interface IEmailService
{
    Task SendVerificationCodeAsync(string toEmail, string fullName, string code, string purpose, CancellationToken cancellationToken = default);
}

public class EmailService(
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<EmailService> logger) : IEmailService
{
    public async Task SendVerificationCodeAsync(
        string toEmail,
        string fullName,
        string code,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(configuration["Email:SimulateFailure"], "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Simulated email delivery failure.");
        }

        var smtpHost = configuration["Email:SmtpHost"]?.Trim();
        var pickupDir = configuration["Email:PickupDirectory"]?.Trim();
        var fromAddress = configuration["Email:FromAddress"]?.Trim();
        var fromName = configuration["Email:FromName"]?.Trim();
        if (string.IsNullOrWhiteSpace(fromName))
        {
            fromName = "ACMCL Vote";
        }

        var hasSmtpHost = !string.IsNullOrWhiteSpace(smtpHost);
        var hasPickupDir = !string.IsNullOrWhiteSpace(pickupDir);

        if (hasSmtpHost && hasPickupDir)
        {
            throw new InvalidOperationException(
                "Email configuration cannot enable both Email:SmtpHost and Email:PickupDirectory. Clear Email:PickupDirectory for live Brevo SMTP delivery, or clear Email:SmtpHost for local test pickup.");
        }

        if (!hasSmtpHost && !hasPickupDir)
        {
            throw new InvalidOperationException(
                "Email delivery is not configured. Configure Brevo SMTP (Email:SmtpHost, Email:SmtpPort, Email:UseSsl, Email:SmtpUsername, Email:SmtpPassword, and a verified Email:FromAddress), or set Email:PickupDirectory in Development for local test pickup.");
        }

        var isReset = string.Equals(purpose, "ResetPassword", StringComparison.Ordinal);
        var subject = isReset
            ? "ACMCL Vote password recovery code"
            : "ACMCL Vote email verification code";

        var safeName = string.IsNullOrWhiteSpace(fullName) ? "Voter" : fullName.Trim().Replace("\r", " ").Replace("\n", " ");
        var body = isReset
            ? $"""
               Hello {safeName},

               Use this 6-digit recovery code to reset your ACMCL Vote password:

               {code}

               This code expires in 15 minutes and can only be used once.
               If you did not request a password reset, you can safely ignore this message.
               """
            : $"""
               Hello {safeName},

               Use this 6-digit verification code to verify your email address for ACMCL Vote:

               {code}

               This code expires in 15 minutes and can only be used once.
               If you did not request this code, you can safely ignore this message.
               """;

        if (hasPickupDir)
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "Email:PickupDirectory is for local testing only and cannot be used in Production. Configure Brevo SMTP (Email:SmtpHost, Email:SmtpUsername, Email:SmtpPassword, and Email:FromAddress).");
            }

            var localFrom = string.IsNullOrWhiteSpace(fromAddress) ? "no-reply@acmcl.edu.ph" : fromAddress;
            var resolvedDir = Path.IsPathRooted(pickupDir!)
                ? pickupDir!
                : Path.Combine(environment.ContentRootPath, pickupDir!);
            Directory.CreateDirectory(resolvedDir);

            var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.eml";
            var fullPath = Path.Combine(resolvedDir, fileName);
            var cleanTo = toEmail.Trim().Replace("\r", "").Replace("\n", "");
            var cleanFrom = localFrom.Trim().Replace("\r", "").Replace("\n", "");
            var cleanFromName = fromName.Replace("\r", "").Replace("\n", "");

            var eml = $"""
                From: "{cleanFromName}" <{cleanFrom}>
                To: <{cleanTo}>
                Subject: {subject}
                Date: {DateTimeOffset.UtcNow:R}
                X-ACMCL-Purpose: {purpose}
                MIME-Version: 1.0
                Content-Type: text/plain; charset=utf-8

                {body}
                """;

            await File.WriteAllTextAsync(fullPath, eml, Encoding.UTF8, cancellationToken);
            logger.LogInformation("Wrote {Purpose} message to local test pickup directory.", purpose);
            return;
        }

        var rawPort = configuration["Email:SmtpPort"]?.Trim();
        var port = 587;
        if (!string.IsNullOrEmpty(rawPort) && (!int.TryParse(rawPort, out port) || port < 1 || port > 65535))
        {
            throw new InvalidOperationException("Email:SmtpPort must be a valid TCP port between 1 and 65535 (Brevo STARTTLS uses 587).");
        }

        var rawUseSsl = configuration["Email:UseSsl"]?.Trim();
        var useSsl = true;
        if (!string.IsNullOrEmpty(rawUseSsl) && !bool.TryParse(rawUseSsl, out useSsl))
        {
            throw new InvalidOperationException("Email:UseSsl must be 'true' or 'false' (set 'true' for Brevo STARTTLS on port 587).");
        }

        var username = configuration["Email:SmtpUsername"]?.Trim();
        var password = configuration["Email:SmtpPassword"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "SMTP credentials are missing. Set Email:SmtpUsername (your Brevo SMTP Login) and Email:SmtpPassword (your Brevo SMTP key) before sending live email.");
        }

        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            throw new InvalidOperationException(
                "Email:FromAddress is missing. Set Email:FromAddress to a sender email address verified in Brevo before sending live email.");
        }

        MailAddress validatedFrom;
        try
        {
            validatedFrom = new MailAddress(fromAddress, fromName);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "Email:FromAddress is not a valid email address. Set Email:FromAddress to a verified sender address in Brevo.",
                ex);
        }

        if (!string.Equals(validatedFrom.Address, fromAddress, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Email:FromAddress must contain only the verified sender email address (set the display name in Email:FromName).");
        }

        if (validatedFrom.Address.EndsWith("@smtp-brevo.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Email:FromAddress must be a verified sender email address in Brevo, not the '@smtp-brevo.com' SMTP login.");
        }

        using var message = new MailMessage
        {
            From = validatedFrom,
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(toEmail.Trim()));

        using var client = new SmtpClient(smtpHost!, port)
        {
            EnableSsl = useSsl,
            Timeout = 15000,
            Credentials = new NetworkCredential(username, password)
        };

        await client.SendMailAsync(message, cancellationToken);
        logger.LogInformation("Sent {Purpose} message via configured SMTP host.", purpose);
    }
}
