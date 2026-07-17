using System.Collections.Generic;
using MarketDumper.Models;

namespace MarketDumper.Services;

public interface IRetainerSnapshotCache
{
    bool TryGetValid(ulong retainerId, int currentMarketItemCount, ulong currentGil, out IReadOnlyList<RetainerListing> listings);
    void Update(ulong retainerId, IReadOnlyList<RetainerListing> listings, ulong gil);
    void Invalidate(ulong retainerId);
    void Flush();
}
