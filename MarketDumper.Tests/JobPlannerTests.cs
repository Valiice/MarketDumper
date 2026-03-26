using System.Collections.Generic;
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
            .Returns((int idx) =>
            {
                var cmd = new Mock<ICommand>();
                cmd.Setup(c => c.Type).Returns(CommandType.SelectRetainer);
                cmd.Setup(c => c.Description).Returns($"Select retainer {idx}");
                return cmd.Object;
            });

        _factory.Setup(f => f.CreateOpenSellMenu())
            .Returns(() =>
            {
                var cmd = new Mock<ICommand>();
                cmd.Setup(c => c.Type).Returns(CommandType.OpenSellMenu);
                cmd.Setup(c => c.Description).Returns("Open sell menu");
                return cmd.Object;
            });

        _factory.Setup(f => f.CreateSelectItem(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((int container, int slot, int stack) =>
            {
                var cmd = new Mock<ICommand>();
                cmd.Setup(c => c.Type).Returns(CommandType.SelectItem);
                cmd.Setup(c => c.Description).Returns($"Select item {container}:{slot} x{stack}");
                return cmd.Object;
            });

        _factory.Setup(f => f.CreateFetchMarketPrice(It.IsAny<uint>()))
            .Returns((uint itemId) =>
            {
                var cmd = new Mock<ICommand>();
                cmd.Setup(c => c.Type).Returns(CommandType.FetchMarketPrice);
                cmd.Setup(c => c.Description).Returns($"Fetch price for {itemId}");
                return cmd.Object;
            });

        _factory.Setup(f => f.CreateSetPrice())
            .Returns(() =>
            {
                var cmd = new Mock<ICommand>();
                cmd.Setup(c => c.Type).Returns(CommandType.SetPrice);
                cmd.Setup(c => c.Description).Returns("Set price");
                return cmd.Object;
            });

        _factory.Setup(f => f.CreateConfirmListing())
            .Returns(() =>
            {
                var cmd = new Mock<ICommand>();
                cmd.Setup(c => c.Type).Returns(CommandType.ConfirmListing);
                cmd.Setup(c => c.Description).Returns("Confirm listing");
                return cmd.Object;
            });

        _planner = new JobPlanner(_factory.Object);
    }

    [Fact]
    public void GenerateCommands_SingleItemSingleStack_ProducesCorrectSequence()
    {
        var matches = new List<InventoryMatch>
        {
            new(1001, 99, new List<InventorySlot> { new(0, 5, 99, false) })
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 1001, StackSize = 99, Enabled = true }
        };
        var freeSlots = new[] { 20 };

        var commands = _planner.GenerateCommands(matches, rules, freeSlots);

        Assert.Equal(6, commands.Count);
        Assert.Equal(CommandType.SelectRetainer, commands[0].Type);
        Assert.Equal(CommandType.OpenSellMenu, commands[1].Type);
        Assert.Equal(CommandType.SelectItem, commands[2].Type);
        Assert.Equal(CommandType.FetchMarketPrice, commands[3].Type);
        Assert.Equal(CommandType.SetPrice, commands[4].Type);
        Assert.Equal(CommandType.ConfirmListing, commands[5].Type);
    }

    [Fact]
    public void GenerateCommands_MultipleStacks_RepeatsListingCommands()
    {
        // 200 items at stack size 99 → 3 stacks (99 + 99 + 2)
        var matches = new List<InventoryMatch>
        {
            new(1002, 200, new List<InventorySlot> { new(0, 0, 200, false) })
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 1002, StackSize = 99, Enabled = true }
        };
        var freeSlots = new[] { 20 };

        var commands = _planner.GenerateCommands(matches, rules, freeSlots);

        // 2 header (SelectRetainer + OpenSellMenu) + 3 listings × 4 commands = 14
        Assert.Equal(14, commands.Count);
        Assert.Equal(CommandType.SelectRetainer, commands[0].Type);
        Assert.Equal(CommandType.OpenSellMenu, commands[1].Type);
        for (var i = 0; i < 3; i++)
        {
            var baseIdx = 2 + i * 4;
            Assert.Equal(CommandType.SelectItem, commands[baseIdx].Type);
            Assert.Equal(CommandType.FetchMarketPrice, commands[baseIdx + 1].Type);
            Assert.Equal(CommandType.SetPrice, commands[baseIdx + 2].Type);
            Assert.Equal(CommandType.ConfirmListing, commands[baseIdx + 3].Type);
        }
    }

    [Fact]
    public void GenerateCommands_RetainerSlotsFull_MovesToNextRetainer()
    {
        // 300 items at stack size 99 → 4 stacks; retainer 0 has 1 slot, retainer 1 has 20
        var matches = new List<InventoryMatch>
        {
            new(1003, 300, new List<InventorySlot> { new(0, 0, 300, false) })
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 1003, StackSize = 99, Enabled = true }
        };
        var freeSlots = new[] { 1, 20 };

        var commands = _planner.GenerateCommands(matches, rules, freeSlots);

        var selectRetainerCommands = commands.FindAll(c => c.Type == CommandType.SelectRetainer);
        Assert.Equal(2, selectRetainerCommands.Count);
    }

    [Fact]
    public void GenerateCommands_NoFreeSlots_ReturnsEmpty()
    {
        var matches = new List<InventoryMatch>
        {
            new(1004, 99, new List<InventorySlot> { new(0, 0, 99, false) })
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 1004, StackSize = 99, Enabled = true }
        };
        var freeSlots = new[] { 0, 0, 0 };

        var commands = _planner.GenerateCommands(matches, rules, freeSlots);

        Assert.Empty(commands);
    }

    [Fact]
    public void GenerateCommands_NoMatches_ReturnsEmpty()
    {
        var matches = new List<InventoryMatch>();
        var rules = new List<SellRule>
        {
            new() { ItemId = 1005, StackSize = 99, Enabled = true }
        };
        var freeSlots = new[] { 20 };

        var commands = _planner.GenerateCommands(matches, rules, freeSlots);

        Assert.Empty(commands);
    }

    [Fact]
    public void GenerateCommands_MoreStacksThanTotalSlots_OnlyFillsAvailable()
    {
        // 9999 items at stack size 99 → many stacks; only 2 retainers with 1 slot each
        var matches = new List<InventoryMatch>
        {
            new(1006, 9999, new List<InventorySlot> { new(0, 0, 9999, false) })
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 1006, StackSize = 99, Enabled = true }
        };
        var freeSlots = new[] { 1, 1 };

        var commands = _planner.GenerateCommands(matches, rules, freeSlots);

        var confirmCount = commands.FindAll(c => c.Type == CommandType.ConfirmListing).Count;
        Assert.Equal(2, confirmCount);
    }
}
