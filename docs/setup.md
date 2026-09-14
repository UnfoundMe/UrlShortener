# Setup

Step-by-step instructions to get `UrlShortener` running locally right after cloning the repo.
See [README.md](../README.md) for a quick-reference version and [CLAUDE.md](../CLAUDE.md) for the
stack/structure overview.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (running) — used both for
  Postgres/Redis via Docker Compose *and* by `UrlShortener.Api.Tests` (Testcontainers spins up
  ephemeral Postgres/Redis containers for integration tests)

## Option A — Run everything via Docker Compose (recommended)

1. Copy the env template and fill in a real password:
   ```
   cp .env.example .env
   ```
   Edit `.env` and set `POSTGRES_PASSWORD` to something other than the placeholder.

2. Build and start the stack (api + postgres + redis):
   ```
   docker compose up --build -d
   ```

3. Apply database migrations. This is a deliberate, explicit step — the API image never
   auto-migrates on startup (see Task 12 in
   [url-shortener-tasks.md](url-shortener-tasks.md#task-12--explicit-migration-deploy-step)) —
   so a fresh Postgres volume stays schema-less until you run:
   ```
   docker compose run --rm migrate
   ```
   Safe to re-run; it's a no-op against an already-migrated database.

4. Confirm it's up:
   ```
   curl http://localhost:8080/health
   ```

5. Mint an API key (needed for `POST /links` and `GET /links/{code}/analytics` — the redirect
   endpoint itself is public):
   ```
   docker exec urlshortener-api-1 dotnet UrlShortener.Api.dll seed-api-key --owner "Your Name"
   ```
   Copy the printed key now — it's not shown again — and send it as the `X-Api-Key` header.

6. Try it end to end:
   ```
   curl -X POST http://localhost:8080/links \
     -H "X-Api-Key: <your key>" -H "Content-Type: application/json" \
     -d '{"originalUrl":"https://example.com"}'

   curl -i http://localhost:8080/<shortCode>   # 302 redirect
   ```

To stop the stack without losing data: `docker compose down` (no `-v`). Add `-v` only if you
want to wipe Postgres and start completely fresh.

## Option B — Run the API directly with `dotnet run` (Postgres/Redis still via Docker)

Useful for iterating on the API itself without rebuilding a Docker image each time.

1. Start just the datastores:
   ```
   docker compose up -d postgres redis
   ```

2. Apply migrations from the host (needs the EF Core CLI tool once: `dotnet tool install
   --global dotnet-ef`):
   ```
   dotnet ef database update --project UrlShortener.Api
   ```

3. Run the API. It reads `ConnectionStrings:Postgres`/`ConnectionStrings:Redis` from
   `UrlShortener.Api/appsettings.Development.json` by default — update those if your `.env`
   ports differ from the defaults (5432/6379):
   ```
   dotnet run --project UrlShortener.Api
   ```

4. Mint an API key the same way as above, but locally:
   ```
   dotnet run --project UrlShortener.Api -- seed-api-key --owner "Your Name"
   ```

## Running the tests

```
dotnet test
```

Docker must be running — `UrlShortener.Api.Tests` uses Testcontainers to spin up real,
throwaway Postgres/Redis containers per test collection rather than mocking them.

## Notes

- `POST /auth/api-keys` (self-serve key creation) is disabled by default — it's unauthenticated
  with no rate limiting, so only enable it locally/for demos by setting
  `ENABLE_SELF_SERVE_API_KEYS=true` in `.env` (Compose) or `Auth:EnableSelfServeApiKeys` in
  `appsettings.Development.json` (bare `dotnet run`).
- Swagger/OpenAPI UI is available at `/swagger` in the `Development` environment only.
