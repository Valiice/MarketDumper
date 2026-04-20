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

        // RetainerSellList groups identical items together, with groups ordered by each
        // item type's earliest slot index, and items within a group ordered by slot index.
        // This matches the AtkComponentList display rows 0..N-1.
        var slotToDisplayRow = listings
            .GroupBy(l => l.ItemId)
            .OrderBy(g => g.Min(l => l.SlotIndex))
            .SelectMany(g => g.OrderBy(l => l.SlotIndex))
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

            // Dismiss any stale ContextMenu before right-clicking
            if (await _addon.IsAddonVisible("ContextMenu"))
            {
                _log.Information("[Consolidate] Closing stale ContextMenu before right-click");
                await _addon.CloseAddon("ContextMenu");
                await Task.Delay(100, cancellationToken);
            }

            if (!await _addon.RightClickRetainerListing(displayRow))
            {
                _log.Warning($"[Consolidate] RightClickRetainerListing failed for display row {displayRow} — skipping");
                continue;
            }

            // Wait for the game to open the ContextMenu (FireCallback is async — menu takes a frame or two)
            await Task.Delay(200, cancellationToken);

            if (!await _addon.WaitForAddon("ContextMenu", TimeSpan.FromSeconds(3), cancellationToken))
            {
                _log.Warning($"[Consolidate] ContextMenu did not appear after right-clicking display row {displayRow}");
                continue;
            }

            _log.Information("[Consolidate] ContextMenu visible, clicking 'Return Items to Inventory' (index 2)...");
            await _addon.ClickAddonButton("ContextMenu", 2);

            // No confirmation dialog expected for Return Items to Inventory — just wait briefly
            await Task.Delay(300, cancellationToken);
        }

        _log.Information("[Consolidate] Done.");
        return new CommandResult(CommandStatus.Success);
    }
}
