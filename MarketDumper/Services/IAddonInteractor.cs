using System;
using System.Threading;
using System.Threading.Tasks;

namespace MarketDumper.Services;

public interface IAddonInteractor
{
    Task<bool> WaitForAddon(string addonName, TimeSpan timeout, CancellationToken cancellationToken);
    bool IsAddonVisible(string addonName);
    bool ClickAddonButton(string addonName, int nodeIndex);
    bool SetAddonInputValue(string addonName, int nodeIndex, int value);
    string? ReadAddonText(string addonName, int nodeIndex);
}
