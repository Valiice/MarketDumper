using System;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class SelectItemCommand : ICommand
{
    private readonly int _containerIndex;
    private readonly int _slotIndex;
    private readonly int _stackSize;
    private readonly IAddonInteractor _addon;
    private readonly TimeSpan _timeout;

    public string Description => $"Select item at container {_containerIndex} slot {_slotIndex}";
    public CommandType Type => CommandType.SelectItem;

    public SelectItemCommand(int containerIndex, int slotIndex, int stackSize, IAddonInteractor addon, TimeSpan timeout)
    {
        _containerIndex = containerIndex;
        _slotIndex = slotIndex;
        _stackSize = stackSize;
        _addon = addon;
        _timeout = timeout;
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Wait for retainer sell list
        if (!await _addon.WaitForAddon("RetainerSellList", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "RetainerSellList addon not visible");

        // Click the item slot
        var slotNodeIndex = _containerIndex * 35 + _slotIndex;
        if (!_addon.ClickAddonButton("RetainerSellList", slotNodeIndex))
            return new CommandResult(CommandStatus.Retry, $"Failed to click slot {slotNodeIndex}");

        // Handle InputNumeric dialog for stack splitting if stack > 1
        if (_stackSize > 1)
        {
            await Task.Delay(200, cancellationToken);
            if (_addon.IsAddonVisible("InputNumeric"))
            {
                _addon.SetAddonInputValue("InputNumeric", 0, _stackSize);
                _addon.ClickAddonButton("InputNumeric", 1); // Confirm
            }
        }

        // Wait for RetainerSell addon
        if (!await _addon.WaitForAddon("RetainerSell", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "RetainerSell addon not visible");

        // Read HQ status from the addon
        var hqText = _addon.ReadAddonText("RetainerSell", 0);
        context.IsHq = hqText != null && hqText.Contains("HQ", StringComparison.OrdinalIgnoreCase);

        return new CommandResult(CommandStatus.Success);
    }
}
