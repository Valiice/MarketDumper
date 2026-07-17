using System;
using System.Collections.Generic;

namespace MarketDumper.Models;

[Serializable]
public class RetainerSnapshot
{
    public List<RetainerListingEntry> Listings { get; set; } = new();
    public int MarketItemCount { get; set; }
    public ulong Gil { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

[Serializable]
public class RetainerListingEntry
{
    public int SlotIndex { get; set; }
    public uint ItemId { get; set; }
    public int Quantity { get; set; }
}
