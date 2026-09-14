# URL Shortener Service — .NET 10 + PostgreSQL + Redis + Docker

## Context

The user wants to build a URL shortener as a learning/small-production project. A minimal scaffold already exists at `C:\Users\vishn\UrlShortener`:

- `UrlShortener.slnx` solution referencing one project, `UrlShortener.Api`
- `.NET 10` Web API (`Microsoft.NET.Sdk.Web`) with `Nullable`/`ImplicitUsings` enabled and `InvariantGlobalization`
- Packages already installed: `Microsoft.EntityFrameworkCore.Design`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `StackExchange.Redis`, `Serilog.AspNetCore` (+ console sink), `Swashbuckle.AspNetCore`
- `Program.cs` already wires up Serilog, `AppDbContext` (Postgres via Npgsql), a singleton `IConnectionMultiplexer` (Redis), Swagger (dev only), and a single `/health` endpoint
- `Data/AppDbContext.cs` is an empty `DbContext` — no entities yet
- `appsettings.json` has placeholder connection strings for Postgres and Redis
- No git repo yet, no domain model, no shortening/redirect endpoints, no auth, no Docker files

Scope confirmed with the user:
- **Core**: shorten a long URL → short code; redirect short code → long URL
- **In scope**: click analytics (counts, timestamps, referrer, user-agent, IP), API-key auth protecting write/read-analytics endpoints (public redirect stays unauthenticated)
- **Out of scope**: custom aliases, link expiration/TTL, rate limiting, admin UI, multi-region deployment (all called out explicitly as future extensions)
- **Scale bar**: "small production app" — real users, must survive restarts/redeploys without data loss, modest traffic (tens of req/s), not over-engineered for horizontal scale but shouldn't be painted into a corner either
- **Deployment**: production-oriented Docker setup (multi-stage Dockerfile, docker-compose with Postgres/Redis, healthchecks, env-based config) — not full k8s/cloud manifests

This plan is a design document with trade-offs for each major decision, plus an ordered build sequence. Nothing is implemented yet.

---

## Recommended Architecture

Single ASP.NET Core minimal-API service, Postgres as the system of record, Redis as a cache-aside layer for the hot redirect path. Short codes come from a Postgres identity/sequence, base62-encoded with a light obfuscation step. Analytics are captured off the hot path via an in-process bounded `Channel<T>` + `BackgroundService` batch-writer. API keys (hashed, stored in Postgres) protect link creation and analytics reads; the redirect endpoint stays public. Ships as a multi-stage Docker image + docker-compose (api + postgres + redis), with EF Core migrations applied as an explicit deploy step rather than automatically on startup.

---

## Key Decisions & Trade-offs

### 1. Short code generation — Postgres identity + base62 + obfuscation
Use a `BIGINT GENERATED ALWAYS AS IDENTITY` on `Links`, base62-encode the ID, and run it through a fixed reversible bit-mix (XOR + rotation, or a small Feistel permutation) so codes aren't visibly sequential (`1, 2, 3…`).

- **Rejected: pre-generated key pool (Bitly-style KGS)** — solves distributed-uniqueness across multiple independent writers, which we don't have at this scale. Revisit if the DB is ever sharded or multi-primary.
- **Rejected: random string + collision retry** — non-sequential by construction but costs an extra `SELECT`-then-retry round trip on every collision, for no real benefit here.
- **Rejected: hashed URL (MD5/SHA truncated)** — truncated hashes collide meaningfully at scale, still needs a retry-with-salt loop, and complicates "same URL, two different owners."
- **Limitation**: obfuscation is not cryptographic — a determined party could reverse-engineer it and enumerate links. Not a substitute for access control if link privacy ever becomes a hard requirement. Single-primary-Postgres coordination is fine now but would need rework under sharding/multi-primary writes.

### 2. Redis caching — cache-aside, no invalidation needed, graceful degradation
`GET /{code}` checks Redis first, falls back to Postgres on miss (unique index on `ShortCode`), populates Redis on the way back.

- Because links are immutable (no aliases/expiration in scope), there is **no cache invalidation problem** — a link's cached value can never go stale. This is a direct, deliberate benefit of the scope cuts.
- TTL (~24h) + eviction policy (`allkeys-lru`) bounds Redis memory as link volume grows; TTL is a memory-management knob here, not a correctness one.
- **Cache stampede**: mitigate with jittered TTLs (desync mass-expiry) and optionally a single-flight lock per code for very hot links.
- **Redis down**: must degrade to Postgres-only, never fail the request — wrap Redis calls in try/catch, log and fall through on any exception/timeout.
- **Limitation**: this simplicity evaporates if aliases/expiration are added later — that would reintroduce real cache-invalidation design work.

### 3. Analytics write path — bounded `Channel<T>` + `BackgroundService` batch-writer
Redirect handler pushes a `ClickEvent` (LinkId, timestamp, referrer, user-agent, IP) into a bounded in-process channel and returns immediately. A single `BackgroundService` drains the channel, batching inserts (flush every ~500ms or N=100 events) into Postgres, updating a denormalized `ClickCount` in the same transaction.

