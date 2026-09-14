# Prompt Logs

Log of AI-assisted prompts used while building this project, one row per task from
[url-shortener-tasks.md](url-shortener-tasks.md).

| Task ID | Prompt (summary or link to full text above) | AI output summary | Action | Rationale |
| --- | --- | --- | --- | --- |
| T0 | Generate a .NET 10 minimal API project skeleton for a URL shortener service. Include: Program.cs with DI setup for a PostgreSQL DbContext (Npgsql) and a Redis connection multiplexer (StackExchange.Redis), appsettings.json with placeholder connection strings, Serilog console logging, and Swagger/OpenAPI enabled. No business logic yet — just the skeleton and wiring| / Edited / "Updated couple of folders to be a proper modular monolith architecture |
| T1 |Design an EF Core entity and migration for a urls table: Id (bigint identity), ShortCode (varchar(10), unique index), OriginalUrl (text), CreatedAt (timestamptz), ExpiresAt (timestamptz, nullable), ClickCount (bigint, default 0). Generate the entity class, DbContext configuration, and the EF Core migration." | | Accepted / | |
| T2 | Implement a Base62 encoder for generating short codes from a bigint auto-increment ID in C#. Include a service method that encodes the ID after insert, and a collision-retry wrapper (max 3 attempts) in case a manually-specified code collides. Include XML doc comments."| / Edited / Used database id based base 62 encoder with no collison |
| T3 | Implement a POST /shorten minimal API endpoint in .NET 10. Request: { originalUrl, expiresAt? }. Validate originalUrl is a well-formed absolute URL. Insert into Postgres via EF Core, generate the short code via [paste T3's service], return { shortCode, shortUrl, expiresAt }. Return 400 on invalid URL| | Accepted /  |
| T4 | | | Accepted / Edited / Rejected | |
| T5 | Implement GET /{code} in .NET 10 minimal APIs using cache-aside with Redis (StackExchange.Redis): check Redis first for the original URL, on miss query Postgres and populate Redis with a TTL of 1 hour, then issue an HTTP 302 redirect. Increment ClickCount in Postgres asynchronously (don't block the redirect on it). Return 404 if the code doesn't exist or is expired.| | Accepted | |
| T6 | Implement GET /{code}/stats in .NET 10 returning { shortCode, originalUrl, clickCount, createdAt, expiresAt }. Query Postgres directly (not cache) for accuracy. 404 if not found.| | Accepted /
| T7 | Generate xUnit tests for the URL shortener: unit tests for the Base62 encoder (including collision retry), integration tests for POST /shorten (valid and invalid URL) and GET /{code} (hit, miss, expired), using an in-memory or test-container Postgres.| | Accepted / Edited / Rejected | |
| T8 | Write a multi-stage Dockerfile for a .NET 10 minimal API project: SDK image to restore/build/publish, ASP.NET runtime image to run the published output. Expose port 8080, run as a non-root user| | Accepted / Edited / Rejected | |
| T9 | Write a docker-compose.yml with three services: api (built from the local Dockerfile, depends on postgres and redis, reads connection strings from environment variables), postgres (postgres:16, with a named volume and a healthcheck), and redis (redis:7, with a healthcheck). Wire the api service to wait for both healthchecks before starting| | Accepted / Edited / Rejected | |
| T10 | | | Accepted / | |
| T11 | implement serilogger for observebility efficiency| / Edited / Rejected | |
| T12 | | | Accepted / Edited / Rejected | |
| T13 | Write a GitHub Actions workflow that on push/PR: checks out the repo, sets up .NET 10, runs dotnet build, runs dotnet test, then runs docker build on the Dockerfile to confirm the image builds cleanly. No deployment or registry push — build/test verification only| | Accepted / Edited / Rejected | |



Brown field scenario:
Prompt: Add IP-based rate limiting (10 requests/minute) to POST /shorten using Redis as the counter store (sliding window or fixed window, your choice — state which). Return 429 with a Retry-After header when exceeded. This must not affect the GET /{code} redirect path



Ambigious Scenario:
Prompt: make it more scalable


 Given that scope, propose and implement the smallest change that meaningfully improves redirect throughput without changing the API contract — e.g., tuning the Redis cache TTL/strategy or adding a read-through cache warmup on write. Explain the trade-off of your choice