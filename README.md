# UrlShortener

A .NET 10 minimal-API URL shortener backed by PostgreSQL (system of record) and Redis
(cache-aside for the redirect hot path). See [CLAUDE.md](CLAUDE.md) for the stack/structure
overview, [docs/url-shortener-plan.md](docs/url-shortener-plan.md) for the design rationale, and
[docs/url-shortener-tasks.md](docs/url-shortener-tasks.md) for the build-order task breakdown.

New to this repo? [docs/setup.md](docs/setup.md) walks through getting it running locally right
after cloning, step by step. What follows here is the quick-reference version.

## Running locally (no Docker)

Requires a reachable Postgres and Redis (connection strings in `appsettings.json` /
`appsettings.Development.json`, or override via `ConnectionStrings__Postgres` /
`ConnectionStrings__Redis` env vars).

```
dotnet ef database update --project UrlShortener.Api
dotnet run --project UrlShortener.Api
```

Mint an API key (required for `POST /links` and `GET /links/{code}/analytics` — the redirect
endpoint itself is public):

```
dotnet run --project UrlShortener.Api -- seed-api-key --owner "Your Name"
```

## Running via Docker Compose

```
cp .env.example .env      # fill in a real POSTGRES_PASSWORD
docker compose up --build -d
```

The `api` image never runs `dotnet ef database update` (or any other auto-migration) on
startup — a fresh Postgres volume stays schema-less until you apply migrations explicitly, so
schema changes are a deliberate, race-safe deploy step rather than something every replica races
to do on boot:

```
docker compose run --rm migrate
```

This is idempotent — re-running it against an already-migrated database is a no-op. Then:

```
curl http://localhost:8080/health
```

`docker compose down` (without `-v`) stops the stack but keeps the `postgres-data` volume, so
links and analytics survive a restart; add `-v` only if you want to wipe Postgres and start over.

Self-serve API key creation (`POST /auth/api-keys`) is unauthenticated with no rate limiting, so
it's off by default — set `ENABLE_SELF_SERVE_API_KEYS=true` in `.env` for local/demo use only.

## Tests

```
dotnet test
```

`UrlShortener.Api.Tests` uses Testcontainers for Postgres/Redis, so Docker must be running.
