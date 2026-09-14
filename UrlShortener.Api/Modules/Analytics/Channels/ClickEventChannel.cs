using System.Threading.Channels;
using UrlShortener.Api.Modules.Analytics.Entities;

namespace UrlShortener.Api.Modules.Analytics.Channels;

// DI singleton. Bounded + DropOldest so a traffic spike sheds old analytics rather than
// growing unbounded memory or applying backpressure to the redirect hot path.
public class ClickEventChannel
{
    private readonly Channel<ClickEvent> _channel = Channel.CreateBounded<ClickEvent>(
        new BoundedChannelOptions(capacity: 1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    public ChannelReader<ClickEvent> Reader => _channel.Reader;
    public ChannelWriter<ClickEvent> Writer => _channel.Writer;
}
