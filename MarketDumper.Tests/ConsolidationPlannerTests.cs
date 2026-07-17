using MarketDumper.Automation;
using MarketDumper.Models;
using Xunit;

namespace MarketDumper.Tests;

public class ConsolidationPlannerTests
{
    private readonly ConsolidationPlanner _planner = new();

    private static SellRule Rule(uint itemId, int stackSize = 99, bool allowPartial = false, bool enabled = true) =>
        new() { ItemId = itemId, ItemName = $"Item{itemId}", StackSize = stackSize, AllowPartial = allowPartial, Enabled = enabled };

    private static InventoryMatch Inv(uint itemId, int qty) =>
        new(itemId, qty, new List<InventorySlot> { new(0, 0, qty, false) });

    [Fact]
    public void NullListings_MeansRefresh()
    {
        var plan = _planner.PlanRetainer(null, new List<InventoryMatch>(), new List<SellRule> { Rule(1) }, 20);
        Assert.Equal(RetainerPlanKind.Refresh, plan.Kind);
    }

    [Fact]
    public void FullStacksOnly_MeansSkip()
    {
        var listings = new List<RetainerListing> { new(0, 1, 99), new(1, 1, 99) };
        var plan = _planner.PlanRetainer(listings, new List<InventoryMatch> { Inv(1, 500) }, new List<SellRule> { Rule(1) }, 20);
        Assert.Equal(RetainerPlanKind.Skip, plan.Kind);
    }

    [Fact]
    public void PartialReturned_WhenInventoryCompletesAFullStack()
    {
        var listings = new List<RetainerListing> { new(0, 1, 31) };
        var plan = _planner.PlanRetainer(listings, new List<InventoryMatch> { Inv(1, 68) }, new List<SellRule> { Rule(1) }, 20);
        Assert.Equal(RetainerPlanKind.Return, plan.Kind);
        var action = Assert.Single(plan.Actions);
        Assert.Equal(31, action.Quantity);
        Assert.Equal(0, action.SlotIndex);
    }

    [Fact]
    public void PartialLeftListed_WhenInventoryTooSmallToCompleteAStack()
    {
        var listings = new List<RetainerListing> { new(0, 1, 31) };
        var plan = _planner.PlanRetainer(listings, new List<InventoryMatch> { Inv(1, 5) }, new List<SellRule> { Rule(1) }, 20);
        Assert.Equal(RetainerPlanKind.Skip, plan.Kind); // 36 < 99: returning helps nothing
    }

    [Fact]
    public void TwoPartialsMergedIntoFewer_WhenAllowPartial()
    {
        var listings = new List<RetainerListing> { new(0, 1, 59), new(1, 1, 6) };
        var plan = _planner.PlanRetainer(listings, new List<InventoryMatch>(), new List<SellRule> { Rule(1, allowPartial: true) }, 20);
        Assert.Equal(RetainerPlanKind.Return, plan.Kind);
        Assert.Equal(2, plan.Actions.Count); // 65 relists as one
    }

    [Fact]
    public void TwoPartials_NotReturned_WhenPartialsDisallowedAndNoFullStackForms()
    {
        var listings = new List<RetainerListing> { new(0, 1, 59), new(1, 1, 6) };
        var plan = _planner.PlanRetainer(listings, new List<InventoryMatch>(), new List<SellRule> { Rule(1, allowPartial: false) }, 20);
        Assert.Equal(RetainerPlanKind.Skip, plan.Kind);
    }

    [Fact]
    public void TwoBigPartials_NotReturned_WhenMergeWouldNotReduceListingCount()
    {
        var listings = new List<RetainerListing> { new(0, 1, 98), new(1, 1, 98) };
        var plan = _planner.PlanRetainer(listings, new List<InventoryMatch>(), new List<SellRule> { Rule(1, allowPartial: true) }, 20);
        Assert.Equal(RetainerPlanKind.Skip, plan.Kind); // 196 -> still 2 listings
    }

    [Fact]
    public void ItemGroupDropped_WhenNotEnoughFreeInventorySlots()
    {
        var listings = new List<RetainerListing> { new(0, 1, 59), new(1, 1, 6) };
        var plan = _planner.PlanRetainer(listings, new List<InventoryMatch>(), new List<SellRule> { Rule(1, allowPartial: true) }, 1);
        Assert.Equal(RetainerPlanKind.Skip, plan.Kind); // needs 2 slots, only 1 free
    }

    [Fact]
    public void DisabledRulesAndUnruledItems_AreIgnored()
    {
        var listings = new List<RetainerListing> { new(0, 1, 31), new(1, 2, 31) };
        var rules = new List<SellRule> { Rule(1, enabled: false) }; // item 2 has no rule at all
        var plan = _planner.PlanRetainer(listings, new List<InventoryMatch> { Inv(1, 68), Inv(2, 68) }, rules, 20);
        Assert.Equal(RetainerPlanKind.Skip, plan.Kind);
    }

    [Fact]
    public void FullStackNeverReturned_EvenWhenPartialsOfSameItemAre()
    {
        var listings = new List<RetainerListing> { new(0, 1, 99), new(1, 1, 31) };
        var plan = _planner.PlanRetainer(listings, new List<InventoryMatch> { Inv(1, 68) }, new List<SellRule> { Rule(1) }, 20);
        Assert.Equal(RetainerPlanKind.Return, plan.Kind);
        var action = Assert.Single(plan.Actions);
        Assert.Equal(1, action.SlotIndex); // only the 31, never the 99
    }

    [Fact]
    public void PartialReturned_WhenFullStackFormsWithLeftoverRemainder()
    {
        var listings = new List<RetainerListing> { new(0, 1, 31) };
        var plan = _planner.PlanRetainer(listings, new List<InventoryMatch> { Inv(1, 70) }, new List<SellRule> { Rule(1) }, 20);
        Assert.Equal(RetainerPlanKind.Return, plan.Kind); // 70+31 = one new full 99 + 2 leftover in inventory
        Assert.Single(plan.Actions);
    }

    // Inventory 302 = 3 full stacks + remainder 5. Listing would put that 5 next to the
    // already-listed 61 as a second partial; returning the 61 lets them relist as one 66.
    [Fact]
    public void PartialReturned_WhenInventoryRemainderWouldOtherwiseListSeparately()
    {
        var listings = new List<RetainerListing> { new(0, 1, 61) };
        var plan = _planner.PlanRetainer(listings, new List<InventoryMatch> { Inv(1, 302) }, new List<SellRule> { Rule(1, allowPartial: true) }, 20);
        Assert.Equal(RetainerPlanKind.Return, plan.Kind);
        var action = Assert.Single(plan.Actions);
        Assert.Equal(61, action.Quantity);
    }

    // Same shape but partials disallowed: the remainder stays in inventory instead of being
    // listed, so pulling the 61 would achieve nothing — it stays listed and sellable.
    [Fact]
    public void PartialLeftListed_WhenRemainderExistsButPartialsDisallowed()
    {
        var listings = new List<RetainerListing> { new(0, 1, 61) };
        var plan = _planner.PlanRetainer(listings, new List<InventoryMatch> { Inv(1, 302) }, new List<SellRule> { Rule(1, allowPartial: false) }, 20);
        Assert.Equal(RetainerPlanKind.Skip, plan.Kind);
    }
}