- **Rejected: synchronous write on every redirect** — couples redirect latency/availability to Postgres write load; unacceptable for a service whose whole value is a fast redirect.
- **Rejected: unbounded `Task.Run` per event** — no backpressure, risks thread-pool/memory exhaustion under load. A bounded channel with an explicit drop policy (`DropOldest`/`DropWrite`) degrades analytics before it degrades the service.
- **Rejected: Redis stream/list + separate flusher** — survives an API crash (data lives in Redis, not process memory) but adds a consumer with offset/ack handling for a failure window that, at this scale, is an acceptable and clearly disclosed loss.
- **Rejected: external queue (Kafka/RabbitMQ/Service Bus)** — real operational overhead to solve a durability problem tens-of-req/s doesn't have yet. Justified later if click volume grows into hundreds/thousands req/s, analytics needs to fan out to multiple independent consumers, or the API scales to multiple instances needing a consolidated view.
- **Limitation**: data-loss window = whatever is buffered-but-unflushed at an unclean process crash — normally ≤500ms of events, up to a full channel if flush is blocked or a spike fills it. This is a conscious, disclosed trade, not an oversight. Also: analytics writer lives and dies with the API process — not independently scalable.

### 4. API auth — hashed API-key table + custom `AuthenticationHandler`
`ApiKeys(Id, KeyPrefix, HashedKey, OwnerName, OwnerEmail, CreatedAt, IsActive, LastUsedAt)`. Generate a random secret, store only its SHA-256 hash, return the raw key once at creation (GitHub PAT-style). `KeyPrefix` stored in plaintext for fast candidate lookup before hash verification. A custom `AuthenticationHandler` reads `X-Api-Key`, validates, sets a minimal `ClaimsPrincipal`. `[Authorize]`/`RequireAuthorization()` on create-link and analytics-read endpoints; `GET /{code}` stays open.

