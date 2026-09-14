using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Serilog;
using StackExchange.Redis;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Analytics;
using UrlShortener.Api.Modules.Auth;
using UrlShortener.Api.Modules.Auth.Constants;
using UrlShortener.Api.Modules.Auth.Services;
using UrlShortener.Api.Modules.Links;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog(
    (context, services, loggerConfiguration) => loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .WriteTo.Console(),
    // Task 9 (docs/url-shortener-tasks.md): also forward events to any ILoggerProvider
    // registered in DI (e.g. a test-only capturing provider) instead of Serilog being the
    // sole consumer of ILogger<T> calls.
    writeToProviders: true);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition(ApiKeyConstants.SchemeName, new OpenApiSecurityScheme
    {
        Name = ApiKeyConstants.HeaderName,
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "API key required for protected endpoints.",
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = ApiKeyConstants.SchemeName,
                },
            },
            Array.Empty<string>()
        },
    });
});

// Each module owns its own DI registrations; Program.cs only composes them.
builder.Services.AddAuthModule();
builder.Services.AddLinksModule();
builder.Services.AddAnalyticsModule();

var app = builder.Build();

// Task 3 (docs/url-shortener-tasks.md): one-off bootstrap to mint the first API key — there's
// no self-serve signup in scope. Runs the CLI action and exits without starting the web host.
// Usage: dotnet run -- seed-api-key --owner "Jane Doe" [--email jane@example.com]
if (args.Length > 0 && string.Equals(args[0], "seed-api-key", StringComparison.OrdinalIgnoreCase))
{
    var ownerName = GetArgValue(args, "--owner");
    if (string.IsNullOrWhiteSpace(ownerName))
    {
        Console.Error.WriteLine("seed-api-key requires --owner \"<name>\".");
        return 1;
    }

    var ownerEmail = GetArgValue(args, "--email");

    using var scope = app.Services.CreateScope();
    var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
    var (rawKey, entity) = await apiKeyService.GenerateAsync(ownerName, ownerEmail);

    Console.WriteLine($"API key created for '{entity.OwnerName}' (id {entity.Id}):");
    Console.WriteLine(rawKey);
    Console.WriteLine("Store this key now — it will not be shown again.");

    return 0;
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapAuthModule(builder.Configuration);
app.MapLinksModule();
app.MapAnalyticsModule();

app.Run();

return 0;

static string? GetArgValue(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

// Exposed for WebApplicationFactory<Program> in the future test project (Task 0).
public partial class Program;
