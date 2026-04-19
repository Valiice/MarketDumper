using MarketDumper.Models;
using MarketDumper.Services;
using Xunit;

namespace MarketDumper.Tests;

public class SellRuleManagerTests
{
    private static SellRuleManager CreateManager(List<SellRule>? rules = null)
    {
        rules ??= new List<SellRule>();
        return new SellRuleManager(rules, () => { });
    }

    [Fact]
    public void AddRule_NewItem_ReturnsTrue()
    {
        var manager = CreateManager();
        var rule = new SellRule { ItemId = 1, ItemName = "Mythrite Ore", StackSize = 99 };
        var result = manager.AddRule(rule);
        Assert.True(result);
        Assert.Single(manager.GetAllRules());
    }

    [Fact]
    public void AddRule_DuplicateItemId_ReturnsFalse()
    {
        var manager = CreateManager();
        var rule1 = new SellRule { ItemId = 1, ItemName = "Mythrite Ore", StackSize = 99 };
        var rule2 = new SellRule { ItemId = 1, ItemName = "Mythrite Ore", StackSize = 50 };
        manager.AddRule(rule1);
        var result = manager.AddRule(rule2);
        Assert.False(result);
        Assert.Single(manager.GetAllRules());
    }

    [Fact]
    public void RemoveRule_ExistingItem_ReturnsTrue()
    {
        var manager = CreateManager();
        manager.AddRule(new SellRule { ItemId = 1, ItemName = "Test" });
        var result = manager.RemoveRule(1);
        Assert.True(result);
        Assert.Empty(manager.GetAllRules());
    }

    [Fact]
    public void RemoveRule_NonExistent_ReturnsFalse()
    {
        var manager = CreateManager();
        var result = manager.RemoveRule(999);
        Assert.False(result);
    }

    [Fact]
    public void GetEnabledRules_FiltersDisabled()
    {
        var manager = CreateManager();
        manager.AddRule(new SellRule { ItemId = 1, ItemName = "Enabled", Enabled = true });
        manager.AddRule(new SellRule { ItemId = 2, ItemName = "Disabled", Enabled = false });
        var enabled = manager.GetEnabledRules();
        Assert.Single(enabled);
        Assert.Equal((uint)1, enabled[0].ItemId);
    }

    [Fact]
    public void UpdateRule_ExistingItem_UpdatesFields()
    {
        var manager = CreateManager();
        manager.AddRule(new SellRule { ItemId = 1, ItemName = "Test", StackSize = 99 });
        var updated = new SellRule { ItemId = 1, ItemName = "Test", StackSize = 50 };
        var result = manager.UpdateRule(updated);
        Assert.True(result);
        Assert.Equal(50, manager.GetAllRules()[0].StackSize);
    }

    [Fact]
    public void UpdateRule_NonExistent_ReturnsFalse()
    {
        var manager = CreateManager();
        var result = manager.UpdateRule(new SellRule { ItemId = 999 });
        Assert.False(result);
    }

    [Fact]
    public void HasRule_ExistingItem_ReturnsTrue()
    {
        var manager = CreateManager();
        manager.AddRule(new SellRule { ItemId = 1, ItemName = "Test" });
        Assert.True(manager.HasRule(1));
    }

    [Fact]
    public void HasRule_NonExistent_ReturnsFalse()
    {
        var manager = CreateManager();
        Assert.False(manager.HasRule(999));
    }

    [Fact]
    public void Constructor_LoadsExistingRules()
    {
        var existing = new List<SellRule>
        {
            new() { ItemId = 1, ItemName = "Pre-existing" }
        };
        var manager = CreateManager(existing);
        Assert.Single(manager.GetAllRules());
        Assert.Equal("Pre-existing", manager.GetAllRules()[0].ItemName);
    }
}
