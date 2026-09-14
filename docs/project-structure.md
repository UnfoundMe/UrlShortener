# Project Structure

Modular-monolith layout: one deployable (`UrlShortener.Api`), but code is organized by business
capability (`Modules/Links`, `Modules/Analytics`, `Modules/Auth`) rather than by technical layer.
Each module owns its endpoints, services, entities, and EF Core entity configuration end to end;
`Common/` holds the one piece every module shares — the `AppDbContext`. This is deliberate prep
for scalability: if a module ever needs to be pulled out into its own service, the module-owned
files move with it largely unchanged, and the few cross-module contracts (below) become the seam.

See [url-shortener-plan.md](url-shortener-plan.md) for the architecture rationale and
[url-shortener-tasks.md](url-shortener-tasks.md) for the task-by-task build order — most files
below are currently skeletons (interfaces + stub classes that throw `NotImplementedException`,
tagged with `// TODO (Task N...)`) so the solution builds and runs before the logic lands.

```
UrlShortener/
├── UrlShortener.slnx
├── CLAUDE.md
├── README.md
├── .gitignore
├── .dockerignore
├── .env.example / .env                    # not yet created — Task 11
├── Dockerfile / docker-compose.yml        # not yet created — Task 10/11
│
├── docs/
│   ├── url-shortener-plan.md
│   ├── url-shortener-tasks.md
│   └── project-structure.md               # this file
│
├── UrlShortener.Api/
│   ├── UrlShortener.Api.csproj
│   ├── Program.cs                         # composition root only: calls each module's Add*Module()/Map*Module()
│   ├── appsettings.json / appsettings.Development.json
│   ├── Properties/launchSettings.json
│   │
│   ├── Common/
│   │   └── Data/
│   │       └── AppDbContext.cs            # DbSets + ApplyConfigurationsFromAssembly — the one shared piece
│   │
│   └── Modules/
│       ├── Links/                         # core: create + redirect
│       │   ├── LinksModule.cs             # AddLinksModule(IServiceCollection), MapLinksModule(IEndpointRouteBuilder)
│       │   ├── Entities/Link.cs           # cross-module refs are scalar ids only (CreatedByApiKeyId), no nav properties
│       │   ├── Data/LinkEntityConfiguration.cs
│       │   ├── Dtos/CreateLinkRequest.cs, CreateLinkResponse.cs
│       │   ├── Endpoints/LinksEndpoints.cs        # POST /links (auth)
│       │   ├── Endpoints/RedirectEndpoints.cs     # GET /{code} (public)
│       │   ├── Services/IShortCodeGenerator.cs, Base62ShortCodeGenerator.cs
│       │   ├── Services/ILinkCacheService.cs, RedisLinkCacheService.cs
│       │   ├── Services/ILinkService.cs, LinkService.cs
│       │   ├── Services/LinkClickCounterUpdater.cs
│       │   └── Abstractions/ILinkClickCounterUpdater.cs   # <- public seam consumed by Analytics
│       │
│       ├── Analytics/                     # click tracking, decoupled from the redirect hot path
│       │   ├── AnalyticsModule.cs
│       │   ├── Entities/ClickEvent.cs     # LinkId is a scalar id only, no nav to Links.Entities.Link
│       │   ├── Data/ClickEventEntityConfiguration.cs
│       │   ├── Dtos/LinkAnalyticsResponse.cs
│       │   ├── Endpoints/AnalyticsEndpoints.cs    # GET /links/{code}/analytics (auth)
│       │   ├── Channels/ClickEventChannel.cs      # bounded Channel<ClickEvent>, DI singleton
│       │   ├── BackgroundServices/ClickEventWriter.cs   # batches channel -> Postgres, calls Links' ILinkClickCounterUpdater
│       │   ├── Services/ClickEventRecorder.cs
│       │   └── Abstractions/IClickEventRecorder.cs        # <- public seam consumed by Links' redirect endpoint
│       │
│       └── Auth/                          # API-key issuance + authentication
│           ├── AuthModule.cs              # registers the "ApiKey" auth scheme; this is the only seam
│           ├── Entities/ApiKey.cs         # other modules never touch it
│           ├── Data/ApiKeyEntityConfiguration.cs
│           ├── Constants/ApiKeyConstants.cs
│           ├── Handlers/ApiKeyAuthenticationHandler.cs, ApiKeyAuthenticationSchemeOptions.cs
│           └── Services/IApiKeyService.cs, ApiKeyService.cs
│
└── UrlShortener.Api.Tests/                # already scaffolded (Task 0)
    ├── UrlShortener.Api.Tests.csproj      # xUnit, Testcontainers.PostgreSql, Testcontainers.Redis
    └── Fixtures/
        ├── PostgresFixture.cs, PostgresCollection.cs, PostgresFixtureTests.cs
        └── RedisFixture.cs, RedisCollection.cs
```

## Module boundary rules

These are conventions enforced by discipline, not the compiler — all modules live in one project/
assembly today, which keeps the monolith simple to build and deploy. If a module is ever split out,
these are exactly the rules that make the split mechanical rather than a rewrite:

1. **Entities never hold navigation properties across module boundaries.** `Link.CreatedByApiKeyId`
   and `ClickEvent.LinkId` are plain `long`s, not `ApiKey`/`Link` references — the same shape a
   foreign-service id would have.
2. **A module exposes cross-module behavior only through an `Abstractions/` interface.** Analytics
   depends on `Links.Abstractions.ILinkClickCounterUpdater` (to bump `Link.ClickCount`); Links
   depends on `Analytics.Abstractions.IClickEventRecorder` (to enqueue a click, non-blocking). Auth
   exposes no interface at all — every other module talks to it only via ASP.NET Core's standard
   `RequireAuthorization()`, with zero compile-time reference to `Modules.Auth`.
3. **Each module owns its own EF Core entity + `IEntityTypeConfiguration<T>`.** `Common/Data/AppDbContext`
   only declares `DbSet<T>` properties and calls `ApplyConfigurationsFromAssembly` — it has no
   knowledge of indexes, constraints, or column mappings, those stay in the owning module.
4. **A module's DI wiring and endpoint mapping are one file each** (`{Module}Module.cs`), called
   once from `Program.cs`. `Program.cs` stays a thin composition root and never reaches into a
   module's internal namespaces.

## Notes

- `appsettings.Production.json`, `Dockerfile`, `docker-compose.yml`, `.env*` are still pending
  (Tasks 10–12 in the task breakdown) — not yet created.
- `CLAUDE.md`'s "Structure" section has been updated to point at this modular layout instead of
  the earlier flat `Data/`/`Endpoints/`/`Services/` sketch.
