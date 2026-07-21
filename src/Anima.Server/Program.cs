using System.Text;
using Anima.Server.Auth;
using Anima.Server.Data;
using Anima.Server.Data.Entities;
using Anima.Server.Email;
using Anima.Server.Hubs;
using Anima.Server.Persistence;
using Anima.Server.Sessions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddDbContext<AnimaDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is missing.")));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<PasswordHasher<AccountEntity>>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<IEmailSender, LogEmailSender>();

builder.Services.AddSingleton<AccountLockRegistry>();
builder.Services.AddScoped<SanctumRosterRepository>();
builder.Services.AddScoped<PersistentLedgerRepository>();
builder.Services.AddScoped<AccountRepository>();
builder.Services.AddScoped<AccountArtifactStatRepository>();
builder.Services.AddScoped<PendingWeaveRepository>();
builder.Services.AddScoped<PendingPurchasedEmberRepository>();
builder.Services.AddSingleton<PlayerSessionRegistry>();

builder.Services.AddSignalR();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration section is missing.");

// ConfigurationBinder does NOT enforce JwtOptions.SigningKey's `required` modifier when only that
// one member is absent (Issuer/Audience/LifetimeDays are enough for Get<JwtOptions>() above to
// succeed) -- it silently binds to null, which otherwise wouldn't fail until the first HTTP
// request hits the JWT bearer handler's lazily-created options, as an opaque ArgumentNullException
// deep in SymmetricSecurityKey construction. Checked explicitly here so a missing signing key fails
// fast at startup with the same clear-error style as the ConnectionStrings:Default check above.
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
    throw new InvalidOperationException("Jwt:SigningKey is missing. Set it via `dotnet user-secrets` for local dev or the Jwt__SigningKey env var for deployment -- see src/Anima.Server/README-dev-secrets.md.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
        };

        // SignalR/WebSocket clients (Godot Web included) can't set a custom Authorization header
        // on the transport handshake, so the documented ASP.NET Core pattern is to accept the same
        // bearer token via an "access_token" query string param on requests under the hub's own
        // path instead. HTTP API callers (register/login/etc.) are unaffected and keep using a
        // normal Authorization: Bearer header.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/game"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GameHub>("/hubs/game");

app.Run();

// Exposed so WebApplicationFactory<Program> can be used from the test project.
public partial class Program;
