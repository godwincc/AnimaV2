using Microsoft.Extensions.Logging;

namespace Anima.Server.Email;

// Real SMTP/provider wiring is out of scope for this pass -- this just logs what a real
// mailer would have sent, so the reset token never has to leave the server (in particular,
// never gets echoed back to the HTTP caller). Swap for a real IEmailSender implementation
// when SMTP/SendGrid/SES wiring happens.
public class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendPasswordResetEmailAsync(string toEmail, string rawToken, DateTime expiresAtUtc, CancellationToken ct = default)
    {
        logger.LogInformation(
            "[password reset] To: {Email} | Token: {Token} | Expires: {ExpiresAtUtc:u}",
            toEmail, rawToken, expiresAtUtc);
        return Task.CompletedTask;
    }
}
