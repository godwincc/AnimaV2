# Local dev secrets (Anima.Server)

`appsettings.json` / `appsettings.Development.json` deliberately do **not** contain
`ConnectionStrings:Default` or `Jwt:SigningKey` — those are secrets and must never be checked in.
`Program.cs` throws a clear startup error if either is missing, so a fresh checkout fails fast
instead of silently running with a weak/shared key.

## One-time local setup (non-Docker `dotnet run`)

Run these once from `src/Anima.Server/` (this project already has a `UserSecretsId` in its
`.csproj`, so `dotnet run`/`dotnet watch` in the `Development` environment picks these up
automatically — no extra wiring needed):

```
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=anima_dev;Username=anima;Password=anima_dev_password"
dotnet user-secrets set "Jwt:SigningKey" "<paste a real random key here>"
```

Generate a random key (any of these works — HS256 needs >=32 bytes, this gives 64):

```
# bash / git-bash
openssl rand -base64 64 | tr -d '\n'

# PowerShell
[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(64))
```

The Postgres connection string above matches `deployment/docker-compose.yaml`'s default
`POSTGRES_USER`/`POSTGRES_PASSWORD`/`POSTGRES_DB` — only change it if you've changed those too
(see `deployment/.env.example`).

## Docker/deployment path

Real values are supplied via environment variables (`ConnectionStrings__Default`,
`Jwt__SigningKey` — double underscore is ASP.NET Core's config-key separator for env vars), not
appsettings files. See `deployment/docker-compose.yaml` and `deployment/.env.example`.

**Note (as of this writing):** only Postgres is containerized in `docker-compose.yaml` today —
`Anima.Server` itself has no Dockerfile/service entry yet and is run directly via `dotnet run`
against the dockerized Postgres. The `ConnectionStrings__Default`/`Jwt__SigningKey` env-var
pattern above is what a future `Anima.Server` service block should use once one exists.
