using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Commands;
using MarketDumper.Models;
using MarketDumper.Services;
using Moq;
using Xunit;

namespace MarketDumper.Tests;

public class ConsolidateRetainerListingsCommandTests
{
    private readonly Mock<IRetainerListingReader> _reader = new();
    private readonly Mock<IAddonInteractor> _addon = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static IReadOnlyList<SellRule> Rules(uint itemId, int stackSize = 99) =>
        new List<SellRule> { new() { ItemId = itemId, StackSize = stackSize, Enabled = true } };

    private static List<InventoryMatch> Matches(params uint[] itemIds)
    {
        var list = new List<InventoryMatch>();
        foreach (var id in itemIds)
            list.Add(new InventoryMatch(id, 10, new List<InventorySlot> { new(0, 0, 10, false) }));
        return list;
    }

    private ConsolidateRetainerListingsCommand Build(
        List<InventoryMatch> matches,
        IReadOnlyList<SellRule> rules) =>
        new(_reader.Object, _addon.Object, matches, rules, TimeSpan.FromSeconds(5));

    [Fact]
    public async Task Execute_NoListings_ReturnsSuccess()
    {
        _reader.Setup(r => r.ReadListingsAsync()).ReturnsAsync(new List<RetainerListing>());

        var result = await Build(Matches(1001), Rules(1001))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.RightClickRetainerListing(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Execute_ListingBelowStackSize_ItemInInventory_ReturnsIt()
    {
        _reader.Setup(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing> { new(0, 1001, 10) });

        _addon.Setup(a => a.RightClickRetainerListing(0)).ReturnsAsync(true);
        _addon.Setup(a => a.WaitForAddon("ContextMenu", It.IsAny<TimeSpan>(), _ct)).ReturnsAsync(true);
        _addon.Setup(a => a.ClickAddonButton("ContextMenu", 2)).ReturnsAsync(true);

        var result = await Build(Matches(1001), Rules(1001, stackSize: 99))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.RightClickRetainerListing(0), Times.Once);
        _addon.Verify(a => a.ClickAddonButton("ContextMenu", 2), Times.Once);
    }

    [Fact]
    public async Task Execute_ListingAtStackSize_NotReturned()
    {
        // A listing of exactly 99 when StackSize=99 should NOT be returned
        _reader.Setup(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing> { new(0, 1001, 99) });

        var result = await Build(Matches(1001), Rules(1001, stackSize: 99))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.RightClickRetainerListing(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Execute_ItemNotInInventory_NotReturned()
    {
        // Listing for item 1002, but player only has 1001
        _reader.Setup(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing> { new(0, 1002, 10) });

        var result = await Build(Matches(1001), Rules(1002, stackSize: 99))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.RightClickRetainerListing(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Execute_MultipleListings_ReturnsInReverseSlotOrder()
    {
        // Slots 0, 1, 2 — all qualifying. Should right-click 2, then 1, then 0.
        _reader.Setup(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing>
            {
                new(0, 1001, 5),
                new(1, 1001, 8),
                new(2, 1001, 3),
            });

        var order = new List<int>();
        _addon.Setup(a => a.RightClickRetainerListing(It.IsAny<int>()))
            .Callback<int>(order.Add)
            .ReturnsAsync(true);
        _addon.Setup(a => a.WaitForAddon("ContextMenu", It.IsAny<TimeSpan>(), _ct)).ReturnsAsync(true);
        _addon.Setup(a => a.ClickAddonButton("ContextMenu", 2)).ReturnsAsync(true);

        await Build(Matches(1001), Rules(1001)).ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(new[] { 2, 1, 0 }, order);
    }

    [Fact]
    public async Task Execute_NoMatchingRule_NotReturned()
    {
        // Item 1001 is listed but there is no sell rule for it
        _reader.Setup(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing> { new(0, 1001, 10) });

        var result = await Build(Matches(1001), new List<SellRule>())
            .ExecuteAsync(new CommandContext(), _ct);

        _addon.Verify(a => a.RightClickRetainerListing(It.IsAny<int>()), Times.Never);
    }
}
