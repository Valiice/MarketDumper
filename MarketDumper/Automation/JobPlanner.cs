using System;
using System.Collections.Generic;
using System.Linq;
using MarketDumper.Commands;
using MarketDumper.Models;

namespace MarketDumper.Automation;

public class JobPlanner
{
    private readonly ICommandFactory _commandFactory;

    public JobPlanner(ICommandFactory commandFactory)
    {
        _commandFactory = commandFactory;
    }

    public bool SkippedPartials { get; private set; }
    public int[] SlotsUsedPerRetainer { get; private set; } = [];

    public List<ICommand> GenerateCommands(
        List<InventoryMatch> matches,
        List<SellRule> rules,
        int[] freeSlotsPerRetainer)
    {
        var commands = new List<ICommand>();
        SkippedPartials = false;
        SlotsUsedPerRetainer = new int[freeSlotsPerRetainer.Length];

        if (matches.Count == 0 || freeSlotsPerRetainer.All(s => s <= 0))
            return commands;

        var rulesByItemId = rules.ToDictionary(r => r.ItemId);

        var pendingListings = new Queue<(uint ItemId, int StackSize, InventorySlot Slot)>();
        foreach (var match in matches)
        {
            if (!rulesByItemId.TryGetValue(match.ItemId, out var rule))
                continue;

            var remaining = match.TotalQuantity;
            var slotIndex = 0;
            var slotRemaining = match.Slots.Count > 0 ? match.Slots[0].Quantity : 0;

            while (remaining > 0 && slotIndex < match.Slots.Count)
            {
                if (slotRemaining <= 0)
                {
                    slotIndex++;
                    if (slotIndex < match.Slots.Count)
                        slotRemaining = match.Slots[slotIndex].Quantity;
                    continue;
                }

                var stackSize = Math.Min(Math.Min(rule.StackSize, remaining), slotRemaining);

                // Skip slot-boundary partials when more items exist in other slots
                // (sorting will consolidate them for the next pass)
                if (stackSize < rule.StackSize && remaining > slotRemaining && slotIndex + 1 < match.Slots.Count)
                {
                    SkippedPartials = true;
                    remaining -= slotRemaining;
                    slotRemaining = 0;
                    continue;
                }

                var slot = match.Slots[slotIndex];
                pendingListings.Enqueue((match.ItemId, stackSize, slot));
                remaining -= stackSize;
                slotRemaining -= stackSize;
            }
        }

        for (var retainerIdx = 0; retainerIdx < freeSlotsPerRetainer.Length; retainerIdx++)
        {
            var slotsAvailable = freeSlotsPerRetainer[retainerIdx];
            if (slotsAvailable <= 0 || pendingListings.Count == 0)
                continue;

            commands.Add(_commandFactory.CreateSelectRetainer(retainerIdx));
            commands.Add(_commandFactory.CreateOpenSellMenu());

            var listed = 0;
            while (listed < slotsAvailable && pendingListings.Count > 0)
            {
                var (itemId, stackSize, slot) = pendingListings.Dequeue();
                commands.Add(_commandFactory.CreateSelectItem(slot.ContainerIndex, slot.SlotIndex, stackSize));
                commands.Add(_commandFactory.CreateFetchMarketPrice(itemId));
                commands.Add(_commandFactory.CreateSetPrice());
                commands.Add(_commandFactory.CreateConfirmListing());
                listed++;
            }

            SlotsUsedPerRetainer[retainerIdx] = listed;
            commands.Add(_commandFactory.CreateCloseRetainer());
        }

        return commands;
    }
}
