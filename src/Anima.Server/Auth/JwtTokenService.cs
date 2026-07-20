using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Anima.Server.Data.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Anima.Server.Auth;

// Issues the one long-lived JWT a login mints (see JwtOptions' own comment on why there's no
// refresh-token rotation). Also the thing that both the HTTP JwtBearer handler and the SignalR hub
// authenticate against -- SignalR's documented pattern for browser/WebSocket clients is to pass the
// same bearer token via an "access_token" query string param, since a WebSocket handshake can't set
// an Authorization header; Program.cs wires JwtBearerEvents.OnMessageReceived to read it from there
// for requests under the hub's own path. This is the standard ASP.NET Core SignalR + JWT pattern,
// not a bespoke workaround.
public class JwtTokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public const string AccountIdClaimType = "aid";

    public (string Token, DateTime ExpiresAtUtc) IssueToken(AccountEntity account)
    {
        var expires = DateTime.UtcNow.AddDays(_options.LifetimeDays);

        var claims = new[]
        {
            new Claim(AccountIdClaimType, account.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, account.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
