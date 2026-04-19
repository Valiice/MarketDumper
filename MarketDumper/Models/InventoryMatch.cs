using System.Collections.Generic;

namespace MarketDumper.Models;

public record InventoryMatch(
    uint ItemId,
    int TotalQuantity,
    List<InventorySlot> Slots
);
