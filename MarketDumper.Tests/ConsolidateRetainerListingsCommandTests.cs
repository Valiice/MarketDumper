using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using MarketDumper.Automation;
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
    private readonly Mock<IRetainerSnapshotCache> _cache = new();
    private readonly ConsolidationPlanner _planner = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private const ulong RetainerId = 12345UL;
    private const ulong RetainerGil = 999UL;
    private static readonly int[] BottomFirstRowOrder = new[] { 1, 0 };

    public ConsolidateRetainerListingsCommandTests()
    {
        _addon.Setup(a => a.IsAddonVisible(It.IsAny<string>())).ReturnsAsync(false);
        _addon.Setup(a => a.WaitForAddon(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _addon.Setup(a => a.ScrollRetainerSellListTo(It.IsAny<int>())).ReturnsAsync(true);
        _addon.Setup(a => a.RightClickRetainerListing(It.IsAny<int>())).ReturnsAsync(true);
        _addon.Setup(a => a.ClickAddonButton(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(true);
        _addon.Setup(a => a.CloseAddon(It.IsAny<string>())).ReturnsAsync(true);
        _addon.Setup(a => a.GetFreeInventorySlots()).ReturnsAsync(20);
        _addon.Setup(a => a.GetRetainerSellListRowTexts()).ReturnsAsync(new List<string>());
    }

    private static IReadOnlyList<SellRule> Rules(uint itemId, string itemName, int stackSize = 99, bool allowPartial = false) =>
        new List<SellRule> { new() { ItemId = itemId, ItemName = itemName, StackSize = stackSize, Enabled = true, AllowPartial = allowPartial } };

    private static List<InventoryMatch> Matches(uint itemId, int quantity)
    {
        return new List<InventoryMatch>
        {
            new(itemId, quantity, new List<InventorySlot> { new(0, 0, quantity, false) })
        };
    }

    private ConsolidateRetainerListingsCommand Build(
        List<InventoryMatch> matches,
        IReadOnlyList<SellRule> rules) =>
        new(_reader.Object, _addon.Object, _log.Object, _planner, _cache.Object,
            RetainerId, RetainerGil, matches, rules, TimeSpan.FromSeconds(5));

    [Fact]
    public async Task NothingQualifies_UpdatesCacheAndSucceeds()
    {
        // All listings are full stacks - nothing for the planner to return.
        var listings = new List<RetainerListing> { new(0, 1001, 99) };
        _reader.Setup(r => r.ReadListingsAsync()).ReturnsAsync(listings);

        var result = await Build(Matches(1001, 10), Rules(1001, "Item1"))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.RightClickRetainerListing(It.IsAny<int>()), Times.Never);
        _cache.Verify(c => c.Update(RetainerId, listings, RetainerGil), Times.Once);
    }

    [Fact]
    public async Task QualifyingPartial_IsReturned_WhenRowVerifies()
    {
        // Slot 0, item 1, qty 31; inventory has 68 of the same item and rule stack size 99
        // -> forms a new full stack -> planner returns it. Row text verifies the row.
        var listings = new List<RetainerListing> { new(0, 1, 31) };
        var emptyAfterReturn = new List<RetainerListing>();
        _reader.SetupSequence(r => r.ReadListingsAsync())
            .ReturnsAsync(listings)           // initial read, reused for the first action
            .ReturnsAsync(emptyAfterReturn);  // verification re-read

        _addon.Setup(a => a.GetRetainerSellListRowTexts())
            .ReturnsAsync(new List<string> { "Item1 | 31" });
        _addon.Setup(a => a.FindContextMenuEntry(It.IsAny<string[]>())).ReturnsAsync(0);

        var result = await Build(Matches(1, 68), Rules(1, "Item1", stackSize: 99))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.ClickAddonButton("ContextMenu", 0), Times.Once);
        _cache.Verify(c => c.Update(RetainerId, emptyAfterReturn, RetainerGil), Times.Once);
    }

    [Fact]
    public async Task TargetFoundAtDifferentRow_ClicksActualRowAfterReverify()
    {
        // The computed display row (0) shows a different listing on screen; the real
        // target sits at row 1. The command must locate it by content, re-verify after
        // scrolling there, and click row 1 — never row 0.
        var listings = new List<RetainerListing> { new(0, 1, 31) };
        var emptyAfterReturn = new List<RetainerListing>();
        _reader.SetupSequence(r => r.ReadListingsAsync())
            .ReturnsAsync(listings)          // initial read, reused for the first action
            .ReturnsAsync(emptyAfterReturn);

        _addon.Setup(a => a.GetRetainerSellListRowTexts())
            .ReturnsAsync(new List<string> { "Other Thing | 99", "Item1 | 31" });
        _addon.Setup(a => a.FindContextMenuEntry(It.IsAny<string[]>())).ReturnsAsync(0);

        var result = await Build(Matches(1, 68), Rules(1, "Item1", stackSize: 99))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.RightClickRetainerListing(1), Times.Once);
        _addon.Verify(a => a.RightClickRetainerListing(0), Times.Never);
        _addon.Verify(a => a.ScrollRetainerSellListTo(1), Times.Once); // corrective scroll
        _cache.Verify(c => c.Update(RetainerId, emptyAfterReturn, RetainerGil), Times.Once);
    }

    [Fact]
    public async Task BuriedTarget_DigsOutByReturningVisibleFullStack()
    {
        // 14 listings (> rendered window of 13): the 31-partial is buried below the
        // viewport, so no rendered row shows it at first. The command must return a
        // visible full stack (shrinking the list), after which the target renders.
        var listings = new List<RetainerListing>();
        for (var s = 0; s < 13; s++)
            listings.Add(new RetainerListing(s, 1, 99));
        listings.Add(new RetainerListing(13, 1, 31));
        _reader.Setup(r => r.ReadListingsAsync()).ReturnsAsync(listings);

        var texts = new List<string> { "Item1 | 99", "Item1 | 99" }; // buried: no 31 rendered
        _addon.Setup(a => a.GetRetainerSellListRowTexts()).ReturnsAsync(() => texts);
        _addon.Setup(a => a.RightClickRetainerListing(It.IsAny<int>()))
            .Callback<int>(_ => texts = new List<string> { "Item1 | 31", "Item1 | 99" }) // list shrank, target visible
            .ReturnsAsync(true);
        _addon.Setup(a => a.FindContextMenuEntry(It.IsAny<string[]>())).ReturnsAsync(0);

        var result = await Build(Matches(1, 68), Rules(1, "Item1", stackSize: 99))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        // One dig-out right-click (a full stack) plus the target's right-click.
        _addon.Verify(a => a.RightClickRetainerListing(It.IsAny<int>()), Times.Exactly(2));
        _addon.Verify(a => a.ClickAddonButton("ContextMenu", 0), Times.Exactly(2));
    }

    [Fact]
    public async Task RowMismatch_SkipsAndInvalidates()
    {
        // Same setup as scenario 2, but the row text shows the wrong quantity (99 instead of 31).
        var listings = new List<RetainerListing> { new(0, 1, 31) };
        _reader.Setup(r => r.ReadListingsAsync()).ReturnsAsync(listings);

        _addon.Setup(a => a.GetRetainerSellListRowTexts())
            .ReturnsAsync(new List<string> { "Item1 | 99" });
        _addon.Setup(a => a.FindContextMenuEntry(It.IsAny<string[]>())).ReturnsAsync(0);

        var result = await Build(Matches(1, 68), Rules(1, "Item1", stackSize: 99))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.RightClickRetainerListing(It.IsAny<int>()), Times.Never);
        _cache.Verify(c => c.Invalidate(RetainerId), Times.Once);
        _cache.Verify(c => c.Update(It.IsAny<ulong>(), It.IsAny<IReadOnlyList<RetainerListing>>(), It.IsAny<ulong>()), Times.Never);
    }

    [Fact]
    public async Task MenuEntryMissing_ClosesMenuAndNeverClicksIndex2()
    {
        // Same setup as scenario 2, but FindContextMenuEntry cannot locate the Return entry.
        var listings = new List<RetainerListing> { new(0, 1, 31) };
        _reader.Setup(r => r.ReadListingsAsync()).ReturnsAsync(listings);

        _addon.Setup(a => a.GetRetainerSellListRowTexts())
            .ReturnsAsync(new List<string> { "Item1 | 31" });
        _addon.Setup(a => a.FindContextMenuEntry(It.IsAny<string[]>())).ReturnsAsync(-1);

        var result = await Build(Matches(1, 68), Rules(1, "Item1", stackSize: 99))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.CloseAddon("ContextMenu"), Times.Once);
        _addon.Verify(a => a.ClickAddonButton("ContextMenu", It.IsAny<int>()), Times.Never);
        _cache.Verify(c => c.Update(It.IsAny<ulong>(), It.IsAny<IReadOnlyList<RetainerListing>>(), It.IsAny<ulong>()), Times.Never);
    }

    [Fact]
    public async Task NoImprovement_LeavesPartialListed()
    {
        // Slot 0, item 1, qty 31; inventory only has 5 -> not enough to form a new full
        // stack, and only one partial listing exists so the fewer-listings rule can't apply
        // either -> planner has nothing to return.
        var listings = new List<RetainerListing> { new(0, 1, 31) };
        _reader.Setup(r => r.ReadListingsAsync()).ReturnsAsync(listings);

        var result = await Build(Matches(1, 5), Rules(1, "Item1", stackSize: 99))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.RightClickRetainerListing(It.IsAny<int>()), Times.Never);
        _cache.Verify(c => c.Update(RetainerId, listings, RetainerGil), Times.Once);
    }

    [Fact]
    public async Task TwoPartials_ProcessedBottomRowFirst_WithMidLoopReread()
    {
        // Slot 0 (qty 59) and slot 1 (qty 6) both undersized for item 1, StackSize 99.
        // Inventory is empty so no new-full-stack forms, but AllowPartial's fewer-listings
        // rule fires: 59+6 collapses to 1 potential stack < 2 existing listings.
        // RetainerSellList displays slot 0 as row 0 and slot 1 as row 1, so the command must
        // act on slot 1 (bottom display row) first. Each loop iteration re-reads listings
        // before mapping/clicking, so the second iteration sees the post-return state.
        var initial = new List<RetainerListing> { new(0, 1, 59), new(1, 1, 6) };
        var afterFirstReturn = new List<RetainerListing> { new(0, 1, 59) }; // slot 1 returned
        var verification = new List<RetainerListing>();

        _reader.SetupSequence(r => r.ReadListingsAsync())
            .ReturnsAsync(initial)          // initial read, reused for slot 1 (bottom row)
            .ReturnsAsync(afterFirstReturn) // re-read before processing slot 0 (now row 0)
            .ReturnsAsync(verification);    // final verification read

        _addon.SetupSequence(a => a.GetRetainerSellListRowTexts())
            .ReturnsAsync(new List<string> { "Item1 | 59", "Item1 | 6" }) // row 1 -> slot 1
            .ReturnsAsync(new List<string> { "Item1 | 59" });             // row 0 -> slot 0
        _addon.Setup(a => a.FindContextMenuEntry(It.IsAny<string[]>())).ReturnsAsync(0);

        var scrollRows = new List<int>();
        _addon.Setup(a => a.ScrollRetainerSellListTo(It.IsAny<int>()))
            .Callback<int>(row => scrollRows.Add(row))
            .ReturnsAsync(true);
        var rightClickRows = new List<int>();
        _addon.Setup(a => a.RightClickRetainerListing(It.IsAny<int>()))
            .Callback<int>(row => rightClickRows.Add(row))
            .ReturnsAsync(true);

        var rules = Rules(1, "Item1", stackSize: 99, allowPartial: true);
        var result = await Build(new List<InventoryMatch>(), rules)
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.Equal(BottomFirstRowOrder, scrollRows);
        Assert.Equal(BottomFirstRowOrder, rightClickRows);
        _addon.Verify(a => a.RightClickRetainerListing(It.IsAny<int>()), Times.Exactly(2));
        _cache.Verify(c => c.Update(RetainerId, verification, RetainerGil), Times.Once);
    }

    [Fact]
    public async Task TwoPartials_SlotVanishesMidLoop_SkipsThatActionButCompletes()
    {
        // Same two-partial setup as above, but by the time the loop re-reads listings for the
        // second action (slot 0), that slot is gone (sold out from under us). The action must
        // be skipped with no scroll/click, while the loop still completes successfully.
        var initial = new List<RetainerListing> { new(0, 1, 59), new(1, 1, 6) };
        var slotGone = new List<RetainerListing>();
        var verification = new List<RetainerListing>();

        _reader.SetupSequence(r => r.ReadListingsAsync())
            .ReturnsAsync(initial)   // initial read, reused for slot 1 (bottom row) -> present
            .ReturnsAsync(slotGone)  // re-read before processing slot 0 -> vanished
            .ReturnsAsync(verification);

        _addon.Setup(a => a.GetRetainerSellListRowTexts())
            .ReturnsAsync(new List<string> { "Item1 | 59", "Item1 | 6" });
        _addon.Setup(a => a.FindContextMenuEntry(It.IsAny<string[]>())).ReturnsAsync(0);

        var rules = Rules(1, "Item1", stackSize: 99, allowPartial: true);
        var result = await Build(new List<InventoryMatch>(), rules)
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.RightClickRetainerListing(It.IsAny<int>()), Times.Once);
        _cache.Verify(c => c.Update(RetainerId, verification, RetainerGil), Times.Once);
    }

    [Fact]
    public async Task SelectYesnoAppearsAfterReturnClick_ClicksYesToConfirm()
    {
        // Same qualifying-partial setup as QualifyingPartial_IsReturned_WhenRowVerifies, but
        // SelectYesno is closed on the pre-click stale-addon check and only appears after the
        // ContextMenu's Return entry is clicked -- the command must confirm it.
        var listings = new List<RetainerListing> { new(0, 1, 31) };
        var emptyAfterReturn = new List<RetainerListing>();
        _reader.SetupSequence(r => r.ReadListingsAsync())
            .ReturnsAsync(listings)          // initial read, reused for the first action
            .ReturnsAsync(emptyAfterReturn); // verification re-read

        _addon.Setup(a => a.GetRetainerSellListRowTexts())
            .ReturnsAsync(new List<string> { "Item1 | 31" });
        _addon.Setup(a => a.FindContextMenuEntry(It.IsAny<string[]>())).ReturnsAsync(0);
        _addon.SetupSequence(a => a.IsAddonVisible("SelectYesno"))
            .ReturnsAsync(false) // pre-click stale-addon check, before scrolling/clicking
            .ReturnsAsync(true); // appears after clicking the ContextMenu's Return entry

        var result = await Build(Matches(1, 68), Rules(1, "Item1", stackSize: 99))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.ClickAddonButton("SelectYesno", 0), Times.Once);
        _cache.Verify(c => c.Update(RetainerId, emptyAfterReturn, RetainerGil), Times.Once);
    }
}
