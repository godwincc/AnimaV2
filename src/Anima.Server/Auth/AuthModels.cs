using System.ComponentModel.DataAnnotations;

namespace Anima.Server.Auth;

// Validation attributes target the record's constructor PARAMETER (no `property:` prefix)
// deliberately -- targeting the synthesized property instead trips a real ASP.NET Core bug where
// MVC's model binder finds validation metadata on the property but expects it on the parameter for
// record types, and throws InvalidOperationException on every single request instead of a normal
// 400 (discovered while testing the password-reset fix; pre-existing, unrelated to that fix).
public record RegisterRequest(
    [Required, MinLength(3), MaxLength(32)] string Username,
    [Required, EmailAddress] string Email,
    [Required, MinLength(6)] string Password);

public record LoginRequest(
    [Required] string Username,
    [Required] string Password);

public record AuthResponse(Guid AccountId, string Username, string Token, DateTime ExpiresAtUtc);

public record RequestPasswordResetRequest([Required, EmailAddress] string Email);

public record ResetPasswordRequest(
    [Required] string Token,
    [Required, MinLength(6)] string NewPassword);
