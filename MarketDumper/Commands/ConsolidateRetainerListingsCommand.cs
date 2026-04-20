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
        _log.Information("[Consolidate] Reading retainer listings...");
        var listings = await _reader.ReadListingsAsync();
        _log.Information($"[Consolidate] Found {listings.Count} total listings in retainer");

        // Map market slot index → display row index (RetainerSellList shows filled slots
        // in slot order as sequential rows 0..N-1, matching AtkComponentList.ListLength)
        var slotToDisplayRow = listings
            .OrderBy(l => l.SlotIndex)
            .Select((l, i) => (l.SlotIndex, Row: i))
            .ToDictionary(x => x.SlotIndex, x => x.Row);

        var toReturn = listings
            .Where(l => _playerItemIds.Contains(l.ItemId)
                     && _stackSizeByItemId.TryGetValue(l.ItemId, out var ss)
                     && l.Quantity < ss)
            .OrderByDescending(l => slotToDisplayRow[l.SlotIndex])
            .ToList();

        _log.Information($"[Consolidate] {toReturn.Count} listings qualify for return (below stack size + item in inventory)");

        foreach (var listing in toReturn)
        {
            var displayRow = slotToDisplayRow[listing.SlotIndex];
            cancellationToken.ThrowIfCancellationRequested();
            _log.Information($"[Consolidate] Returning display row {displayRow} (slot {listing.SlotIndex}): itemId={listing.ItemId} qty={listing.Quantity}");

            if (!await _addon.RightClickRetainerListing(displayRow))
            {
                _log.Warning($"[Consolidate] RightClickRetainerListing failed for display row {displayRow} — skipping");
                continue;
            }

            _log.Information("[Consolidate] Right-click fired, waiting for ContextMenu...");
            if (!await _addon.WaitForAddon("ContextMenu", _timeout, cancellationToken))
            {
                _log.Warning($"[Consolidate] ContextMenu did not appear after right-clicking display row {displayRow}");
                continue;
            }

            _log.Information("[Consolidate] ContextMenu visible, clicking 'Return Items to Inventory' (index 2)...");
            var clicked = await _addon.ClickAddonButton("ContextMenu", 2);
            _log.Information($"[Consolidate] ClickAddonButton result: {clicked}");

            // FFXIV shows a "Are you sure?" confirmation after clicking Return Items to Inventory
            if (await _addon.WaitForAddon("SelectYesno", TimeSpan.FromSeconds(3), cancellationToken))
            {
                _log.Information("[Consolidate] SelectYesno appeared, clicking Yes...");
                await _addon.ClickAddonButton("SelectYesno", 0);
                await Task.Delay(500, cancellationToken);
            }
            else
            {
                _log.Information("[Consolidate] No SelectYesno — return may have completed without confirmation");
                await Task.Delay(300, cancellationToken);
            }
        }

        _log.Information("[Consolidate] Done.");
        return new CommandResult(CommandStatus.Success);
    }
}
