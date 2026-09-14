using UrlShortener.Api.Modules.Analytics.Channels;
using UrlShortener.Api.Modules.Analytics.Entities;

namespace UrlShortener.Api.Tests.Modules.Analytics.Channels;

// Pure unit tests, no infra: the channel is a thin DI wrapper around System.Threading.Channels
// configured bounded + DropOldest so a traffic spike sheds old analytics instead of blocking
// writers or growing memory unbounded.
public class ClickEventChannelTests
{
    [Fact]
    public void TryWrite_NeverBlocksOrFails_EvenWhenBufferIsFull()
    {
        var channel = new ClickEventChannel();

        // Capacity is 1000; write well past it to exercise DropOldest.
        for (var i = 0; i < 1100; i++)
        {
            Assert.True(channel.Writer.TryWrite(new ClickEvent { LinkId = i }));
        }
    }

    [Fact]
    public void TryWrite_WhenBufferIsFull_DropsOldestAndKeepsNewest()
    {
        var channel = new ClickEventChannel();

        for (var i = 0; i < 1000; i++)
        {
            channel.Writer.TryWrite(new ClickEvent { LinkId = i });
        }

        // One more than capacity: LinkId 0 (the oldest) should be evicted to make room.
        channel.Writer.TryWrite(new ClickEvent { LinkId = 1000 });

        Assert.True(channel.Reader.TryRead(out var oldestRemaining));
        Assert.Equal(1, oldestRemaining.LinkId);
    }

    [Fact]
    public async Task WrittenEvent_IsReadableFromReader()
    {
        var channel = new ClickEventChannel();
        var clickEvent = new ClickEvent { LinkId = 7, Referrer = "https://example.com" };

        Assert.True(channel.Writer.TryWrite(clickEvent));

        Assert.True(await channel.Reader.WaitToReadAsync());
        Assert.True(channel.Reader.TryRead(out var read));
        Assert.Same(clickEvent, read);
    }
}
