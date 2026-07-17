using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using MarketDumper.Automation;
using MarketDumper.Models;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class ConsolidateRetainerListingsCommand : ICommand
{
    private readonly IRetainerListingReader _reader;
    private readonly IAddonInteractor _addon;
    private readonly IPluginLog _log;
    private readonly ConsolidationPlanner _planner;
    private readonly IRetainerSnapshotCache _cache;
    private readonly ulong _retainerId;
    private readonly ulong _retainerGil;
    private readonly List<InventoryMatch> _playerMatches;
    private readonly IReadOnlyList<SellRule> _rules;
    private readonly Dictionary<uint, string> _itemNameByItemId;
    private readonly Dictionary<uint, int> _stackSizeByItemId;
    private readonly TimeSpan _timeout;

    // Approximate rendered-window size; the dig-out fallback only applies to
    // lists longer than this (shorter lists always render fully).
    private const int RenderedWindowRows = 13;

    public string Description => "Return undersized retainer listings to inventory";
    public CommandType Type => CommandType.ConsolidateListings;

    public ConsolidateRetainerListingsCommand(
        IRetainerListingReader reader,
        IAddonInteractor addon,
        IPluginLog log,
        ConsolidationPlanner planner,
        IRetainerSnapshotCache cache,
        ulong retainerId,
        ulong retainerGil,
        List<InventoryMatch> playerMatches,
        IReadOnlyList<SellRule> rules,
        TimeSpan timeout)
    {
        _reader = reader;
        _addon = addon;
        _log = log;
        _planner = planner;
        _cache = cache;
        _retainerId = retainerId;
        _retainerGil = retainerGil;
        _playerMatches = playerMatches;
        _rules = rules;
        _itemNameByItemId = rules.ToDictionary(r => r.ItemId, r => r.ItemName);
        _stackSizeByItemId = rules.ToDictionary(r => r.ItemId, r => r.StackSize);
        _timeout = timeout;
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var freeSlots = await _addon.GetFreeInventorySlots();
        _log.Information($"[Consolidate] Player inventory: {freeSlots} free slots");
        if (freeSlots == 0)
            _log.Warning("[Consolidate] Player inventory is FULL — returns will fail server-side");

        _log.Information("[Consolidate] Reading retainer listings...");
        var listings = await _reader.ReadListingsAsync();
        _log.Information($"[Consolidate] Found {listings.Count} total listings in retainer");

        var displayRows = DisplayRowMap(listings);
        if (Diag.Enabled)
        {
            foreach (var l in listings)
            {
                var name = _itemNameByItemId.GetValueOrDefault(l.ItemId, "?");
                var row = displayRows.TryGetValue(l.SlotIndex, out var dr) ? dr.ToString() : "?";
                _log.Information($"[Diag] Listing slot {l.SlotIndex}: item {l.ItemId} '{name}' x{l.Quantity} (display row {row})");
            }
        }

        // Plan from the fresh read — the cache only decided whether to visit at all.
        var plan = _planner.PlanRetainer(listings, _playerMatches, _rules, freeSlots);
        if (plan.Kind != RetainerPlanKind.Return)
        {
            _log.Information("[Consolidate] Nothing to return on this retainer.");
            _cache.Update(_retainerId, listings, _retainerGil);
            return new CommandResult(CommandStatus.Success);
        }

        // Bottom row first so earlier returns don't shift later targets.
        var ordered = plan.Actions
            .OrderByDescending(a => displayRows.TryGetValue(a.SlotIndex, out var r) ? r : int.MinValue)
            .ToList();

        var cacheInvalidated = false;
        var isFirstAction = true;

        foreach (var action in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The initial read is still current for the first action; each return
            // afterwards shifts the remaining rows, so later actions re-read.
            var current = isFirstAction ? listings : await _reader.ReadListingsAsync();
            isFirstAction = false;
            if (!DisplayRowMap(current).TryGetValue(action.SlotIndex, out var displayRow))
            {
                _log.Information($"[Consolidate] Slot {action.SlotIndex} (itemId={action.ItemId}) no longer listed — sold or returned, skipping");
                continue;
            }

            if (await _addon.IsAddonVisible("SelectYesno"))
            {
                await _addon.CloseAddon("SelectYesno");
                await Task.Delay(150, cancellationToken);
            }
            if (await _addon.IsAddonVisible("ContextMenu"))
            {
                await _addon.CloseAddon("ContextMenu");
                await Task.Delay(150, cancellationToken);
            }

            _log.Information($"[Consolidate] Returning display row {displayRow} (slot {action.SlotIndex}): itemId={action.ItemId} qty={action.Quantity}");
            var itemName = _itemNameByItemId.GetValueOrDefault(action.ItemId, string.Empty);

            var targetRow = await FindVerifiedRowAsync(displayRow, current.Count, itemName, action.Quantity, cancellationToken);

            // Fallback for rows the viewport failed to render: return visible full
            // stacks of the same item (the listing phase relists them) until the
            // target shifts up into view.
            var stackSize = _stackSizeByItemId.GetValueOrDefault(action.ItemId, 0);
            var digOuts = 0;
            while (targetRow == null && stackSize > 0 && digOuts < 10)
            {
                var fresh = await _reader.ReadListingsAsync();
                if (fresh.Count <= RenderedWindowRows || !DisplayRowMap(fresh).ContainsKey(action.SlotIndex))
                    break;
                if (await _addon.GetFreeInventorySlots() <= 0)
                {
                    _log.Warning("[Consolidate] No free inventory slot for a dig-out return — skipping this target");
                    break;
                }

                var fullRow = await FindVerifiedRowAsync(0, fresh.Count, itemName, stackSize, cancellationToken);
                if (fullRow == null)
                    break;

                _log.Information($"[Consolidate] Target x{action.Quantity} not rendered — returning visible full stack at row {fullRow} to shift it up (dig-out {digOuts + 1})");
                if (!await ReturnAtRowAsync(fullRow.Value, cancellationToken))
                    break;

                digOuts++;
                await Task.Delay(800, cancellationToken);
                var after = await _reader.ReadListingsAsync();
                var hint = DisplayRowMap(after).TryGetValue(action.SlotIndex, out var h) ? h : 0;
                targetRow = await FindVerifiedRowAsync(hint, after.Count, itemName, action.Quantity, cancellationToken);
            }

            if (targetRow == null)
            {
                _log.Warning($"[Consolidate] No rendered row matches '{itemName}' x{action.Quantity} after scanning and {digOuts} dig-out(s) — skipping and invalidating cache");
                _cache.Invalidate(_retainerId);
                cacheInvalidated = true;
                continue;
            }

            if (!await ReturnAtRowAsync(targetRow.Value, cancellationToken))
            {
                _cache.Invalidate(_retainerId);
                cacheInvalidated = true;
            }
        }

        // Verify returns landed — wait for the server, then re-read and persist reality.
        await Task.Delay(1500, cancellationToken);
        var verification = await _reader.ReadListingsAsync();
        var verifySlots = new HashSet<int>(verification.Select(l => l.SlotIndex));
        var succeeded = ordered.Count(a => !verifySlots.Contains(a.SlotIndex));
        var failed    = ordered.Count(a =>  verifySlots.Contains(a.SlotIndex));
        _log.Information($"[Consolidate] Verification: {succeeded} returned successfully, {failed} still in retainer");
        foreach (var a in ordered.Where(a => verifySlots.Contains(a.SlotIndex)))
            _log.Warning($"[Consolidate] STILL IN RETAINER: slot {a.SlotIndex} itemId={a.ItemId} qty={a.Quantity} — return failed or was rolled back");

        if (!cacheInvalidated)
        {
            _cache.Update(_retainerId, verification, _retainerGil);
        }
        else
        {
            _log.Information("[Consolidate] skipping cache update — retainer invalidated this run");
        }
        _log.Information("[Consolidate] Done.");
        return new CommandResult(CommandStatus.Success);
    }

    // Finds a row by its on-screen content (the true display order is the game's,
    // not ours); the hint and the list ends serve as scroll anchors. Returns a
    // content-verified row or null.
    private async Task<int?> FindVerifiedRowAsync(int hintRow, int listCount, string itemName, int quantity, CancellationToken cancellationToken)
    {
        var anchors = new List<int> { hintRow, 0, Math.Max(0, listCount - 1) };
        int? targetRow = null;
        var lastScrolled = -1;
        foreach (var anchor in anchors.Distinct())
        {
            var rowTexts = await ScrollAndReadRowsAsync(anchor, cancellationToken);
            lastScrolled = anchor;
            if (Diag.Enabled)
            {
                _log.Information($"[Diag] Row texts after scrolling to row {anchor} ({rowTexts.Count} rows read), looking for '{itemName}' x{quantity}:");
                for (var r = 0; r < rowTexts.Count; r++)
                    _log.Information($"[Diag]   row {r}{(r == hintRow ? " <- computed" : "")}: '{rowTexts[r]}'");
            }

            targetRow = FindRowMatching(rowTexts, itemName, quantity, hintRow);
            if (targetRow != null)
                break;
        }

        if (targetRow == null)
            return null;

        if (targetRow.Value != lastScrolled)
        {
            var recheckTexts = await ScrollAndReadRowsAsync(targetRow.Value, cancellationToken);
            var recheck = targetRow.Value < recheckTexts.Count ? recheckTexts[targetRow.Value] : string.Empty;
            if (!RetainerRowVerifier.Matches(recheck, itemName, quantity))
            {
                _log.Warning($"[Consolidate] Row {targetRow} no longer matches after scroll ('{recheck}')");
                return null;
            }
        }

        return targetRow;
    }

    private async Task<List<string>> ScrollAndReadRowsAsync(int row, CancellationToken cancellationToken)
    {
        await _addon.ScrollRetainerSellListTo(row);
        await Task.Delay(150, cancellationToken);
        return await _addon.GetRetainerSellListRowTexts();
    }

    // Right-clicks the given rendered row and clicks 'Return Items to Inventory',
    // confirming any dialog. The row must already be verified by the caller.
    private async Task<bool> ReturnAtRowAsync(int row, CancellationToken cancellationToken)
    {
        if (!await _addon.RightClickRetainerListing(row))
        {
            _log.Warning($"[Consolidate] RightClickRetainerListing failed for display row {row} — skipping");
            return false;
        }

        await Task.Delay(200, cancellationToken);

        if (!await _addon.WaitForAddon("ContextMenu", _timeout, cancellationToken))
        {
            _log.Warning($"[Consolidate] ContextMenu did not appear after right-clicking display row {row}");
            return false;
        }

        var returnIndex = await _addon.FindContextMenuEntry("Return", "Inventory");
        if (returnIndex < 0)
        {
            _log.Warning("[Consolidate] 'Return Items to Inventory' not found in ContextMenu — skipping this listing");
            await _addon.CloseAddon("ContextMenu");
            return false;
        }

        _log.Information($"[Consolidate] Clicking return entry at index {returnIndex}...");
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

        return true;
    }

    private static int? FindRowMatching(List<string> rowTexts, string itemName, int quantity, int preferredRow)
    {
        if (preferredRow >= 0 && preferredRow < rowTexts.Count
            && RetainerRowVerifier.Matches(rowTexts[preferredRow], itemName, quantity))
            return preferredRow;

        // Nearest match to the hint wins so identical twin listings resolve stably.
        int? best = null;
        for (var i = 0; i < rowTexts.Count; i++)
        {
            if (!RetainerRowVerifier.Matches(rowTexts[i], itemName, quantity))
                continue;
            if (best == null || Math.Abs(i - preferredRow) < Math.Abs(best.Value - preferredRow))
                best = i;
        }
        return best;
    }

    // Assumed display order: items grouped, groups by earliest slot, rows by slot.
    // Only a scroll hint — the on-screen order can differ; content matching decides.
    private static Dictionary<int, int> DisplayRowMap(List<RetainerListing> listings)
    {
        var map = new Dictionary<int, int>(listings.Count);
        var row = 0;
        foreach (var l in listings
                     .GroupBy(l => l.ItemId)
                     .OrderBy(g => g.Min(x => x.SlotIndex))
                     .SelectMany(g => g.OrderBy(x => x.SlotIndex)))
            map[l.SlotIndex] = row++;
        return map;
    }
}
