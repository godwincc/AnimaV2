using System.ComponentModel.DataAnnotations;

namespace Anima.Server.Auth;

public record RegisterRequest(
    [property: Required, MinLength(3), MaxLength(32)] string Username,
    [property: Required, EmailAddress] string Email,
    [property: Required, MinLength(6)] string Password);

public record LoginRequest(
    [property: Required] string Username,
    [property: Required] string Password);

public record AuthResponse(Guid AccountId, string Username, string Token, DateTime ExpiresAtUtc);

public record RequestPasswordResetRequest([property: Required, EmailAddress] string Email);

public record ResetPasswordRequest(
    [property: Required] string Token,
    [property: Required, MinLength(6)] string NewPassword);
