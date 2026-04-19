using MarketDumper.Models;
using MarketDumper.Services;
using Moq;
using Xunit;

namespace MarketDumper.Tests;

public class InventoryScannerTests
{
    private static InventoryScanner CreateScanner(List<InventoryItemData> items)
    {
        var provider = new Mock<IInventoryDataProvider>();
        provider.Setup(p => p.GetAllInventoryItems()).Returns(items);
        return new InventoryScanner(provider.Object);
    }

    [Fact]
    public void FindMatchingItems_MatchingRule_ReturnsMatch()
    {
        var items = new List<InventoryItemData>
        {
            new(ItemId: 100, Quantity: 999, IsHq: false, ContainerIndex: 0, SlotIndex: 5)
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 100, ItemName = "Ore", StackSize = 99, Enabled = true }
        };
        var scanner = CreateScanner(items);
        var matches = scanner.FindMatchingItems(rules);
        Assert.Single(matches);
        Assert.Equal((uint)100, matches[0].ItemId);
        Assert.Equal(999, matches[0].TotalQuantity);
        Assert.Single(matches[0].Slots);
    }

    [Fact]
    public void FindMatchingItems_NoMatchingItems_ReturnsEmpty()
    {
        var items = new List<InventoryItemData>
        {
            new(ItemId: 200, Quantity: 50, IsHq: false, ContainerIndex: 0, SlotIndex: 0)
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 100, ItemName = "Ore", StackSize = 99, Enabled = true }
        };
        var scanner = CreateScanner(items);
        var matches = scanner.FindMatchingItems(rules);
        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatchingItems_MultipleSlotsSameItem_AggregatesQuantity()
    {
        var items = new List<InventoryItemData>
        {
            new(ItemId: 100, Quantity: 500, IsHq: false, ContainerIndex: 0, SlotIndex: 0),
            new(ItemId: 100, Quantity: 499, IsHq: false, ContainerIndex: 1, SlotIndex: 3),
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 100, ItemName = "Ore", StackSize = 99, Enabled = true }
        };
        var scanner = CreateScanner(items);
        var matches = scanner.FindMatchingItems(rules);
        Assert.Single(matches);
        Assert.Equal(999, matches[0].TotalQuantity);
        Assert.Equal(2, matches[0].Slots.Count);
    }

    [Fact]
    public void FindMatchingItems_HqAndNqSameItem_TracksHqPerSlot()
    {
        var items = new List<InventoryItemData>
        {
            new(ItemId: 100, Quantity: 50, IsHq: false, ContainerIndex: 0, SlotIndex: 0),
            new(ItemId: 100, Quantity: 30, IsHq: true, ContainerIndex: 0, SlotIndex: 1),
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 100, ItemName = "Ore", StackSize = 99, Enabled = true }
        };
        var scanner = CreateScanner(items);
        var matches = scanner.FindMatchingItems(rules);
        Assert.Single(matches);
        Assert.Equal(80, matches[0].TotalQuantity);
        Assert.False(matches[0].Slots[0].IsHq);
        Assert.True(matches[0].Slots[1].IsHq);
    }

    [Fact]
    public void FindMatchingItems_MultipleRules_ReturnsMultipleMatches()
    {
        var items = new List<InventoryItemData>
        {
            new(ItemId: 100, Quantity: 500, IsHq: false, ContainerIndex: 0, SlotIndex: 0),
            new(ItemId: 200, Quantity: 300, IsHq: false, ContainerIndex: 1, SlotIndex: 0),
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 100, ItemName = "Ore", StackSize = 99, Enabled = true },
            new() { ItemId = 200, ItemName = "Sand", StackSize = 99, Enabled = true },
        };
        var scanner = CreateScanner(items);
        var matches = scanner.FindMatchingItems(rules);
        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void FindMatchingItems_SlotsOrderedByContainerThenSlot()
    {
        var items = new List<InventoryItemData>
        {
            new(ItemId: 100, Quantity: 100, IsHq: false, ContainerIndex: 2, SlotIndex: 5),
            new(ItemId: 100, Quantity: 200, IsHq: false, ContainerIndex: 0, SlotIndex: 3),
            new(ItemId: 100, Quantity: 150, IsHq: false, ContainerIndex: 0, SlotIndex: 1),
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 100, ItemName = "Ore", StackSize = 99, Enabled = true }
        };
        var scanner = CreateScanner(items);
        var matches = scanner.FindMatchingItems(rules);
        var slots = matches[0].Slots;
        Assert.Equal(0, slots[0].ContainerIndex);
        Assert.Equal(1, slots[0].SlotIndex);
        Assert.Equal(0, slots[1].ContainerIndex);
        Assert.Equal(3, slots[1].SlotIndex);
        Assert.Equal(2, slots[2].ContainerIndex);
        Assert.Equal(5, slots[2].SlotIndex);
    }

    [Fact]
    public void FindMatchingItems_EmptyRules_ReturnsEmpty()
    {
        var items = new List<InventoryItemData>
        {
            new(ItemId: 100, Quantity: 999, IsHq: false, ContainerIndex: 0, SlotIndex: 0)
        };
        var rules = new List<SellRule>();
        var scanner = CreateScanner(items);
        var matches = scanner.FindMatchingItems(rules);
        Assert.Empty(matches);
    }
}
