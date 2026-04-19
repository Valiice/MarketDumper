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
    private TaskCompletionSource<MarketDataResult?>? _pendingRequest;
    private uint _expectedItemId;

    public MarketDataProvider(IMarketBoard marketBoard, Configuration configuration, IPluginLog log)
    {
        _marketBoard = marketBoard;
        _configuration = configuration;
        _log = log;
        _marketBoard.OfferingsReceived += OnOfferingsReceived;
    }

    public void PrepareForMarketData(uint itemId)
    {
        _expectedItemId = itemId;
        _pendingRequest = new TaskCompletionSource<MarketDataResult?>();
    }

    public async Task<MarketDataResult?> WaitForMarketDataAsync(uint itemId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_pendingRequest == null || _expectedItemId != itemId)
            PrepareForMarketData(itemId);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            using var registration = cts.Token.Register(() => _pendingRequest.TrySetResult(null));
            return await _pendingRequest.Task;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private unsafe void OnOfferingsReceived(IMarketBoardCurrentOfferings offerings)
    {
        if (_pendingRequest == null)
            return;

        var firstListing = offerings.ItemListings.FirstOrDefault();
        if (firstListing != null && _expectedItemId != 0 && firstListing.ItemId != _expectedItemId)
        {
            _log.Warning($"MarketData: ignoring response for item {firstListing.ItemId}, expected {_expectedItemId}");
            return;
        }

        var listings = offerings.ItemListings
            .Select(l => new MarketListing((int)l.PricePerUnit, l.RetainerId, l.IsHq))
            .ToList();

        _log.Information($"MarketData: received {listings.Count} listings for item {_expectedItemId}");

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

        var result = new MarketDataResult(listings, _configuration.PricingConfig, ownRetainerIds);
        _pendingRequest.TrySetResult(result);
    }

    public void Dispose()
    {
        _marketBoard.OfferingsReceived -= OnOfferingsReceived;
    }
}
