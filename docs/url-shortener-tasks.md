# URL Shortener — Task Breakdown

Companion to [url-shortener-plan.md](url-shortener-plan.md). Each task is scoped to be independently implementable, independently testable, and to leave the service in a working, integrated state when merged — i.e. `main` should build and run after every task, not just after the last one.

**Test project**: none exists yet (per `CLAUDE.md`). Task 0 creates `UrlShortener.Api.Tests` (xUnit) using:
- **Testcontainers.PostgreSql** + **Testcontainers.Redis** for real integration tests against ephemeral containers (chosen over mocks — this service's entire value is the interaction between Postgres/Redis/EF/StackExchange.Redis, so mocking them away would test very little; the trade-off is tests need Docker available and run slower than pure unit tests, which is acceptable at this project's size).
- `WebApplicationFactory<Program>` for endpoint-level integration tests, with the container connection strings injected via `WithWebHostBuilder`.

---

### Task 0 — Test project scaffolding
**Depends on**: nothing.
**Do**: Add `UrlShortener.Api.Tests` (xUnit), reference `UrlShortener.Api`, add `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql`, `Testcontainers.Redis`. Add a `PostgresFixture`/`RedisFixture` (`IAsyncLifetime`) that starts/stops containers per test collection. Add to `UrlShortener.slnx`.
**Test**: a trivial fixture test — start the Postgres container, open a connection, confirm it responds.
**Integration checkpoint**: `dotnet test` runs (one passing test), `dotnet build` on the solution still succeeds.

---

### Task 1 — Domain entities & persistence foundation ✅ Done
**Depends on**: Task 0.
**Do**: `Data/Entities/Link.cs`, `ClickEvent.cs`, `ApiKey.cs`. Wire `DbSet<T>` + Fluent API in `AppDbContext` (unique index on `Link.ShortCode`, index on `ClickEvent.LinkId`, index on `ApiKey.KeyPrefix`, FKs). Generate the initial EF Core migration.
**Test**: integration test against the Postgres testcontainer — apply the migration, assert the expected tables/indexes exist (query `information_schema`), assert inserting a duplicate `ShortCode` throws a unique-constraint violation.
**Integration checkpoint**: `dotnet ef database update` against a local/dockerized Postgres succeeds; no endpoints depend on this yet, but nothing else can proceed without it.

**Status**: entities (`Modules/{Links,Analytics,Auth}/Entities/`), `IEntityTypeConfiguration<T>` per module, and `Common/Data/AppDbContext` were already in place from the structural skeleton. Added in this pass: the `InitialCreate` migration (`Common/Data/Migrations/`) and `UrlShortener.Api.Tests/Data/AppDbContextMigrationTests.cs`, which — against a real Postgres testcontainer — verifies the migration creates all three tables plus `IX_Links_ShortCode` (unique), `IX_ClickEvents_LinkId`, `IX_ApiKeys_KeyPrefix`; that re-running the migration is a no-op; and that inserting a duplicate `ShortCode` throws a `PostgresException` with SQL state `23505` (unique_violation). All 4 tests in the suite pass (`dotnet test`).

---

### Task 2 — Short code generator ✅ Done
**Depends on**: Task 1 (uses `Link.Id` shape, but logic itself is pure).
**Do**: `Services/IShortCodeGenerator.cs` / `Base62ShortCodeGenerator.cs` — base62-encode a `long`, with a fixed reversible bit-mix (XOR + rotation) applied first so output isn't visibly sequential.
**Test**: pure unit tests — round-trip encode/decode for a range of IDs including 0, 1, `long.MaxValue`; assert no two of the first 10,000 sequential IDs produce the same code (collision sanity check); assert output contains only `[A-Za-z0-9]`.
**Integration checkpoint**: none required yet — not wired to anything external, safe to merge standalone.

**Status**: `Base62ShortCodeGenerator.Encode` runs the id through a fixed reversible 64-bit mix
(`XOR` with an odd constant, then `BitOperations.RotateLeft` by 17) before base62-encoding it with
`0-9A-Za-z`. A `Decode` method (not part of `IShortCodeGenerator` — nothing else needs to decode a
code, since resolution is a DB lookup by `ShortCode`) exists on the concrete class purely so the
mix's reversibility is directly testable. `UrlShortener.Api.Tests/Modules/Links/Services/Base62ShortCodeGeneratorTests.cs`
covers round-trip encode/decode (0, 1, an arbitrary id, `long.MaxValue`), no collisions across the
first 10,000 sequential ids, and that output only ever contains `[A-Za-z0-9]`. All 11 tests pass
(`dotnet test --filter FullyQualifiedName~Base62ShortCodeGeneratorTests`); `dotnet build` succeeds
for the whole solution.

