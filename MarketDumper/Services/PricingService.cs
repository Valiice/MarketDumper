using System;
using System.Collections.Generic;
using MarketDumper.Models;

namespace MarketDumper.Services;

public class PricingService : IPricingService
{
    public int CalculatePrice(int lowestPrice, PricingConfig config)
    {
        var price = lowestPrice
            - (lowestPrice % config.Mod)
            - config.Delta;
        price -= (price % config.Multiple);
        return Math.Max(price, config.MinPrice);
    }

    public int? FindTargetPrice(
        IReadOnlyList<MarketListing> listings,
        PricingConfig config,
        bool isHq,
        IReadOnlySet<ulong> ownRetainerIds)
    {
        int? ownBestPrice = null;

        foreach (var listing in listings)
        {
            if (isHq && !listing.IsHq)
                continue;

            var isOwn = ownRetainerIds.Contains(listing.RetainerId);

            if (isOwn && !config.UndercutSelf)
            {
                ownBestPrice ??= listing.PricePerUnit;
                continue;
            }

            return CalculatePrice(listing.PricePerUnit, config);
        }

        if (ownBestPrice != null)
            return ownBestPrice;

        return null;
    }
}
