using MarketDumper.Models;
using MarketDumper.Services;
using Xunit;

namespace MarketDumper.Tests;

public class RetainerSnapshotCacheTests
{
    private readonly Configuration _config = new();
    private ulong _contentId = 111;
    private int _saves;
    private readonly RetainerSnapshotCache _cache;

    public RetainerSnapshotCacheTests()
    {
        _cache = new RetainerSnapshotCache(_config, () => _contentId, () => _saves++);
    }

    private static List<RetainerListing> SomeListings() =>
        new() { new(0, 46246, 99), new(3, 49211, 31) };

    [Fact]
    public void TryGetValid_ReturnsFalse_WhenNothingCached()
    {
        Assert.False(_cache.TryGetValid(42, 2, 1000, out _));
    }

    [Fact]
    public void TryGetValid_ReturnsListings_WhenCountAndGilMatch()
    {
        _cache.Update(42, SomeListings(), gil: 1000);
        Assert.True(_cache.TryGetValid(42, 2, 1000, out var listings));
        Assert.Equal(2, listings.Count);
        Assert.Equal(46246u, listings[0].ItemId);
        Assert.Equal(31, listings[1].Quantity);

        // Saves are batched: nothing hits disk until Flush, and only once per dirty run.
        Assert.Equal(0, _saves);
        _cache.Flush();
        Assert.Equal(1, _saves);
        _cache.Flush();
        Assert.Equal(1, _saves);
    }

    [Fact]
    public void TryGetValid_ReturnsFalse_WhenListingCountChanged()
    {
        _cache.Update(42, SomeListings(), gil: 1000);
        Assert.False(_cache.TryGetValid(42, 1, 1000, out _)); // one sold
    }

    [Fact]
    public void TryGetValid_ReturnsFalse_WhenGilChanged()
    {
        _cache.Update(42, SomeListings(), gil: 1000);
        Assert.False(_cache.TryGetValid(42, 2, 2500, out _)); // sale proceeds arrived
    }

    [Fact]
    public void Invalidate_RemovesSnapshot()
    {
        _cache.Update(42, SomeListings(), gil: 1000);
        _cache.Invalidate(42);
        Assert.False(_cache.TryGetValid(42, 2, 1000, out _));
    }

    [Fact]
    public void Snapshots_AreIsolatedPerCharacter()
    {
        _cache.Update(42, SomeListings(), gil: 1000);
        _contentId = 222; // switch character
        Assert.False(_cache.TryGetValid(42, 2, 1000, out _));
    }

    [Fact]
    public void TryGetValid_ReturnsFalse_WhenContentIdUnknown()
    {
        _cache.Update(42, SomeListings(), gil: 1000);
        _contentId = 0; // not logged in
        Assert.False(_cache.TryGetValid(42, 2, 1000, out _));
    }
}
