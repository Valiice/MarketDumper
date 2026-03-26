using System.Collections.Generic;
using MarketDumper.Models;

namespace MarketDumper.Services;

public interface IPricingService
{
    int CalculatePrice(int lowestPrice, PricingConfig config);
    int? FindTargetPrice(IReadOnlyList<MarketListing> listings, PricingConfig config, bool isHq, IReadOnlySet<ulong> ownRetainerIds);
}

public record MarketListing(int PricePerUnit, ulong RetainerId, bool IsHq);
