using Anima.Server.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Anima.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService authService, JwtTokenService tokenService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await authService.RegisterAsync(request.Username, request.Email, request.Password, ct);
        if (result.Outcome == RegisterOutcome.UsernameTaken)
            return Conflict(new { error = "Username is already taken." });

        var (token, expires) = tokenService.IssueToken(result.Account!);
        return Ok(new AuthResponse(result.Account!.Id, result.Account.Username, token, expires));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request.Username, request.Password, ct);
        if (result.Outcome == LoginOutcome.InvalidCredentials)
            return Unauthorized(new { error = "Invalid username or password." });

        var (token, expires) = tokenService.IssueToken(result.Account!);
        return Ok(new AuthResponse(result.Account!.Id, result.Account.Username, token, expires));
    }

    [HttpPost("password-reset/request")]
    public async Task<ActionResult<RequestPasswordResetResponse>> RequestPasswordReset(RequestPasswordResetRequest request, CancellationToken ct)
    {
        var result = await authService.RequestPasswordResetAsync(request.Email, ct);
        return Ok(result);
    }

    [HttpPost("password-reset/confirm")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await authService.ResetPasswordAsync(request.Token, request.NewPassword, ct);
        if (result.Outcome == ResetPasswordOutcome.InvalidOrExpiredToken)
            return BadRequest(new { error = "Invalid or expired reset token." });

        return Ok();
    }
}
