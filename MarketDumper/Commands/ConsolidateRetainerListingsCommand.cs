using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using MarketDumper.Models;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class ConsolidateRetainerListingsCommand : ICommand
{
    private readonly IRetainerListingReader _reader;
    private readonly IAddonInteractor _addon;
    private readonly IPluginLog _log;
    private readonly HashSet<uint> _playerItemIds;
    private readonly Dictionary<uint, int> _stackSizeByItemId;
    private readonly TimeSpan _timeout;

    public string Description => "Return undersized retainer listings to inventory";
    public CommandType Type => CommandType.ConsolidateListings;

    public ConsolidateRetainerListingsCommand(
        IRetainerListingReader reader,
        IAddonInteractor addon,
        IPluginLog log,
        List<InventoryMatch> playerMatches,
        IReadOnlyList<SellRule> rules,
        TimeSpan timeout)
    {
        _reader = reader;
        _addon = addon;
        _log = log;
        _timeout = timeout;
        _playerItemIds = playerMatches.Select(m => m.ItemId).ToHashSet();
        _stackSizeByItemId = rules.Where(r => r.Enabled).ToDictionary(r => r.ItemId, r => r.StackSize);
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Log free inventory slots upfront — returns fail silently if inventory is full
        var freeSlots = await _addon.GetFreeInventorySlots();
        _log.Information($"[Consolidate] Player inventory: {freeSlots} free slots");
        if (freeSlots == 0)
            _log.Warning("[Consolidate] Player inventory is FULL — all returns will fail silently!");

        _log.Information("[Consolidate] Reading retainer listings...");
        var listings = await _reader.ReadListingsAsync();
        _log.Information($"[Consolidate] Found {listings.Count} total listings in retainer");

        // RetainerSellList groups identical items together, with groups ordered by each
        // item type's earliest slot index, and items within a group ordered by slot index.
        var slotToDisplayRow = listings
            .GroupBy(l => l.ItemId)
            .OrderBy(g => g.Min(l => l.SlotIndex))
            .SelectMany(g => g.OrderBy(l => l.SlotIndex))
            .Select((l, i) => (l.SlotIndex, Row: i))
            .ToDictionary(x => x.SlotIndex, x => x.Row);

        // Return a listing if:
        //   - qty < StackSize, AND
        //   - player has this item in inventory (new items to merge with), OR
        //   - retainer has multiple listings of the same item (consolidate within retainer)
        var listingCountByItemId = listings.GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => g.Count());

        var toReturn = listings
            .Where(l => _stackSizeByItemId.TryGetValue(l.ItemId, out var ss)
                     && l.Quantity < ss
                     && (_playerItemIds.Contains(l.ItemId) || listingCountByItemId[l.ItemId] > 1))
            .OrderByDescending(l => slotToDisplayRow[l.SlotIndex])
            .ToList();

        _log.Information($"[Consolidate] {toReturn.Count} listings qualify for return (below stack size + item in inventory)");

        // Log full display order for verification
        var displayOrder = listings
            .GroupBy(l => l.ItemId)
            .OrderBy(g => g.Min(l => l.SlotIndex))
            .SelectMany(g => g.OrderBy(l => l.SlotIndex))
            .ToList();
        for (var r = 0; r < displayOrder.Count; r++)
            _log.Information($"[Consolidate] Display map: row {r} = itemId={displayOrder[r].ItemId} qty={displayOrder[r].Quantity} slot={displayOrder[r].SlotIndex}");

        if (toReturn.Count == 0)
        {
            _log.Information("[Consolidate] Done.");
            return new CommandResult(CommandStatus.Success);
        }

        foreach (var listing in toReturn)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Re-read to get fresh display rows — rows shift after each return
            var current = await _reader.ReadListingsAsync();
            var freshMap = current
                .GroupBy(l => l.ItemId)
                .OrderBy(g => g.Min(l => l.SlotIndex))
                .SelectMany(g => g.OrderBy(l => l.SlotIndex))
                .Select((l, i) => (l.SlotIndex, Row: i))
                .ToDictionary(x => x.SlotIndex, x => x.Row);

            if (!freshMap.TryGetValue(listing.SlotIndex, out var displayRow))
            {
                _log.Information($"[Consolidate] Slot {listing.SlotIndex} (itemId={listing.ItemId} qty={listing.Quantity}) no longer in market — sold or already returned, skipping");
                continue;
            }

            _log.Information($"[Consolidate] Returning display row {displayRow} (slot {listing.SlotIndex}): itemId={listing.ItemId} qty={listing.Quantity}");

            // Dismiss any stale ContextMenu or SelectYesno before right-clicking
            if (await _addon.IsAddonVisible("SelectYesno"))
            {
                _log.Information("[Consolidate] Closing stale SelectYesno");
                await _addon.CloseAddon("SelectYesno");
                await Task.Delay(150, cancellationToken);
            }
            if (await _addon.IsAddonVisible("ContextMenu"))
            {
                _log.Information("[Consolidate] Closing stale ContextMenu");
                await _addon.CloseAddon("ContextMenu");
                await Task.Delay(150, cancellationToken);
            }

            // Scroll the list to bring the target row into the visible area before right-clicking.
            // FireCallback only works on visible rows — off-screen rows are ignored.
            _log.Information($"[Consolidate] Scrolling list to row {displayRow}...");
            await _addon.ScrollRetainerSellListTo(displayRow);
            await Task.Delay(150, cancellationToken);

            if (!await _addon.RightClickRetainerListing(displayRow))
            {
                _log.Warning($"[Consolidate] RightClickRetainerListing failed for display row {displayRow} — skipping");
                continue;
            }

            await Task.Delay(200, cancellationToken);

            if (!await _addon.WaitForAddon("ContextMenu", TimeSpan.FromSeconds(3), cancellationToken))
            {
                _log.Warning($"[Consolidate] ContextMenu did not appear after right-clicking display row {displayRow}");
                continue;
            }

            // Find "Return Items to Inventory" by text — robust against AutoPinch or other
            // plugins shifting menu indices by injecting extra items.
            var returnIndex = await _addon.FindContextMenuItemByText("Return Items to Inventory");
            if (returnIndex < 0)
            {
                _log.Warning("[Consolidate] 'Return Items to Inventory' not found in ContextMenu — falling back to index 2");
                returnIndex = 2;
            }

            _log.Information($"[Consolidate] Clicking 'Return Items to Inventory' at index {returnIndex}...");
            await _addon.ClickAddonButton("ContextMenu", returnIndex);
            await Task.Delay(200, cancellationToken);

            if (await _addon.IsAddonVisible("SelectYesno"))
            {
                _log.Information("[Consolidate] SelectYesno appeared, clicking Yes...");
                await _addon.ClickAddonButton("SelectYesno", 0);
                await Task.Delay(500, cancellationToken);
            }
            else if (await _addon.IsAddonVisible("InputNumeric"))
            {
                _log.Information("[Consolidate] InputNumeric appeared — clicking OK to confirm full return...");
                await _addon.ClickAddonButton("InputNumeric", 1);
                await Task.Delay(500, cancellationToken);
            }
            else
            {
                await Task.Delay(500, cancellationToken);
            }
        }

        // Verify returns landed — wait for server to process, then re-check
        await Task.Delay(1500, cancellationToken);
        var verification = await _reader.ReadListingsAsync();
        var verifySlots = new HashSet<int>(verification.Select(l => l.SlotIndex));
        var succeeded = toReturn.Count(l => !verifySlots.Contains(l.SlotIndex));
        var failed    = toReturn.Count(l =>  verifySlots.Contains(l.SlotIndex));
        _log.Information($"[Consolidate] Verification: {succeeded} returned successfully, {failed} still in retainer (server rollback)");
        foreach (var l in toReturn.Where(l => verifySlots.Contains(l.SlotIndex)))
            _log.Warning($"[Consolidate] STILL IN RETAINER: slot {l.SlotIndex} itemId={l.ItemId} qty={l.Quantity} — return failed or was rolled back");

        _log.Information("[Consolidate] Done.");
        return new CommandResult(CommandStatus.Success);
    }
}
