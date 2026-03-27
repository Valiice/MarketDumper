using System;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class FetchMarketPriceCommand : ICommand
{
    private readonly uint _itemId;
    private readonly IPricingService _pricingService;
    private readonly IAddonInteractor _addon;
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly TimeSpan _timeout;

    public string Description => $"Fetch market price for item {_itemId}";
    public CommandType Type => CommandType.FetchMarketPrice;

    public FetchMarketPriceCommand(
        uint itemId,
        IPricingService pricingService,
        IAddonInteractor addon,
        IMarketDataProvider marketDataProvider,
        TimeSpan timeout)
    {
        _itemId = itemId;
        _pricingService = pricingService;
        _addon = addon;
        _marketDataProvider = marketDataProvider;
        _timeout = timeout;
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Wait for RetainerSell addon
        if (!await _addon.WaitForAddon("RetainerSell", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "RetainerSell addon not visible");

        // Click "Compare Prices" button
        if (!_addon.ClickAddonButton("RetainerSell", 0))
            return new CommandResult(CommandStatus.Retry, "Failed to click Compare Prices");

        // Wait for ItemSearchResult addon
        if (!await _addon.WaitForAddon("ItemSearchResult", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "ItemSearchResult addon not visible");

        // Wait for market data from the provider
        var marketData = await _marketDataProvider.WaitForMarketDataAsync(_itemId, _timeout, cancellationToken);
        if (marketData == null)
            return new CommandResult(CommandStatus.Abort, "Failed to receive market data");

        // Calculate undercut price
        var isHq = context.IsHq ?? false;
        var price = _pricingService.FindTargetPrice(
            marketData.Listings,
            marketData.PricingConfig,
            isHq,
            marketData.OwnRetainerIds);

        if (price == null)
            return new CommandResult(CommandStatus.Abort, $"No suitable listing found for item {_itemId}");

        context.CalculatedPrice = price;
        context.CurrentItemId = _itemId;

        return new CommandResult(CommandStatus.Success);
    }
}