- **Rejected: ASP.NET Identity / full JWT issuer with refresh tokens** — solves session lifecycle and token-refresh UX problems this service doesn't have (no end-user login, no browser sessions). A bearer API key matches how most machine-to-machine APIs (Stripe, SendGrid) work.
- **Limitation**: no built-in key expiry/rotation (valid until manually deactivated), no scoping (a key can create links and read analytics equally), no rate limiting tied to keys yet (flagged as an easy future addition via ASP.NET Core's built-in rate-limiting middleware).

### 5. Database schema
```
Links        Id, ShortCode (unique idx), OriginalUrl, CreatedAt, CreatedByApiKeyId (FK), ClickCount
ClickEvents  Id, LinkId (FK, idx), ClickedAt, Referrer, UserAgent, IpAddress
ApiKeys      Id, KeyPrefix (idx), HashedKey, OwnerName, OwnerEmail, CreatedAt, IsActive, LastUsedAt
```
- Unique index on `Links.ShortCode` is the most important index in the system — every cache-miss redirect is a point lookup on it.
- Keep **both** a raw `ClickEvents` table (needed for referrer/time-series analytics) and a denormalized `Links.ClickCount` (cheap reads for dashboards/lists) — updated together in the same flush transaction to avoid drift.
- **Limitation**: two writes per click instead of one (amortized in the background flush, off the hot path, so acceptable).

### 6. Docker & deployment
- **Dockerfile**: multi-stage — `sdk:10.0` build/publish stage → `aspnet:10.0` runtime-only final stage (small image, no build tooling in the runtime layer).
- **docker-compose.yml**: `api` + `postgres` + `redis`.
  - `postgres`: named volume for `/var/lib/postgresql/data` — this is what satisfies "survive restarts without data loss." Healthcheck via `pg_isready`.
  - `redis`: **no volume**, intentionally ephemeral — it's a cache; Postgres is the source of truth, so a Redis restart just means a wave of cache misses. Healthcheck via `redis-cli ping`.
  - `api`: `depends_on` both with `condition: service_healthy`; all config (connection strings, etc.) via environment variables (`ConnectionStrings__Postgres`, `ConnectionStrings__Redis`) overriding `appsettings.json`, sourced from a gitignored `.env` — no secrets baked into the image or committed.
- **Production note**: managed Postgres/Redis (RDS, ElastiCache, or cloud equivalents) and a real secrets manager would replace the local containers/`.env` for an actual production deployment; the API container is already stateless (all state in Postgres/Redis), so that's an infra change, not an app redesign, when that day comes. Full k8s/cloud manifests are out of scope here per the agreed deployment scope.

### 7. Migrations — explicit deploy step, not automatic on startup
Apply migrations via an explicit command (`dotnet ef database update`, run in CI/CD or as a one-off `docker compose run --rm api ...` before the app starts) rather than calling `Database.Migrate()` in `Program.cs`.

- **Why**: `Migrate()`-on-startup is convenient for a single instance but races badly the moment two instances briefly overlap (rolling redeploy) — concurrent migration attempts can deadlock or corrupt schema state. An explicit, ordered step avoids this regardless of future replica count.
- **Limitation**: slightly more deploy ceremony — must be documented (README/CI job) so it isn't forgotten.

---

## File / Folder Structure

Per the project's `CLAUDE.md`: minimal-API endpoint style (`app.MapGet/MapPost`, consistent with the existing `/health` endpoint) rather than Controllers, and entities live under `Data/` alongside `AppDbContext`.

```
UrlShortener.Api/
  Program.cs
  Endpoints/
    LinksEndpoints.cs        # MapLinksEndpoints(): POST /links (auth)
    RedirectEndpoints.cs     # MapRedirectEndpoints(): GET /{code} (public)
    AnalyticsEndpoints.cs    # MapAnalyticsEndpoints(): GET /links/{code}/analytics (auth)
  Data/
    AppDbContext.cs          # existing — add DbSets
    Entities/
      Link.cs
      ClickEvent.cs
      ApiKey.cs
    Migrations/
  Dtos/
    CreateLinkRequest.cs
    CreateLinkResponse.cs
    LinkAnalyticsResponse.cs
  Services/
    IShortCodeGenerator.cs / Base62ShortCodeGenerator.cs
    ILinkCacheService.cs / RedisLinkCacheService.cs
    ILinkService.cs / LinkService.cs
    IApiKeyService.cs / ApiKeyService.cs
  BackgroundServices/
    ClickEventWriter.cs       # Channel<T> consumer, batch flush
  Channels/
    ClickEventChannel.cs      # DI-registered bounded Channel<ClickEvent>
  Auth/
    ApiKeyAuthenticationHandler.cs
    ApiKeyAuthenticationSchemeOptions.cs
    ApiKeyConstants.cs
  appsettings.json / appsettings.Development.json / appsettings.Production.json
Dockerfile
docker-compose.yml
.dockerignore
```

## Ordered Build Steps

1. Define `Link`, `ClickEvent`, `ApiKey` entities + `AppDbContext` DbSets and indexes (unique `ShortCode`, `ClickEvents.LinkId`, `ApiKeys.KeyPrefix`); generate the initial EF Core migration.
2. Implement `IShortCodeGenerator` (identity + base62 + obfuscation) and wire into link creation.
3. Implement `IApiKeyService` (generate/hash/verify) and `ApiKeyAuthenticationHandler`; add a one-time bootstrap path to create the first API key (no self-serve signup in scope).
4. Implement `POST /links` (auth) and `GET /{code}` (public, Postgres-only first) end to end. Use **302** for redirects (not 301) — analytics are in scope, and 301 responses get cached by browsers/CDNs, which silently stops future clicks from ever reaching the server to be counted.
5. Add Redis cache-aside around `GET /{code}`: read-through, populate-on-miss, graceful fallback when Redis is unreachable, jittered TTL.
6. Implement the `Channel<ClickEvent>` + `ClickEventWriter : BackgroundService` batch-flush pipeline; wire click capture into the redirect path without blocking the response; update `Links.ClickCount` in the same flush transaction.
7. Implement `GET /links/{code}/analytics` (auth) returning aggregated + recent raw click data.
8. Extend Serilog logging for auth failures, cache hit/miss, and flush batch sizes (debugging visibility without extra tooling).
9. Write the multi-stage `Dockerfile` and `.dockerignore`.
10. Write `docker-compose.yml` (api + postgres + redis, Postgres-only volume, healthchecks, env-var config, `.env.example`).
11. Document/script the explicit migration step (README + `docker compose run --rm api dotnet ef database update`-style command).
12. `git init`, initial commit, then smoke-test the full stack via `docker compose up`: create a link, redirect, confirm a cache hit on the second request, confirm graceful fallback with Redis stopped, check the analytics endpoint.

---

## Known Limitations / Explicitly Out of Scope

- No custom aliases — short codes are system-generated only.
- No link expiration/TTL (Redis TTL is a memory-management detail, not a link lifecycle feature).
- No multi-region or multi-instance cache coherence beyond a single Redis instance.
- No rate limiting yet (natural low-effort future addition via ASP.NET Core's rate-limiting middleware, keyed off the API key).
- Single-region deployment assumed; no cross-region failover.
- No API key rotation/expiry or scoped/read-only keys.
- Analytics have a small, bounded, disclosed data-loss window on unclean process crash.
- No admin UI — key issuance/management assumed via direct DB access or a future admin endpoint.

---

## Verification (after implementation)

- `dotnet build` / `dotnet test` (once tests exist) from the solution root.
- `docker compose up --build`, then:
  - `POST /links` with a valid API key → 201 with a short code.
  - `POST /links` without a key → 401.
  - `GET /{code}` → 302 to the original URL; repeat and confirm the second request is served from Redis (check logs/cache-hit metric).
  - `docker compose stop redis`, repeat `GET /{code}` → still 302 (Postgres fallback), no 5xx.
  - `GET /links/{code}/analytics` with a valid key → reflects the click(s) generated above after the batch-flush interval elapses.
  - `docker compose down && docker compose up` (no `-v`) → previously created links still resolve, confirming Postgres volume persistence.
