namespace Anima.Server.Email;

// Seam for real email delivery (SendGrid/SES/etc.), out of scope for this pass -- see
// LogEmailSender for the current stand-in. Nothing outside AuthService should need to know
// which implementation is wired up.
public interface IEmailSender
{
    Task SendPasswordResetEmailAsync(string toEmail, string rawToken, DateTime expiresAtUtc, CancellationToken ct = default);
}
