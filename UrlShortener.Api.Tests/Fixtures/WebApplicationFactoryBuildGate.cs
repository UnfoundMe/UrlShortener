namespace UrlShortener.Api.Tests.Fixtures;

// Program.cs bootstraps a process-wide static Serilog logger (Log.Logger = ...CreateBootstrapLogger())
// every time a WebApplicationFactory<Program> builds its host. xUnit runs different test
// collections in parallel (and doesn't guarantee collection fixtures within one collection
// initialize sequentially either), so two fixtures building a WebApplicationFactory<Program> at
// the same time race on that static field — the loser's UseSerilog freeze throws
// "The logger is already frozen." Any fixture that builds one should wrap the build (and the
// first access to Factory.Services, which triggers it) in RunAsync so only one such build is
// ever in flight process-wide, regardless of xUnit's scheduling.
internal static class WebApplicationFactoryBuildGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<T> RunAsync<T>(Func<Task<T>> buildAsync)
    {
        await Gate.WaitAsync();
        try
        {
            return await buildAsync();
        }
        finally
        {
            Gate.Release();
        }
    }
}
