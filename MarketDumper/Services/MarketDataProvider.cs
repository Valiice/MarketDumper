using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Network.Structures;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using MarketDumper.Models;

namespace MarketDumper.Services;

public class MarketDataProvider : IMarketDataProvider, IDisposable
{
    private readonly IMarketBoard _marketBoard;
    private readonly Configuration _configuration;
    private readonly IPluginLog _log;
    private readonly Func<HashSet<ulong>> _ownRetainerIds;
    private TaskCompletionSource<MarketDataResult?>? _pendingRequest;
    private uint _expectedItemId;

    public MarketDataProvider(
        IMarketBoard marketBoard,
        Configuration configuration,
        IPluginLog log,
        Func<HashSet<ulong>>? ownRetainerIds = null)
    {
        _marketBoard = marketBoard;
        _configuration = configuration;
        _log = log;
        _ownRetainerIds = ownRetainerIds ?? ReadOwnRetainerIdsFromGame;
        _marketBoard.OfferingsReceived += OnOfferingsReceived;
    }

    public void PrepareForMarketData(uint itemId)
    {
        _expectedItemId = itemId;
        _pendingRequest = new TaskCompletionSource<MarketDataResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task<MarketDataResult?> WaitForMarketDataAsync(uint itemId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // A completed request here is a pre-armed one holding this item's
        // just-received data (state clears after every wait) — return it.
        if (_pendingRequest == null || _expectedItemId != itemId)
            PrepareForMarketData(itemId);

        var pending = _pendingRequest!;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            using var registration = cts.Token.Register(() => pending.TrySetResult(null));
            return await pending.Task;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _pendingRequest = null;
            _expectedItemId = 0;
        }
    }

    private void OnOfferingsReceived(IMarketBoardCurrentOfferings offerings)
    {
        var pending = _pendingRequest;
        if (pending == null || pending.Task.IsCompleted)
            return;

        // An empty response carries no item id, so it cannot be attributed to the
        // expected item — ignore it rather than resolve the wait with foreign data.
        var firstListing = offerings.ItemListings.FirstOrDefault();
        if (firstListing == null)
        {
            _log.Information("MarketData: ignoring response with no listings");
            return;
        }
        if (_expectedItemId != 0 && firstListing.ItemId != _expectedItemId)
        {
            _log.Warning($"MarketData: ignoring response for item {firstListing.ItemId}, expected {_expectedItemId}");
            return;
        }

        var listings = offerings.ItemListings
            .Select(l => new MarketListing((int)l.PricePerUnit, l.RetainerId, l.IsHq))
            .ToList();

        _log.Information($"MarketData: received {listings.Count} listings for item {_expectedItemId}");

        var result = new MarketDataResult(listings, _configuration.PricingConfig, _ownRetainerIds());
        pending.TrySetResult(result);
    }

    private static unsafe HashSet<ulong> ReadOwnRetainerIdsFromGame()
    {
        var ownRetainerIds = new HashSet<ulong>();
        var retainerManager = RetainerManager.Instance();
        if (retainerManager != null)
        {
            for (var i = 0; i < retainerManager->GetRetainerCount(); i++)
            {
                var retainer = retainerManager->Retainers[i];
                ownRetainerIds.Add(retainer.RetainerId);
            }
        }
        return ownRetainerIds;
    }

    public void Dispose()
    {
        _marketBoard.OfferingsReceived -= OnOfferingsReceived;
    }
}
