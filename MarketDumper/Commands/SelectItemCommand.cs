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
        if (!OpenItemContextMenu())
            return new CommandResult(CommandStatus.Retry, $"Failed to right-click item at container {_containerIndex} slot {_slotIndex}");

        if (!await ClickPutUpForSale(cancellationToken))
            return new CommandResult(CommandStatus.Retry, "Failed to click Put Up for Sale");

        if (!await HandleStackSplitIfNeeded(cancellationToken))
            return new CommandResult(CommandStatus.Retry, "Failed to set stack size");

        if (!await WaitForAdjustPriceScreen(cancellationToken))
            return new CommandResult(CommandStatus.Retry, "Adjust Price screen not visible");

        ReadHqStatus(context);
        return new CommandResult(CommandStatus.Success);
    }

    private bool OpenItemContextMenu()
    {
        return _addon.RightClickInventoryItem(_containerIndex, _slotIndex);
    }

    private async Task<bool> ClickPutUpForSale(CancellationToken cancellationToken)
    {
        if (!await _addon.WaitForAddon("ContextMenu", _timeout, cancellationToken))
            return false;

        return _addon.ClickAddonButton("ContextMenu", 0);
    }

    private async Task<bool> HandleStackSplitIfNeeded(CancellationToken cancellationToken)
    {
        await Task.Delay(300, cancellationToken);

        if (!_addon.IsAddonVisible("InputNumeric"))
            return true;

        if (!_addon.SetAddonInputValue("InputNumeric", 0, _stackSize))
            return false;

        return _addon.ClickAddonButton("InputNumeric", 1);
    }

    private async Task<bool> WaitForAdjustPriceScreen(CancellationToken cancellationToken)
    {
        return await _addon.WaitForAddon("RetainerSell", _timeout, cancellationToken);
    }

    private void ReadHqStatus(CommandContext context)
    {
        var itemText = _addon.ReadAddonText("RetainerSell", 0);
        context.IsHq = itemText?.Contains("\uE03C") ?? false;
    }
}
