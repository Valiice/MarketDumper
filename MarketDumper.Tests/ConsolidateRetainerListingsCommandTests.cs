using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
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
    private readonly Mock<IPluginLog> _log = new();
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
        new(_reader.Object, _addon.Object, _log.Object, matches, rules, TimeSpan.FromSeconds(5));

    private void SetupSuccessfulReturn(int contextMenuIndex = 2)
    {
        _addon.Setup(a => a.ScrollRetainerSellListTo(It.IsAny<int>())).ReturnsAsync(true);
        _addon.Setup(a => a.RightClickRetainerListing(It.IsAny<int>())).ReturnsAsync(true);
        _addon.Setup(a => a.WaitForAddon("ContextMenu", It.IsAny<TimeSpan>(), _ct)).ReturnsAsync(true);
        _addon.Setup(a => a.FindContextMenuItemByText(It.IsAny<string>())).ReturnsAsync(contextMenuIndex);
        _addon.Setup(a => a.ClickAddonButton("ContextMenu", contextMenuIndex)).ReturnsAsync(true);
        _addon.Setup(a => a.IsAddonVisible("SelectYesno")).ReturnsAsync(false);
        _addon.Setup(a => a.IsAddonVisible("InputNumeric")).ReturnsAsync(false);
        _addon.Setup(a => a.IsAddonVisible("ContextMenu")).ReturnsAsync(false);
    }

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

        SetupSuccessfulReturn(contextMenuIndex: 2);

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
        // Listing for item 1002, but player only has 1001 and there is only one listing of 1002
        _reader.Setup(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing> { new(0, 1002, 10) });

        var result = await Build(Matches(1001), Rules(1002, stackSize: 99))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.RightClickRetainerListing(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Execute_MultipleListings_ReturnsInReverseDisplayRowOrder()
    {
        // Slots 0, 1, 2 — all qualifying. Display rows match slot order (same item).
        // Processing must go 2 → 1 → 0 so row indices stay stable as items are removed.
        _reader.Setup(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing>
            {
                new(0, 1001, 5),
                new(1, 1001, 8),
                new(2, 1001, 3),
            });

        var order = new List<int>();
        _addon.Setup(a => a.ScrollRetainerSellListTo(It.IsAny<int>())).ReturnsAsync(true);
        _addon.Setup(a => a.RightClickRetainerListing(It.IsAny<int>()))
            .Callback<int>(order.Add)
            .ReturnsAsync(true);
        _addon.Setup(a => a.WaitForAddon("ContextMenu", It.IsAny<TimeSpan>(), _ct)).ReturnsAsync(true);
        _addon.Setup(a => a.FindContextMenuItemByText(It.IsAny<string>())).ReturnsAsync(2);
        _addon.Setup(a => a.ClickAddonButton("ContextMenu", 2)).ReturnsAsync(true);

        var result = await Build(Matches(1001), Rules(1001)).ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(new[] { 2, 1, 0 }, order);
        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.ClickAddonButton("ContextMenu", 2), Times.Exactly(3));
    }

    [Fact]
    public async Task Execute_NoMatchingRule_NotReturned()
    {
        // Item 1001 is listed but there is no sell rule for it
        _reader.Setup(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing> { new(0, 1001, 10) });

        var result = await Build(Matches(1001), new List<SellRule>())
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.RightClickRetainerListing(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Execute_MultipleListingsSameItem_NoPlayerInventory_ReturnsAll()
    {
        // Retainer has 3 undersized listings of the same item; player has none.
        // All 3 qualify via the multi-fragment path (listingCount > 1).
        _reader.Setup(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing>
            {
                new(0, 1001, 5),
                new(1, 1001, 8),
                new(2, 1001, 3),
            });

        SetupSuccessfulReturn(contextMenuIndex: 2);

        var result = await Build(new List<InventoryMatch>(), Rules(1001, stackSize: 99))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.RightClickRetainerListing(It.IsAny<int>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Execute_UsesTextSearchResultForContextMenuClick()
    {
        // AutoPinch injects an extra context menu item, shifting "Return Items to
        // Inventory" from index 2 to index 3. The text-search result must be used.
        _reader.Setup(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing> { new(0, 1001, 10) });

        _addon.Setup(a => a.ScrollRetainerSellListTo(It.IsAny<int>())).ReturnsAsync(true);
        _addon.Setup(a => a.RightClickRetainerListing(0)).ReturnsAsync(true);
        _addon.Setup(a => a.WaitForAddon("ContextMenu", It.IsAny<TimeSpan>(), _ct)).ReturnsAsync(true);
        _addon.Setup(a => a.FindContextMenuItemByText("Return Items to Inventory")).ReturnsAsync(3);
        _addon.Setup(a => a.ClickAddonButton("ContextMenu", It.IsAny<int>())).ReturnsAsync(true);

        await Build(Matches(1001), Rules(1001, stackSize: 99)).ExecuteAsync(new CommandContext(), _ct);

        _addon.Verify(a => a.ClickAddonButton("ContextMenu", 3), Times.Once);
        _addon.Verify(a => a.ClickAddonButton("ContextMenu", 2), Times.Never);
    }

    [Fact]
    public async Task Execute_TextSearchReturnsNegative_FallsBackToIndex2()
    {
        // If "Return Items to Inventory" cannot be found by text, fall back to index 2.
        _reader.Setup(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing> { new(0, 1001, 10) });

        _addon.Setup(a => a.ScrollRetainerSellListTo(It.IsAny<int>())).ReturnsAsync(true);
        _addon.Setup(a => a.RightClickRetainerListing(0)).ReturnsAsync(true);
        _addon.Setup(a => a.WaitForAddon("ContextMenu", It.IsAny<TimeSpan>(), _ct)).ReturnsAsync(true);
        _addon.Setup(a => a.FindContextMenuItemByText(It.IsAny<string>())).ReturnsAsync(-1);
        _addon.Setup(a => a.ClickAddonButton("ContextMenu", 2)).ReturnsAsync(true);

        await Build(Matches(1001), Rules(1001, stackSize: 99)).ExecuteAsync(new CommandContext(), _ct);

        _addon.Verify(a => a.ClickAddonButton("ContextMenu", 2), Times.Once);
    }

    [Fact]
    public async Task Execute_ListingGoneOnReread_Skipped()
    {
        // Slot 1 disappears between the initial scan and when the loop processes it
        // (sold externally, or returned by a prior iteration). Must be silently skipped.
        _reader.SetupSequence(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing> { new(0, 1001, 5), new(1, 1001, 3) })  // initial
            .ReturnsAsync(new List<RetainerListing> { new(0, 1001, 5) })                   // re-read: slot 1 gone
            .ReturnsAsync(new List<RetainerListing> { new(0, 1001, 5) })                   // re-read: slot 0 present
            .ReturnsAsync(new List<RetainerListing>());                                     // verification

        SetupSuccessfulReturn(contextMenuIndex: 2);

        var result = await Build(Matches(1001), Rules(1001, stackSize: 99))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.RightClickRetainerListing(0), Times.Once);
        _addon.Verify(a => a.RightClickRetainerListing(1), Times.Never);
    }

    [Fact]
    public async Task Execute_SelectYesnoAfterReturn_ClicksYes()
    {
        _reader.Setup(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing> { new(0, 1001, 10) });

        _addon.Setup(a => a.ScrollRetainerSellListTo(It.IsAny<int>())).ReturnsAsync(true);
        _addon.Setup(a => a.RightClickRetainerListing(0)).ReturnsAsync(true);
        _addon.Setup(a => a.WaitForAddon("ContextMenu", It.IsAny<TimeSpan>(), _ct)).ReturnsAsync(true);
        _addon.Setup(a => a.FindContextMenuItemByText(It.IsAny<string>())).ReturnsAsync(2);
        _addon.Setup(a => a.ClickAddonButton("ContextMenu", 2)).ReturnsAsync(true);
        _addon.Setup(a => a.IsAddonVisible("ContextMenu")).ReturnsAsync(false);
        _addon.SetupSequence(a => a.IsAddonVisible("SelectYesno"))
            .ReturnsAsync(false)   // stale check before right-click
            .ReturnsAsync(true);   // confirmation dialog after clicking "Return Items to Inventory"
        _addon.Setup(a => a.ClickAddonButton("SelectYesno", 0)).ReturnsAsync(true);
        _addon.Setup(a => a.IsAddonVisible("InputNumeric")).ReturnsAsync(false);

        await Build(Matches(1001), Rules(1001, stackSize: 99)).ExecuteAsync(new CommandContext(), _ct);

        _addon.Verify(a => a.ClickAddonButton("SelectYesno", 0), Times.Once);
    }
}
