using System;
using System.Collections.Generic;
using MarketDumper.Models;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class CommandFactory : ICommandFactory
{
    private readonly IAddonInteractor _addon;
    private readonly IPricingService _pricingService;
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IRetainerListingReader _retainerListingReader;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _interactionDelay;
    private readonly Action<int> _setPendingStackSize;

    public CommandFactory(
        IAddonInteractor addon,
        IPricingService pricingService,
        IMarketDataProvider marketDataProvider,
        IRetainerListingReader retainerListingReader,
        TimeSpan timeout,
        TimeSpan interactionDelay,
        Action<int> setPendingStackSize)
    {
        _addon = addon;
        _pricingService = pricingService;
        _marketDataProvider = marketDataProvider;
        _retainerListingReader = retainerListingReader;
        _timeout = timeout;
        _interactionDelay = interactionDelay;
        _setPendingStackSize = setPendingStackSize;
    }

    public ICommand CreateSelectRetainer(int retainerIndex) =>
        new SelectRetainerCommand(retainerIndex, _addon, _timeout);

    public ICommand CreateOpenSellMenu() =>
        new OpenSellMenuCommand(_addon, _timeout);

    public ICommand CreateSelectItem(int containerIndex, int slotIndex, int stackSize) =>
        new SelectItemCommand(containerIndex, slotIndex, stackSize, _addon, _timeout, _setPendingStackSize);

    public ICommand CreateFetchMarketPrice(uint itemId) =>
        new FetchMarketPriceCommand(itemId, _pricingService, _addon, _marketDataProvider, _timeout);

    public ICommand CreateSetPrice() =>
        new SetPriceCommand(_addon, _timeout);

    public ICommand CreateConfirmListing() =>
        new ConfirmListingCommand(_addon, _timeout, _interactionDelay);

    public ICommand CreateCloseRetainer() =>
        new CloseRetainerCommand(_addon, _timeout);

    public ICommand CreateConsolidateListings(List<InventoryMatch> playerMatches, IReadOnlyList<SellRule> rules) =>
        new ConsolidateRetainerListingsCommand(_retainerListingReader, _addon, playerMatches, rules, _timeout);
}
