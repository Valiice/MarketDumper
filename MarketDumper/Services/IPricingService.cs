using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Models;

namespace MarketDumper.Services;

public interface IPricingService
{
    int CalculatePrice(int lowestPrice, PricingConfig config);
    int? FindTargetPrice(IReadOnlyList<MarketListing> listings, PricingConfig config, bool isHq, IReadOnlySet<ulong> ownRetainerIds);
}

public record MarketListing(int PricePerUnit, ulong RetainerId, bool IsHq);

public record MarketDataResult(
    IReadOnlyList<MarketListing> Listings,
    PricingConfig PricingConfig,
    IReadOnlySet<ulong> OwnRetainerIds
);

public interface IMarketDataProvider
{
    void PrepareForMarketData(uint itemId);
    Task<MarketDataResult?> WaitForMarketDataAsync(uint itemId, TimeSpan timeout, CancellationToken cancellationToken);
}
