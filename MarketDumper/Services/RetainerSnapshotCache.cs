using System;
using System.Collections.Generic;
using System.Linq;
using MarketDumper.Models;

namespace MarketDumper.Services;

public class RetainerSnapshotCache : IRetainerSnapshotCache
{
    private readonly Configuration _configuration;
    private readonly Func<ulong> _currentContentId;
    private readonly Action _save;
    private bool _dirty;

    public RetainerSnapshotCache(Configuration configuration, Func<ulong> currentContentId, Action save)
    {
        _configuration = configuration;
        _currentContentId = currentContentId;
        _save = save;
    }

    public bool TryGetValid(ulong retainerId, int currentMarketItemCount, ulong currentGil, out IReadOnlyList<RetainerListing> listings)
    {
        listings = Array.Empty<RetainerListing>();
        var contentId = _currentContentId();
        if (contentId == 0 || retainerId == 0)
            return false;

        lock (Configuration.SyncRoot)
        {
            if (!_configuration.RetainerSnapshots.TryGetValue(contentId, out var byRetainer))
                return false;
            if (!byRetainer.TryGetValue(retainerId, out var snapshot))
                return false;
            if (snapshot.MarketItemCount != currentMarketItemCount || snapshot.Gil != currentGil)
                return false;

            listings = snapshot.Listings
                .Select(e => new RetainerListing(e.SlotIndex, e.ItemId, e.Quantity))
                .ToList();
            return true;
        }
    }

    public void Update(ulong retainerId, IReadOnlyList<RetainerListing> listings, ulong gil)
    {
        var contentId = _currentContentId();
        if (contentId == 0 || retainerId == 0)
            return;

        lock (Configuration.SyncRoot)
        {
            if (!_configuration.RetainerSnapshots.TryGetValue(contentId, out var byRetainer))
            {
                byRetainer = new Dictionary<ulong, RetainerSnapshot>();
                _configuration.RetainerSnapshots[contentId] = byRetainer;
            }

            byRetainer[retainerId] = new RetainerSnapshot
            {
                Listings = listings.Select(l => new RetainerListingEntry
                {
                    SlotIndex = l.SlotIndex, ItemId = l.ItemId, Quantity = l.Quantity
                }).ToList(),
                MarketItemCount = listings.Count,
                Gil = gil,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _dirty = true;
        }
    }

    public void Invalidate(ulong retainerId)
    {
        var contentId = _currentContentId();
        if (contentId == 0 || retainerId == 0)
            return;

        lock (Configuration.SyncRoot)
        {
            if (_configuration.RetainerSnapshots.TryGetValue(contentId, out var byRetainer)
                && byRetainer.Remove(retainerId))
                _dirty = true;
        }
    }

    // Disk writes are batched: a run mutates several snapshots, one save persists them.
    public void Flush()
    {
        bool save;
        lock (Configuration.SyncRoot)
        {
            save = _dirty;
            _dirty = false;
        }
        if (save)
            _save();
    }
}
