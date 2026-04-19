using MarketDumper.Automation;
using MarketDumper.Commands;
using MarketDumper.Models;
using Moq;
using Xunit;

namespace MarketDumper.Tests;

public class JobPlannerTests
{
    private readonly Mock<ICommandFactory> _factory;
    private readonly JobPlanner _planner;

    public JobPlannerTests()
    {
        _factory = new Mock<ICommandFactory>();

        _factory.Setup(f => f.CreateSelectRetainer(It.IsAny<int>()))
            .Returns((int idx) => MockCommand(CommandType.SelectRetainer, $"Select retainer {idx}"));

        _factory.Setup(f => f.CreateOpenSellMenu())
            .Returns(MockCommand(CommandType.OpenSellMenu, "Open sell menu"));

        _factory.Setup(f => f.CreateSelectItem(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((int c, int s, int sz) => MockCommand(CommandType.SelectItem, $"Select item {c}:{s} x{sz}"));

        _factory.Setup(f => f.CreateFetchMarketPrice(It.IsAny<uint>()))
            .Returns((uint id) => MockCommand(CommandType.FetchMarketPrice, $"Fetch price for {id}"));

        _factory.Setup(f => f.CreateSetPrice())
            .Returns(MockCommand(CommandType.SetPrice, "Set price"));

        _factory.Setup(f => f.CreateConfirmListing())
            .Returns(MockCommand(CommandType.ConfirmListing, "Confirm listing"));

        _factory.Setup(f => f.CreateCloseRetainer())
            .Returns(MockCommand(CommandType.CloseRetainer, "Close retainer"));

        _planner = new JobPlanner(_factory.Object);
    }

    private static ICommand MockCommand(CommandType type, string description)
    {
        var cmd = new Mock<ICommand>();
        cmd.Setup(c => c.Type).Returns(type);
        cmd.Setup(c => c.Description).Returns(description);
        return cmd.Object;
    }

    // 1 retainer + 1 item = SelectRetainer + OpenSellMenu + (SelectItem + Fetch + SetPrice + Confirm) + CloseRetainer = 7
    [Fact]
    public void GenerateCommands_SingleItemSingleStack_ProducesCorrectSequence()
    {
        var matches = new List<InventoryMatch>
        {
            new(1001, 99, new List<InventorySlot> { new(0, 5, 99, false) })
        };
        var rules = new List<SellRule> { new() { ItemId = 1001, StackSize = 99, Enabled = true } };

        var commands = _planner.GenerateCommands(matches, rules, new[] { 20 });

        Assert.Equal(7, commands.Count);
        Assert.Equal(CommandType.SelectRetainer,  commands[0].Type);
        Assert.Equal(CommandType.OpenSellMenu,    commands[1].Type);
        Assert.Equal(CommandType.SelectItem,      commands[2].Type);
        Assert.Equal(CommandType.FetchMarketPrice,commands[3].Type);
        Assert.Equal(CommandType.SetPrice,        commands[4].Type);
        Assert.Equal(CommandType.ConfirmListing,  commands[5].Type);
        Assert.Equal(CommandType.CloseRetainer,   commands[6].Type);
    }

    // 200 items at stack 99 → 3 listings (99+99+2); per retainer: header(2) + 3×4 + footer(1) = 15
    [Fact]
    public void GenerateCommands_MultipleStacks_RepeatsListingCommands()
    {
        var matches = new List<InventoryMatch>
        {
            new(1002, 200, new List<InventorySlot> { new(0, 0, 200, false) })
        };
        var rules = new List<SellRule> { new() { ItemId = 1002, StackSize = 99, Enabled = true } };

        var commands = _planner.GenerateCommands(matches, rules, new[] { 20 });

        Assert.Equal(15, commands.Count);
        Assert.Equal(CommandType.SelectRetainer, commands[0].Type);
        Assert.Equal(CommandType.OpenSellMenu,   commands[1].Type);
        for (var i = 0; i < 3; i++)
        {
            var base_ = 2 + i * 4;
            Assert.Equal(CommandType.SelectItem,       commands[base_].Type);
            Assert.Equal(CommandType.FetchMarketPrice, commands[base_ + 1].Type);
            Assert.Equal(CommandType.SetPrice,         commands[base_ + 2].Type);
            Assert.Equal(CommandType.ConfirmListing,   commands[base_ + 3].Type);
        }
        Assert.Equal(CommandType.CloseRetainer, commands[14].Type);
    }

    [Fact]
    public void GenerateCommands_RetainerSlotsFull_MovesToNextRetainer()
    {
        var matches = new List<InventoryMatch>
        {
            new(1003, 300, new List<InventorySlot> { new(0, 0, 300, false) })
        };
        var rules = new List<SellRule> { new() { ItemId = 1003, StackSize = 99, Enabled = true } };

        var commands = _planner.GenerateCommands(matches, rules, new[] { 1, 20 });

        var selectRetainerCmds = commands.Where(c => c.Type == CommandType.SelectRetainer).ToList();
        Assert.Equal(2, selectRetainerCmds.Count);
    }

    [Fact]
    public void GenerateCommands_NoFreeSlots_ReturnsEmpty()
    {
        var matches = new List<InventoryMatch>
        {
            new(1004, 99, new List<InventorySlot> { new(0, 0, 99, false) })
        };
        var rules = new List<SellRule> { new() { ItemId = 1004, StackSize = 99, Enabled = true } };

        var commands = _planner.GenerateCommands(matches, rules, new[] { 0, 0, 0 });

        Assert.Empty(commands);
    }

    [Fact]
    public void GenerateCommands_NoMatches_ReturnsEmpty()
    {
        var matches = new List<InventoryMatch>();
        var rules   = new List<SellRule> { new() { ItemId = 1005, StackSize = 99, Enabled = true } };

        var commands = _planner.GenerateCommands(matches, rules, new[] { 20 });

        Assert.Empty(commands);
    }

    [Fact]
    public void GenerateCommands_MoreStacksThanTotalSlots_OnlyFillsAvailable()
    {
        var matches = new List<InventoryMatch>
        {
            new(1006, 9999, new List<InventorySlot> { new(0, 0, 9999, false) })
        };
        var rules = new List<SellRule> { new() { ItemId = 1006, StackSize = 99, Enabled = true } };

        var commands = _planner.GenerateCommands(matches, rules, new[] { 1, 1 });

        Assert.Equal(2, commands.Count(c => c.Type == CommandType.ConfirmListing));
    }

    // --- SlotsUsedPerRetainer tracking ---

    [Fact]
    public void SlotsUsedPerRetainer_SingleRetainer_TracksCorrectly()
    {
        var matches = new List<InventoryMatch>
        {
            new(2001, 297, new List<InventorySlot> { new(0, 0, 297, false) })
        };
        var rules = new List<SellRule> { new() { ItemId = 2001, StackSize = 99, Enabled = true } };

        _planner.GenerateCommands(matches, rules, new[] { 20 });

        // 297 / 99 = 3 full stacks
        Assert.Equal(new[] { 3 }, _planner.SlotsUsedPerRetainer);
    }

    [Fact]
    public void SlotsUsedPerRetainer_TwoRetainers_TracksEachSeparately()
    {
        // 5 stacks total; retainer 0 has 2 slots, retainer 1 has 20 slots
        var matches = new List<InventoryMatch>
        {
            new(2002, 495, new List<InventorySlot> { new(0, 0, 495, false) })
        };
        var rules = new List<SellRule> { new() { ItemId = 2002, StackSize = 99, Enabled = true } };

        _planner.GenerateCommands(matches, rules, new[] { 2, 20 });

        Assert.Equal(2, _planner.SlotsUsedPerRetainer[0]);
        Assert.Equal(3, _planner.SlotsUsedPerRetainer[1]);
    }

    [Fact]
    public void SlotsUsedPerRetainer_SkippedRetainer_ReportsZero()
    {
        var matches = new List<InventoryMatch>
        {
            new(2003, 99, new List<InventorySlot> { new(0, 0, 99, false) })
        };
        var rules = new List<SellRule> { new() { ItemId = 2003, StackSize = 99, Enabled = true } };

        // Only retainer 1 has slots; retainer 0 is full
        _planner.GenerateCommands(matches, rules, new[] { 0, 20 });

        Assert.Equal(0, _planner.SlotsUsedPerRetainer[0]);
        Assert.Equal(1, _planner.SlotsUsedPerRetainer[1]);
    }

    // --- Multi-pass stale slot regression ---

    // Reproduces the bug: after pass 1 fills both retainers, a second GenerateCommands call
    // with the updated (subtracted) freeSlotsPerRetainer must produce zero commands.
    [Fact]
    public void MultiPass_AfterSubtractingUsedSlots_ProducesNoCommandsWhenFull()
    {
        var matches = new List<InventoryMatch>
        {
            new(3001, 99, new List<InventorySlot> { new(0, 0, 99, false) })
        };
        var rules = new List<SellRule> { new() { ItemId = 3001, StackSize = 99, Enabled = true } };
        var freeSlots = new[] { 1, 0 };

        // Pass 1: retainer 0 has 1 slot → uses it
        _planner.GenerateCommands(matches, rules, freeSlots);

        // Simulate stale read returning the original counts, then subtract (the fix)
        var updatedSlots = new[] {
            Math.Max(0, freeSlots[0] - _planner.SlotsUsedPerRetainer[0]),
            Math.Max(0, freeSlots[1] - _planner.SlotsUsedPerRetainer[1]),
        };

        // Pass 2 with corrected slots → nothing to list
        var pass2 = _planner.GenerateCommands(matches, rules, updatedSlots);

        Assert.Empty(pass2);
    }

    // If a stale re-read incorrectly returns the original slot counts, pass 2
    // would generate commands for the already-full retainer — this is the bug.
    [Fact]
    public void MultiPass_StaleSlotRead_WouldIncorrectlyGenerateCommands()
    {
        var matches = new List<InventoryMatch>
        {
            new(3002, 99, new List<InventorySlot> { new(0, 0, 99, false) })
        };
        var rules = new List<SellRule> { new() { ItemId = 3002, StackSize = 99, Enabled = true } };
        var freeSlots = new[] { 1, 0 };

        // Pass 1
        _planner.GenerateCommands(matches, rules, freeSlots);

        // Bug scenario: stale re-read returns the same slot counts as before pass 1
        var staleSlots = new[] { 1, 0 };
        var pass2 = _planner.GenerateCommands(matches, rules, staleSlots);

        // With stale data, planner would wrongly generate 7 commands for retainer 0 again
        Assert.NotEmpty(pass2);  // documents the incorrect behavior with stale data
    }
}
