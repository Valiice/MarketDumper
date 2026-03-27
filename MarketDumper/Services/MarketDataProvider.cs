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

    public async Task<MarketDataResult?> WaitForMarketDataAsync(uint itemId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _expectedItemId = itemId;
        _pendingRequest = new TaskCompletionSource<MarketDataResult?>();

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

        var listings = offerings.ItemListings
            .Select(l => new MarketListing((int)l.PricePerUnit, l.RetainerId, l.IsHq))
            .ToList();

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
