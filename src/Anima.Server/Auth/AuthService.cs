using System.Security.Cryptography;
using System.Text;
using Anima.Server.Data;
using Anima.Server.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Anima.Server.Auth;

public enum RegisterOutcome { Ok, UsernameTaken }
public enum LoginOutcome { Ok, InvalidCredentials }
public enum ResetPasswordOutcome { Ok, InvalidOrExpiredToken }

public record RegisterResult(RegisterOutcome Outcome, AccountEntity? Account);
public record LoginResult(LoginOutcome Outcome, AccountEntity? Account);
public record ResetPasswordResult(ResetPasswordOutcome Outcome);

// Deliberately minimal, not full ASP.NET Core Identity (no roles/lockout/2FA/UserManager
// ceremony) -- spec asked for "keep this simple", and this game needs exactly: username+password,
// a password-reset path keyed off email, and a long-lived login token. PasswordHasher<T> is reused
// from Microsoft.Extensions.Identity.Core purely for its hashing algorithm (PBKDF2, correctly
// salted/iterated) -- pulling in the rest of Identity's machinery for that alone isn't warranted.
public class AuthService(AnimaDbContext db, PasswordHasher<AccountEntity> hasher)
{
    private static string Normalize(string username) => username.Trim().ToUpperInvariant();

    public async Task<RegisterResult> RegisterAsync(string username, string email, string password, CancellationToken ct = default)
    {
        var normalized = Normalize(username);
        var exists = await db.Accounts.AnyAsync(a => a.NormalizedUsername == normalized, ct);
        if (exists) return new RegisterResult(RegisterOutcome.UsernameTaken, null);

        var account = new AccountEntity
        {
            Id = Guid.NewGuid(),
            Username = username.Trim(),
            NormalizedUsername = normalized,
            Email = email.Trim(),
            PasswordHash = string.Empty,
            CreatedAtUtc = DateTime.UtcNow,
        };
        account.PasswordHash = hasher.HashPassword(account, password);

        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);

        return new RegisterResult(RegisterOutcome.Ok, account);
    }

    public async Task<LoginResult> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var normalized = Normalize(username);
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.NormalizedUsername == normalized, ct);
        if (account is null) return new LoginResult(LoginOutcome.InvalidCredentials, null);

        var verify = hasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (verify == PasswordVerificationResult.Failed) return new LoginResult(LoginOutcome.InvalidCredentials, null);

        // Rehash transparently if PasswordHasher's own algorithm/iteration defaults have moved on
        // since this account last logged in -- standard PasswordHasher usage, costs nothing when
        // SuccessRehashNeeded is false.
        if (verify == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = hasher.HashPassword(account, password);
            await db.SaveChangesAsync(ct);
        }

        return new LoginResult(LoginOutcome.Ok, account);
    }

    // Real email DELIVERY is out of scope for this pass -- there is no SMTP/mailer wired up
    // anywhere in this solution. This mints and stores a real, single-use, expiring, hashed token
    // exactly as a real flow would, but hands the raw token straight back to the HTTP caller instead
    // of emailing it, which is a stand-in that must be replaced (e.g. with SendGrid/SES) before this
    // is genuinely usable end-to-end. Also note: returning AccountFound at all (and a token only
    // when true) leaks account existence by email -- acceptable for a pre-launch build per the
    // user's own "keep it simple" framing, but should be revisited (always return the same generic
    // response) once a real mailer removes the need to hand the token back directly.
    public async Task<RequestPasswordResetResponse> RequestPasswordResetAsync(string email, CancellationToken ct = default)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Email == email.Trim(), ct);
        if (account is null) return new RequestPasswordResetResponse(false, null, null);

        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = DateTime.UtcNow.AddHours(1);

        db.PasswordResetTokens.Add(new PasswordResetTokenEntity
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            TokenHash = HashToken(rawToken),
            ExpiresAtUtc = expires,
        });
        await db.SaveChangesAsync(ct);

        return new RequestPasswordResetResponse(true, rawToken, expires);
    }

    public async Task<ResetPasswordResult> ResetPasswordAsync(string rawToken, string newPassword, CancellationToken ct = default)
    {
        var tokenHash = HashToken(rawToken);
        var entry = await db.PasswordResetTokens
            .Where(t => t.TokenHash == tokenHash && t.UsedAtUtc == null && t.ExpiresAtUtc > DateTime.UtcNow)
            .SingleOrDefaultAsync(ct);
        if (entry is null) return new ResetPasswordResult(ResetPasswordOutcome.InvalidOrExpiredToken);

        var account = await db.Accounts.SingleAsync(a => a.Id == entry.AccountId, ct);
        account.PasswordHash = hasher.HashPassword(account, newPassword);
        entry.UsedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new ResetPasswordResult(ResetPasswordOutcome.Ok);
    }

    // Reset tokens are high-entropy (256-bit random) rather than low-entropy like user passwords,
    // so a fast one-way hash (SHA-256) is the right tool here -- unlike PasswordHasher's deliberate
    // PBKDF2 slowness, which exists specifically to blunt brute-forcing LOW-entropy user passwords.
    private static string HashToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
