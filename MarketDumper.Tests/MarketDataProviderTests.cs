using Dalamud.Game.Network.Structures;
using Dalamud.Plugin.Services;
using MarketDumper.Services;
using Moq;
using Xunit;

namespace MarketDumper.Tests;

public class MarketDataProviderTests
{
    private readonly Mock<IMarketBoard> _marketBoard = new();
    private readonly Mock<IPluginLog> _log = new();
    private readonly MarketDataProvider _provider;

    public MarketDataProviderTests()
    {
        _provider = new MarketDataProvider(
            _marketBoard.Object, new Configuration(), _log.Object,
            ownRetainerIds: () => new HashSet<ulong>());
    }

    private static IMarketBoardCurrentOfferings Offerings(uint itemId, params int[] prices)
    {
        var listings = prices.Select(p =>
        {
            var l = new Mock<IMarketBoardItemListing>();
            l.Setup(x => x.ItemId).Returns(itemId);
            l.Setup(x => x.PricePerUnit).Returns((uint)p);
            l.Setup(x => x.RetainerId).Returns(0UL);
            l.Setup(x => x.IsHq).Returns(false);
            return l.Object;
        }).ToList();
        var offerings = new Mock<IMarketBoardCurrentOfferings>();
        offerings.Setup(o => o.ItemListings).Returns(listings);
        return offerings.Object;
    }

    private void Raise(IMarketBoardCurrentOfferings offerings) =>
        _marketBoard.Raise(m => m.OfferingsReceived += null, offerings);

    [Fact]
    public async Task Wait_CompletesWithListings_WhenMatchingOfferingsArrive()
    {
        var wait = _provider.WaitForMarketDataAsync(46246, TimeSpan.FromSeconds(5), CancellationToken.None);
        Raise(Offerings(46246, 100, 200));
        var result = await wait;
        Assert.NotNull(result);
        Assert.Equal(2, result!.Listings.Count);
    }

    [Fact]
    public async Task Wait_IgnoresEmptyResponses()
    {
        // A zero-listing response has no item id and must not resolve the wait.
        var wait = _provider.WaitForMarketDataAsync(46246, TimeSpan.FromMilliseconds(300), CancellationToken.None);
        Raise(Offerings(49211)); // empty listings for a different item
        var result = await wait;
        Assert.Null(result);
    }

    [Fact]
    public async Task Wait_IgnoresOfferingsForOtherItem()
    {
        var wait = _provider.WaitForMarketDataAsync(46246, TimeSpan.FromMilliseconds(300), CancellationToken.None);
        Raise(Offerings(49211, 100));
        var result = await wait; // times out -> null, wrong item never satisfies it
        Assert.Null(result);
    }

    [Fact]
    public async Task SecondWait_DoesNotReuseCompletedRequest()
    {
        var first = _provider.WaitForMarketDataAsync(46246, TimeSpan.FromSeconds(5), CancellationToken.None);
        Raise(Offerings(46246, 100));
        Assert.NotNull(await first);

        // Same item again: must NOT return instantly with the old result.
        var second = await _provider.WaitForMarketDataAsync(46246, TimeSpan.FromMilliseconds(300), CancellationToken.None);
        Assert.Null(second); // no new offerings arrived -> timeout, not stale data
    }

    [Fact]
    public async Task OfferingsAfterRunEnds_AreIgnoredSilently()
    {
        var wait = _provider.WaitForMarketDataAsync(46246, TimeSpan.FromMilliseconds(200), CancellationToken.None);
        Assert.Null(await wait); // run "ended" (timed out), state must be cleared

        _log.Invocations.Clear();
        Raise(Offerings(49211, 100)); // foreign response hours later
        _log.Verify(l => l.Warning(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Wait_ReturnsPreArmedResult_WhenResponseArrivedBeforeWait()
    {
        _provider.PrepareForMarketData(46246);
        Raise(Offerings(46246, 100)); // response lands before WaitForMarketDataAsync is called

        var result = await _provider.WaitForMarketDataAsync(46246, TimeSpan.FromMilliseconds(300), CancellationToken.None);

        Assert.NotNull(result); // pre-armed data must be returned, not discarded
        Assert.Single(result!.Listings);
    }
}
