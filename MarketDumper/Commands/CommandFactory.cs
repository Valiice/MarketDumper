using System;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class CommandFactory : ICommandFactory
{
    private readonly IAddonInteractor _addon;
    private readonly IPricingService _pricingService;
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _interactionDelay;

    public CommandFactory(
        IAddonInteractor addon,
        IPricingService pricingService,
        IMarketDataProvider marketDataProvider,
        TimeSpan timeout,
        TimeSpan interactionDelay)
    {
        _addon = addon;
        _pricingService = pricingService;
        _marketDataProvider = marketDataProvider;
        _timeout = timeout;
        _interactionDelay = interactionDelay;
    }

    public ICommand CreateSelectRetainer(int retainerIndex) =>
        new SelectRetainerCommand(retainerIndex, _addon, _timeout);

    public ICommand CreateOpenSellMenu() =>
        new OpenSellMenuCommand(_addon, _timeout);

    public ICommand CreateSelectItem(int containerIndex, int slotIndex, int stackSize) =>
        new SelectItemCommand(containerIndex, slotIndex, stackSize, _addon, _timeout);

    public ICommand CreateFetchMarketPrice(uint itemId) =>
        new FetchMarketPriceCommand(itemId, _pricingService, _addon, _marketDataProvider, _timeout);

    public ICommand CreateSetPrice() =>
        new SetPriceCommand(_addon, _timeout);

    public ICommand CreateConfirmListing() =>
        new ConfirmListingCommand(_addon, _timeout, _interactionDelay);
}
