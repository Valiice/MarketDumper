using System;
using System.Collections.Generic;
using System.Linq;
using MarketDumper.Models;

namespace MarketDumper.Automation;

public enum RetainerPlanKind { Skip, Refresh, Return }

public record ReturnAction(uint ItemId, int Quantity, int SlotIndex);

public record RetainerPlan(RetainerPlanKind Kind, IReadOnlyList<ReturnAction> Actions)
{
    public static readonly RetainerPlan Skip = new(RetainerPlanKind.Skip, Array.Empty<ReturnAction>());
    public static readonly RetainerPlan Refresh = new(RetainerPlanKind.Refresh, Array.Empty<ReturnAction>());
}

public class ConsolidationPlanner
{
    public RetainerPlan PlanRetainer(
        IReadOnlyList<RetainerListing>? knownListings,
        IReadOnlyList<InventoryMatch> inventory,
        IReadOnlyList<SellRule> rules,
        int freeInventorySlots)
    {
        if (knownListings == null)
            return RetainerPlan.Refresh;

        var inventoryByItem = inventory.ToDictionary(m => m.ItemId, m => m.TotalQuantity);
        var actions = new List<ReturnAction>();
        var slotsLeft = freeInventorySlots;

        foreach (var rule in rules.Where(r => r.Enabled))
        {
            var partials = knownListings
                .Where(l => l.ItemId == rule.ItemId && l.Quantity > 0 && l.Quantity < rule.StackSize)
                .ToList();
            if (partials.Count == 0)
                continue;

            var inventoryQty = inventoryByItem.GetValueOrDefault(rule.ItemId);
            var returnedQty = partials.Sum(l => l.Quantity);

            // A new full stack counts only when inventory contributes items.
            var formsNewFullStack = inventoryQty > 0
                && (inventoryQty + returnedQty) / rule.StackSize > inventoryQty / rule.StackSize;

            // The inventory remainder would be listed as its own partial, so it
            // counts as one more merge candidate.
            var inventoryRemainder = rule.AllowPartial ? inventoryQty % rule.StackSize : 0;
            var mergeGroupCount = partials.Count + (inventoryRemainder > 0 ? 1 : 0);
            var mergesIntoFewerListings = rule.AllowPartial && mergeGroupCount >= 2
                && (returnedQty + inventoryRemainder + rule.StackSize - 1) / rule.StackSize < mergeGroupCount;

            if (!formsNewFullStack && !mergesIntoFewerListings)
                continue;
            if (partials.Count > slotsLeft)
                continue; // not enough free inventory slots to hold the whole returned group

            slotsLeft -= partials.Count;
            actions.AddRange(partials.Select(l => new ReturnAction(l.ItemId, l.Quantity, l.SlotIndex)));
        }

        return actions.Count == 0 ? RetainerPlan.Skip : new RetainerPlan(RetainerPlanKind.Return, actions);
    }
}
