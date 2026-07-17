using System.Collections.Generic;
using MarketDumper.Models;

namespace MarketDumper.Commands;

public interface ICommandFactory
{
    ICommand CreateSelectRetainer(int retainerIndex);
    ICommand CreateOpenSellMenu();
    ICommand CreateSelectItem(int containerIndex, int slotIndex, int stackSize);
    ICommand CreateFetchMarketPrice(uint itemId);
    ICommand CreateSetPrice();
    ICommand CreateConfirmListing();
    ICommand CreateCloseRetainer();
    ICommand CreateConsolidateListings(List<InventoryMatch> playerMatches, IReadOnlyList<SellRule> rules, ulong retainerId, ulong retainerGil);
}
