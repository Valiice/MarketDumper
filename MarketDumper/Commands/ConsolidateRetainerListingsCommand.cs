using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Models;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class ConsolidateRetainerListingsCommand : ICommand
{
    private readonly IRetainerListingReader _reader;
    private readonly IAddonInteractor _addon;
    private readonly HashSet<uint> _playerItemIds;
    private readonly Dictionary<uint, int> _stackSizeByItemId;
    private readonly TimeSpan _timeout;

    public string Description => "Return undersized retainer listings to inventory";
    public CommandType Type => CommandType.ConsolidateListings;

    public ConsolidateRetainerListingsCommand(
        IRetainerListingReader reader,
        IAddonInteractor addon,
        List<InventoryMatch> playerMatches,
        IReadOnlyList<SellRule> rules,
        TimeSpan timeout)
    {
        _reader = reader;
        _addon = addon;
        _timeout = timeout;
        _playerItemIds = playerMatches.Select(m => m.ItemId).ToHashSet();
        _stackSizeByItemId = rules.ToDictionary(r => r.ItemId, r => r.StackSize);
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var listings = await _reader.ReadListingsAsync();

        var toReturn = listings
            .Where(l => _playerItemIds.Contains(l.ItemId)
                     && _stackSizeByItemId.TryGetValue(l.ItemId, out var ss)
                     && l.Quantity < ss)
            .OrderByDescending(l => l.SlotIndex)
            .ToList();

        foreach (var listing in toReturn)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await _addon.RightClickRetainerListing(listing.SlotIndex))
                continue;

            if (!await _addon.WaitForAddon("ContextMenu", _timeout, cancellationToken))
                continue;

            await _addon.ClickAddonButton("ContextMenu", 2);
            await Task.Delay(300, cancellationToken);
        }

        return new CommandResult(CommandStatus.Success);
    }
}
