using UrlShortener.Api.Modules.Analytics.Channels;
using UrlShortener.Api.Modules.Analytics.Services;

namespace UrlShortener.Api.Tests.Modules.Analytics.Services;

// Pure unit test, no infra: Record() just builds a ClickEvent and writes it to the channel.
public class ClickEventRecorderTests
{
    [Fact]
    public async Task Record_WritesAClickEventWithTheGivenFieldsToTheChannel()
    {
        var channel = new ClickEventChannel();
        var recorder = new ClickEventRecorder(channel);

        recorder.Record(linkId: 42, referrer: "https://example.com/from", userAgent: "TestAgent/1.0", ipAddress: "203.0.113.5");

        Assert.True(await channel.Reader.WaitToReadAsync());
        Assert.True(channel.Reader.TryRead(out var clickEvent));
        Assert.Equal(42, clickEvent.LinkId);
        Assert.Equal("https://example.com/from", clickEvent.Referrer);
        Assert.Equal("TestAgent/1.0", clickEvent.UserAgent);
        Assert.Equal("203.0.113.5", clickEvent.IpAddress);
    }

    [Fact]
    public async Task Record_WithNullOptionalFields_WritesAClickEventWithNulls()
    {
        var channel = new ClickEventChannel();
        var recorder = new ClickEventRecorder(channel);

        recorder.Record(linkId: 1, referrer: null, userAgent: null, ipAddress: null);

        Assert.True(await channel.Reader.WaitToReadAsync());
        Assert.True(channel.Reader.TryRead(out var clickEvent));
        Assert.Equal(1, clickEvent.LinkId);
        Assert.Null(clickEvent.Referrer);
        Assert.Null(clickEvent.UserAgent);
        Assert.Null(clickEvent.IpAddress);
    }
}
