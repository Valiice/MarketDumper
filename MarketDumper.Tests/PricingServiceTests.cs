using MarketDumper.Models;
using MarketDumper.Services;
using Xunit;

namespace MarketDumper.Tests;

public class PricingServiceTests
{
    private readonly PricingService _sut = new();

    [Fact]
    public void CalculatePrice_DefaultConfig_UndercutsBy1Gil()
    {
        var config = new PricingConfig();
        var result = _sut.CalculatePrice(100, config);
        Assert.Equal(99, result);
    }

    [Fact]
    public void CalculatePrice_CustomDelta_UndercutsByDelta()
    {
        var config = new PricingConfig { Delta = 5 };
        var result = _sut.CalculatePrice(100, config);
        Assert.Equal(95, result);
    }

    [Fact]
    public void CalculatePrice_WithMod_StripsRemainder()
    {
        var config = new PricingConfig { Mod = 10 };
        var result = _sut.CalculatePrice(457, config);
        Assert.Equal(449, result);
    }

    [Fact]
    public void CalculatePrice_WithMultiple_RoundsDown()
    {
        var config = new PricingConfig { Multiple = 10 };
        var result = _sut.CalculatePrice(100, config);
        Assert.Equal(90, result);
    }

    [Fact]
    public void CalculatePrice_ResultBelowMin_ClampsToMin()
    {
        var config = new PricingConfig { Delta = 200, MinPrice = 50 };
        var result = _sut.CalculatePrice(100, config);
        Assert.Equal(50, result);
    }

    [Fact]
    public void CalculatePrice_ResultNegative_ClampsToMin()
    {
        var config = new PricingConfig { Delta = 500, MinPrice = 1 };
        var result = _sut.CalculatePrice(100, config);
        Assert.Equal(1, result);
    }

    [Fact]
    public void FindTargetPrice_SkipsOwnRetainers()
    {
        var ownRetainers = new HashSet<ulong> { 111 };
        var listings = new List<MarketListing>
        {
            new(100, 111, false),
            new(200, 222, false),
        };
        var config = new PricingConfig();
        var result = _sut.FindTargetPrice(listings, config, isHq: false, ownRetainers);
        Assert.Equal(199, result);
    }

    [Fact]
    public void FindTargetPrice_HqItem_SkipsNqListings()
    {
        var ownRetainers = new HashSet<ulong>();
        var listings = new List<MarketListing>
        {
            new(100, 222, false),
            new(300, 333, true),
        };
        var config = new PricingConfig();
        var result = _sut.FindTargetPrice(listings, config, isHq: true, ownRetainers);
        Assert.Equal(299, result);
    }

    [Fact]
    public void FindTargetPrice_NoListings_ReturnsNull()
    {
        var ownRetainers = new HashSet<ulong>();
        var listings = new List<MarketListing>();
        var config = new PricingConfig();
        var result = _sut.FindTargetPrice(listings, config, isHq: false, ownRetainers);
        Assert.Null(result);
    }

    [Fact]
    public void FindTargetPrice_AllOwnRetainers_ReturnsNull()
    {
        var ownRetainers = new HashSet<ulong> { 111, 222 };
        var listings = new List<MarketListing>
        {
            new(100, 111, false),
            new(200, 222, false),
        };
        var config = new PricingConfig();
        var result = _sut.FindTargetPrice(listings, config, isHq: false, ownRetainers);
        Assert.Null(result);
    }

    [Fact]
    public void FindTargetPrice_UndercutSelfTrue_IncludesOwnRetainers()
    {
        var ownRetainers = new HashSet<ulong> { 111 };
        var listings = new List<MarketListing>
        {
            new(100, 111, false),
            new(200, 222, false),
        };
        var config = new PricingConfig { UndercutSelf = true };
        var result = _sut.FindTargetPrice(listings, config, isHq: false, ownRetainers);
        Assert.Equal(99, result);
    }
}
