# UrlShortener

ASP.NET Core Web API for shortening URLs.

## Stack
- .NET 10 (`net10.0`), nullable + implicit usings enabled
- EF Core with Npgsql (PostgreSQL) — `UrlShortener.Api/Common/Data/AppDbContext.cs`
- StackExchange.Redis for caching
- Serilog for logging (console sink, request logging enabled)
- Swashbuckle for Swagger/OpenAPI (Development only)

## Structure
Modular monolith — one deployable (`UrlShortener.Api/UrlShortener.Api.csproj`, referenced from
`UrlShortener.slnx`), code organized by business capability rather than technical layer. See
[docs/project-structure.md](docs/project-structure.md) for the full layout and module-boundary
rules, and [docs/url-shortener-plan.md](docs/url-shortener-plan.md) /
[docs/url-shortener-tasks.md](docs/url-shortener-tasks.md) for the architecture rationale and
build order (most module internals are currently `NotImplementedException` skeletons).

- `Program.cs` — thin composition root; calls each module's `Add{Module}Module()` /
  `Map{Module}Module()` extension methods, plus the standalone `/health` endpoint
- `Common/Data/AppDbContext.cs` — shared EF Core context: `DbSet<T>` properties +
  `ApplyConfigurationsFromAssembly` only; no entity-specific mapping lives here
- `Modules/Links/`, `Modules/Analytics/`, `Modules/Auth/` — each owns its own `Entities/`,
  `Data/` (`IEntityTypeConfiguration<T>`), `Services/`, `Endpoints/`, `Dtos/`, and a top-level
  `{Module}Module.cs`. Cross-module calls go only through an `Abstractions/` interface
  (`Links.Abstractions.ILinkClickCounterUpdater`, `Analytics.Abstractions.IClickEventRecorder`);
  entities reference other modules by scalar id, never by navigation property
- Connection strings (`Postgres`, `Redis`) come from configuration (`appsettings*.json`)

## Conventions
- Prefer minimal API endpoint style (`app.MapGet/MapPost/...`) consistent with existing `/health` endpoint, unless the project moves to controllers later
- Keep entities and their EF Core configuration inside the owning module's `Data/`/`Entities/` folders — not in `Common/`
- New cross-module behavior gets an interface in the owning module's `Abstractions/` folder; don't reach into another module's `Services/`/`Entities/` directly
- `UrlShortener.Api.Tests/` exists (xUnit + Testcontainers for Postgres/Redis) — see its `Fixtures/` folder before assuming test setup

## Commands
- Build: `dotnet build`
- Run: `dotnet run --project UrlShortener.Api`
