using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Common.Data;
using UrlShortener.Api.Modules.Analytics.Channels;
using UrlShortener.Api.Modules.Analytics.Entities;
using UrlShortener.Api.Modules.Links.Abstractions;

namespace UrlShortener.Api.Modules.Analytics.BackgroundServices;

// Drains ClickEventChannel in batches of up to BatchSize, flushed at least every FlushInterval,
// so a quiet period doesn't leave events sitting in the channel indefinitely. Each batch is a
// single Postgres transaction: insert the ClickEvent rows, then bump Link.ClickCount per link
// via Links.Abstractions.ILinkClickCounterUpdater — both commit together or not at all.
public class ClickEventWriter(
    ClickEventChannel channel,
    IServiceScopeFactory scopeFactory,
    ILogger<ClickEventWriter> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);

    private readonly ClickEventChannel _channel = channel;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<ClickEventWriter> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ClickEvent>(BatchSize);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await FillBatchAsync(batch, stoppingToken);

                if (batch.Count > 0)
                {
                    await FlushAsync(batch, CancellationToken.None);
                    batch.Clear();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
        finally
        {
            // Best-effort: flush whatever was buffered when shutdown/cancellation interrupted
            // the loop above, rather than silently dropping it.
            if (batch.Count > 0)
            {
                await FlushAsync(batch, CancellationToken.None);
            }
        }
    }

    // Reads from the channel until either BatchSize events have been collected or
    // FlushInterval elapses since this call started — whichever comes first.
    private async Task FillBatchAsync(List<ClickEvent> batch, CancellationToken stoppingToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(FlushInterval);

        try
        {
            while (batch.Count < BatchSize)
            {
                if (!await _channel.Reader.WaitToReadAsync(timeoutCts.Token))
                {
                    return;
                }

                while (batch.Count < BatchSize && _channel.Reader.TryRead(out var clickEvent))
                {
                    batch.Add(clickEvent);
                }
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // FlushInterval elapsed before BatchSize events arrived — flush whatever we have.
        }
    }

    private async Task FlushAsync(List<ClickEvent> batch, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clickCounterUpdater = scope.ServiceProvider.GetRequiredService<ILinkClickCounterUpdater>();

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            dbContext.ClickEvents.AddRange(batch);
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var group in batch.GroupBy(c => c.LinkId))
            {
                await clickCounterUpdater.IncrementClickCountAsync(group.Key, group.Count(), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation(
                "Flushed a batch of {BatchSize} click events in {ElapsedMilliseconds}ms.",
                batch.Count,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // A flush failure (e.g. Postgres transiently unreachable) drops this batch rather
            // than crashing the BackgroundService — losing some analytics is preferable to an
            // unhandled exception here taking down the whole host.
            _logger.LogError(ex, "Failed to flush a batch of {Count} click events; batch dropped.", batch.Count);
        }
    }
}
