using System.Collections.Generic;
using System.Linq;
using MarketDumper.Models;

namespace MarketDumper.Services;

public class InventoryScanner : IInventoryScanner
{
    private readonly IInventoryDataProvider _dataProvider;

    public InventoryScanner(IInventoryDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public List<InventoryMatch> FindMatchingItems(IReadOnlyList<SellRule> rules)
    {
        var ruleItemIds = new HashSet<uint>(rules.Select(r => r.ItemId));
        var allItems = _dataProvider.GetAllInventoryItems();

        return allItems
            .Where(item => ruleItemIds.Contains(item.ItemId))
            .GroupBy(item => item.ItemId)
            .Select(group => new InventoryMatch(
                ItemId: group.Key,
                TotalQuantity: group.Sum(i => i.Quantity),
                Slots: group
                    .OrderBy(i => i.ContainerIndex)
                    .ThenBy(i => i.SlotIndex)
                    .Select(i => new InventorySlot(i.ContainerIndex, i.SlotIndex, i.Quantity, i.IsHq))
                    .ToList()
            ))
            .ToList();
    }
}
