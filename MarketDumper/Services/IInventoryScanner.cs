using System.Collections.Generic;
using MarketDumper.Models;

namespace MarketDumper.Services;

public interface IInventoryScanner
{
    List<InventoryMatch> FindMatchingItems(IReadOnlyList<SellRule> rules);
}

public record InventoryItemData(uint ItemId, int Quantity, bool IsHq, int ContainerIndex, int SlotIndex);

public interface IInventoryDataProvider
{
    List<InventoryItemData> GetAllInventoryItems();
}