---

### Task 3 — API key issuance & authentication ✅ Done
**Depends on**: Task 1.
**Do**: `Data/Entities/ApiKey` already exists (Task 1). Add `Services/IApiKeyService.cs`/`ApiKeyService.cs` (generate random secret, SHA-256 hash, verify by prefix+hash lookup). Add `Auth/ApiKeyAuthenticationHandler.cs` + scheme options reading `X-Api-Key`. Register the scheme in `Program.cs`. Add a small bootstrap mechanism to mint the first key (a `dotnet run -- seed-api-key --owner "..."` CLI switch handled at the top of `Program.cs`, or a one-off script — no self-serve signup in scope).
**Test**: unit tests for `ApiKeyService` (hash/verify correctness, wrong-key rejection). Integration test with `WebApplicationFactory`: add one throwaway `[Authorize]`-protected test-only endpoint (or wait and cover this via Task 4's real endpoint if you want to avoid a throwaway route) — valid key → 200/passes auth; missing header → 401; invalid/inactive key → 401.
**Integration checkpoint**: `Program.cs` boots with the auth scheme registered without breaking `/health`.

**Status**: `ApiKeyService` (`GenerateAsync`/`ValidateAsync`, prefix+SHA-256-hash lookup,
fixed-time hash comparison), `ApiKeyAuthenticationHandler`/`ApiKeyAuthenticationSchemeOptions`
(reads `X-Api-Key`, never throws — auth failures map to `NoResult()`/`Fail()` so ASP.NET Core
turns them into a 401), `AuthModule` (registers the `ApiKey` scheme), and the
`dotnet run -- seed-api-key --owner "..."` bootstrap in `Program.cs` were already in place.
`ApiKeyServiceTests` covers hash/verify correctness, wrong-secret and unknown-key rejection, and
deactivated-key rejection against a real Postgres testcontainer. The `WebApplicationFactory`
auth-handler checks (valid key passes, missing/invalid key → 401) were deferred to Task 4's real
endpoint per the note above, and now live in `LinksEndpointsTests`.

---

### Task 4 — Create-link endpoint (`POST /links`) ✅ Done
**Depends on**: Task 2, Task 3.
**Do**: `Dtos/CreateLinkRequest.cs`/`CreateLinkResponse.cs`, `Services/ILinkService.cs`/`LinkService.cs` (validates URL, persists `Link` with `CreatedByApiKeyId` from the authenticated principal, uses `IShortCodeGenerator`), `Endpoints/LinksEndpoints.cs` mapping `POST /links` with `RequireAuthorization()`.
**Test**: integration test (Postgres testcontainer + `WebApplicationFactory`) — valid key + valid URL → 201 with a short code that round-trips through the generator; missing/invalid key → 401; malformed URL → 400.
**Integration checkpoint**: this is the first fully real, DB-backed, authenticated endpoint — confirms entities, auth, and generator all work together.

**Status**: `LinkService.CreateAsync` validates `originalUrl` is an absolute `http`/`https` URI
(throws `ArgumentException` otherwise), then persists the `Link` in two steps — insert with a
throwaway placeholder `ShortCode` to get the identity-generated `Link.Id`, then update
`ShortCode` to `IShortCodeGenerator.Encode(link.Id)` — since the real code is derived from an id
that doesn't exist until after the first insert. `LinksEndpoints` maps `POST /links` behind
`RequireAuthorization()`, reads the authenticated API key id off `ClaimsPrincipal` (the
`ApiKeyAuthenticationHandler`'s `ClaimTypes.NameIdentifier` claim), and returns `201` with a
`Location` header + `CreateLinkResponse`, or `400` on the service's `ArgumentException`.
Added `LinksApiFixture`/`LinksApiCollection` — a `WebApplicationFactory<Program>` wired to real
Postgres + Redis testcontainers (via `WithWebHostBuilder`/`UseSetting`, migrated on init) — for
true end-to-end HTTP tests. `LinksEndpointsTests` covers valid key + valid URL → 201 with a
short code that round-trips through `Base62ShortCodeGenerator.Decode`; missing key → 401;
invalid key → 401; malformed URL → 400. `LinkServiceTests` covers the service directly against
a Postgres testcontainer (persistence, malformed/non-http URL rejection via `[Theory]`, distinct
codes across calls) using a cache double that throws if `CreateAsync` ever touches it, confirming
Task 5/6's cache path isn't accidentally invoked yet. All 30 tests pass (`dotnet test`);
`dotnet build` succeeds for the whole solution.

---

### Task 5 — Redirect endpoint (`GET /{code}`), Postgres-only ✅ Done
**Depends on**: Task 4 (needs a way to create links to redirect to).
**Do**: `Endpoints/RedirectEndpoints.cs` mapping `GET /{code}` (no auth), `LinkService.ResolveAsync` (lookup by `ShortCode`), return `302` with `Location` header; `404` for unknown codes.
**Test**: integration test — create a link via `POST /links`, then `GET /{code}` → 302 to the original URL; `GET /unknown-code` → 404.
**Integration checkpoint**: full create→redirect round trip works end-to-end over real Postgres. This is the walking skeleton of the whole product.

**Status**: `LinkService.ResolveOriginalUrlAsync` queries `AppDbContext.Links` directly by
`ShortCode` (`AsNoTracking`, projected to just `OriginalUrl`) — no cache involved yet, that's
Task 6. `RedirectEndpoints` maps `GET /{code}` with no `RequireAuthorization()` (public), returns
`Results.Redirect(originalUrl, permanent: false)` (302) on a hit or `Results.NotFound()` (404) on
a miss. `LinkServiceTests` gained `ResolveOriginalUrlAsync` cases (existing code → its URL,
unknown code → `null`) using the same cache double that fails the test if the cache is ever
touched, confirming Task 6's cache-aside path isn't wired in yet. `RedirectEndpointsTests` (new)
drives the full create→redirect round trip over real HTTP via `WebApplicationFactory<Program>`
(using a non-auto-redirecting `HttpClient` so the 302/`Location` header can be asserted directly
instead of the client following it out to `example.com`): create via `POST /links` → `GET /{code}`
→ 302 to the original URL; unknown code → 404; and confirms the redirect endpoint needs no API
key. All 35 tests pass (`dotnet test`); `dotnet build` succeeds for the whole solution.

---

### Task 6 — Redis cache-aside for redirects ✅ Done
**Depends on**: Task 5.
**Do**: `Services/ILinkCacheService.cs`/`RedisLinkCacheService.cs` (get/set with TTL + random jitter, using the existing `IConnectionMultiplexer`). Wire into `LinkService.ResolveAsync`: check cache → hit returns immediately; miss queries Postgres then populates cache. Wrap all Redis calls in try/catch that logs and falls through to Postgres.
**Test**: integration test with the Redis testcontainer — create + redirect, then inspect Redis directly (`IDatabase.StringGet`) to confirm the key was populated; a second redirect should still return 302 with Redis stopped/unreachable (simulate by disposing the Redis container or pointing at a dead endpoint) — no 5xx.
**Integration checkpoint**: redirect endpoint now depends on Postgres *and* Redis together and degrades gracefully when Redis isn't there — matches the plan's graceful-degradation requirement.

**Status**: `RedisLinkCacheService.GetOriginalUrlAsync`/`SetOriginalUrlAsync` use `IConnectionMultiplexer.GetDatabase()` with a `link:{shortCode}` key; `Set` applies a 24h base TTL plus up to 10 minutes of random jitter (`Random.Shared`) so co-created keys don't expire in a stampede. Every Redis call is wrapped in try/catch — a caught exception logs a warning (`ILogger<RedisLinkCacheService>`) and returns `null`/no-ops, so a cache miss and a Redis outage are indistinguishable to callers. `LinkService.ResolveOriginalUrlAsync` now checks the cache first, returns immediately on a hit, and on a miss queries Postgres and (only when a row was found) populates the cache before returning. Added `RedisLinkCacheServiceTests` (Redis testcontainer): set-then-get round trip, miss returns null, `Set` applies a TTL, and a "dead endpoint" `ConfigurationOptions` (`AbortOnConnectFail: false`, short `ConnectTimeout`/`ConnectRetry`) proves both methods degrade gracefully instead of throwing. `LinkServiceTests` gained a `FakeLinkCacheService` in-memory double (replacing the old "cache must never be touched" double for the resolve tests, since Task 6 now wires the cache in) covering cache-miss-populates-cache and cache-hit-skips-Postgres; `CreateAsync` still uses the never-called double since create must not touch the cache. `RedirectEndpointsTests` gained an HTTP-level test that creates + redirects then inspects Redis directly (a fresh `IConnectionMultiplexer`, independent of the app's own) to confirm the key was populated. `RedirectEndpointsRedisUnavailableTests` (new, its own `WebApplicationFactory` over the shared `PostgresFixture` rather than mutating `LinksApiFixture`'s shared Redis container) points `ConnectionStrings:Redis` at a dead endpoint end-to-end and confirms create→redirect still returns 302 with no 5xx. All 43 tests pass (`dotnet test`); `dotnet build` succeeds for the whole solution.

---

### Task 7 — Click analytics capture pipeline ✅ Done
**Depends on**: Task 6 (hooks into the redirect path).
**Do**: `Channels/ClickEventChannel.cs` (DI singleton wrapping a bounded `Channel<ClickEvent>`), `BackgroundServices/ClickEventWriter.cs` (`BackgroundService` draining the channel, batching inserts every ~500ms or N=100, updating `Link.ClickCount` in the same transaction as the batch insert). Redirect endpoint writes a `ClickEvent` (referrer/user-agent/IP from the request) into the channel without awaiting the write.
**Test**: integration test — issue several redirects, then either (a) expose an internal test hook to force-flush the writer, or (b) poll with a short timeout for `ClickEvents` rows and updated `ClickCount` to appear. Also assert the redirect response itself returns before any DB write for the click occurs (e.g., by injecting a slow/blocked flush and timing the redirect response).
**Integration checkpoint**: clicks are now durably recorded without adding latency to the hot path — verifies the channel/background-service wiring end-to-end against real Postgres.

**Status**: Resolving a short code now needs to yield the `Link.Id` (not just its URL) so the
redirect endpoint can record a click against the right link, whether that resolution came from
Postgres or the cache — so `ILinkService.ResolveOriginalUrlAsync(...) : Task<string?>` became
`ResolveAsync(...) : Task<LinkResolution?>` (`Dtos/LinkResolution.cs`, a `(LinkId, OriginalUrl)`
record), and `ILinkCacheService`/`RedisLinkCacheService` cache the whole `LinkResolution`
(JSON-serialized) under the same `link:{shortCode}` key rather than just the URL string.
`LinkClickCounterUpdater.IncrementClickCountAsync` issues a single `ExecuteUpdateAsync` (`SET
"ClickCount" = "ClickCount" + @p`, no entity load) that participates in the caller's ambient
transaction. `ClickEventWriter` drains `ClickEventChannel` into batches of up to 100 events or
every 500ms (whichever comes first, via a `CancellationTokenSource.CancelAfter`-timed
`WaitToReadAsync` loop), flushing each batch as one transaction — insert the `ClickEvent` rows,
then one `IncrementClickCountAsync` per distinct `LinkId` in the batch — and logs + drops a
batch on flush failure rather than letting an unhandled exception crash the whole
`BackgroundService` (and, per .NET's default `BackgroundServiceExceptionBehavior`, the host).
`RedirectEndpoints` now injects `Analytics.Abstractions.IClickEventRecorder` and calls
`Record(...)` with the resolved `LinkId` plus the `Referer`/`User-Agent` headers and
`HttpContext.Connection.RemoteIpAddress` after a successful resolve — a synchronous, non-awaited
bounded-channel write, so the redirect response never depends on analytics.
Tests: pure-unit `ClickEventChannelTests` (bounded + `DropOldest` behavior) and
`ClickEventRecorderTests` (field mapping, no infra); integration `LinkClickCounterUpdaterTests`
and `ClickEventWriterTests` (the writer run as a real hosted `BackgroundService` —
`StartAsync`/`StopAsync`, not its internals called directly — against a real Postgres
testcontainer, covering the time-based flush, the size-based flush across multiple batches, and
per-link `ClickCount` aggregation); e2e `RedirectEndpointsClickAnalyticsTests` (via
`LinksApiFixture`) polls for `ClickEvents` rows and `ClickCount` to reflect several redirects
with distinct referrer/user-agent headers, and confirms a 404 records nothing; and
`RedirectEndpointsClickWriteLatencyTests` proves the response/flush decoupling structurally
(not just "it happens to be fast") by DI-substituting a `ILinkClickCounterUpdater` that stalls
the flush for 3 seconds and asserting the redirect still returns in under 1 second, with the
click landing later once the stall clears. That test — and Task 6's
`RedirectEndpointsRedisUnavailableTests`, similarly updated — had to move into
`LinksApiCollection` (rather than a standalone/`PostgresCollection` collection) alongside every
other test class that boots a `WebApplicationFactory<Program>`: `Program.cs` assigns a static,
process-global Serilog `Log.Logger` on every boot, and two factories starting concurrently (across
xUnit's default parallel-collections execution) intermittently threw "the logger is already
frozen" — one shared collection serializes all such boots against each other while leaving
non-`WebApplicationFactory` integration tests free to run in parallel. All 57 tests pass
(`dotnet test`, verified stable across repeated runs); `dotnet build` succeeds for the whole
solution.

---

### Task 8 — Analytics read endpoint (`GET /links/{code}/analytics`)
**Depends on**: Task 7.
**Do**: `Dtos/LinkAnalyticsResponse.cs`, `Endpoints/AnalyticsEndpoints.cs` mapping `GET /links/{code}/analytics` with `RequireAuthorization()` — returns `ClickCount`, recent `ClickEvents` (paged), and any breakdowns (e.g., top referrers) the DTO exposes.
**Test**: integration test — create a link, drive N redirects (with distinct referrers/user-agents), flush, then call the analytics endpoint with a valid key and assert the response reflects the generated clicks; call without a key → 401; call for an unknown code → 404.
**Integration checkpoint**: full loop closed — create, click, and inspect analytics, all through real HTTP calls against real Postgres/Redis.

---

### Task 9 — Observability polish
**Depends on**: Tasks 3, 6, 7 (the things being logged).
**Do**: Serilog enrichment/log events for auth failures (with reason, not the raw key), cache hit/miss on redirect, and batch size/duration on each analytics flush.
**Test**: lightweight integration test asserting expected log events fire (Serilog `InMemorySink` or similar), or manual verification via `docker compose logs` — call out in the PR which approach was used.
**Integration checkpoint**: no behavior change; safe, low-risk task that can land any time after its dependencies.

---

### Task 10 — Dockerize the API
**Depends on**: Tasks 4–8 (needs the app fully functional to be worth containerizing, though technically buildable earlier).
**Do**: Multi-stage `Dockerfile` (`sdk:10.0` build/publish → `aspnet:10.0` runtime), `.dockerignore`.
**Test**: `docker build -t urlshortener-api .` succeeds; `docker run` the image with env vars pointing at a reachable Postgres/Redis and confirm `GET /health` responds `200`.
**Integration checkpoint**: the app now runs identically in-container as it does via `dotnet run`.

---

### Task 11 — docker-compose stack
**Depends on**: Task 10.
**Do**: `docker-compose.yml` (api + postgres + redis), named volume on Postgres only, healthchecks (`pg_isready`, `redis-cli ping`), `api` config via env vars (`ConnectionStrings__Postgres`, `ConnectionStrings__Redis`) sourced from a gitignored `.env`, plus a committed `.env.example`.
**Test**: `docker compose up --build`, wait for healthy, run the Task 4/5/6/8 test scenarios by hand (or a small shell/`curl` smoke script) against `http://localhost:<port>`.
**Integration checkpoint**: the entire stack is reproducible with one command.

---

### Task 12 — Explicit migration deploy step
**Depends on**: Task 11.
**Do**: Remove any temptation to call `Database.Migrate()` on startup; document/script the explicit step, e.g. `docker compose run --rm api dotnet ef database update`, in the README.
**Test**: fresh `docker compose up` against an empty Postgres volume — API should **not** auto-create schema; running the documented migration command should create it; running it a second time should be a no-op (idempotent).
**Integration checkpoint**: confirms the deploy story is race-safe for future multi-replica deploys.

---

### Task 13 — Repo hygiene & full end-to-end smoke test
**Depends on**: all prior tasks.
**Do**: `git init` (currently not a repo), initial commit, confirm `.gitignore`/`.dockerignore` exclude `.env`/`bin`/`obj`.
**Test**: run the full scenario from the plan's Verification section against the compose stack: create → redirect (302) → repeat (cache hit) → stop Redis → redirect still works (Postgres fallback) → analytics reflects clicks → `docker compose down && up` (no `-v`) → links persist.
**Integration checkpoint**: this is the release gate — if this passes, the service matches the plan end-to-end.

---

## Suggested execution order

```
0 → 1 → 2 ─┐
      3 ───┼→ 4 → 5 → 6 → 7 → 8 → 9
           ┘                        \
                                      → 10 → 11 → 12 → 13
```
Tasks 2 and 3 can be done in parallel once Task 1 lands; Task 9 can slot in any time after its dependencies; everything from Task 10 onward is strictly sequential since each wraps the previous.
