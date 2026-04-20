using System;
using System.Threading;
using System.Threading.Tasks;

namespace MarketDumper.Services;

public interface IAddonInteractor
{
    Task<bool> WaitForAddon(string addonName, TimeSpan timeout, CancellationToken cancellationToken);
    Task<bool> IsAddonVisible(string addonName);
    Task<bool> ClickAddonButton(string addonName, int nodeIndex);
    Task<bool> SetAddonInputValue(string addonName, int nodeIndex, int value);
    Task<string?> ReadAddonText(string addonName, int nodeIndex);
    Task<bool> RightClickInventoryItem(int containerIndex, int slotIndex);
    Task<bool> RightClickRetainerListing(int slotIndex);
    Task<bool> ScrollRetainerSellListTo(int row);
    Task<int> FindContextMenuItemByText(string containsText);
    Task<int> GetFreeInventorySlots();
    Task<bool> CloseAddon(string addonName);
    Task ExecuteGameCommand(string command);
}
