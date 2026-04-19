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
    private readonly Action<int> _setPendingStackSize;

    public string Description => $"Select item at container {_containerIndex} slot {_slotIndex}";
    public CommandType Type => CommandType.SelectItem;

    public SelectItemCommand(int containerIndex, int slotIndex, int stackSize, IAddonInteractor addon, TimeSpan timeout, Action<int> setPendingStackSize)
    {
        _containerIndex = containerIndex;
        _slotIndex = slotIndex;
        _stackSize = stackSize;
        _addon = addon;
        _timeout = timeout;
        _setPendingStackSize = setPendingStackSize;
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Tell the InputNumeric listener what stack size to use
        _setPendingStackSize(_stackSize);

        if (!await OpenItemContextMenu())
            return new CommandResult(CommandStatus.Retry, $"Failed to right-click item at container {_containerIndex} slot {_slotIndex}");

        if (!await ClickPutUpForSale(cancellationToken))
            return new CommandResult(CommandStatus.Retry, "Failed to click Put Up for Sale");

        if (!await WaitForAdjustPriceScreen(cancellationToken))
            return new CommandResult(CommandStatus.Retry, "Adjust Price screen not visible");

        await _addon.SetAddonInputValue("RetainerSell", 1, _stackSize);
        await ReadHqStatus(context);
        return new CommandResult(CommandStatus.Success);
    }

    private Task<bool> OpenItemContextMenu()
    {
        return _addon.RightClickInventoryItem(_containerIndex, _slotIndex);
    }

    private async Task<bool> ClickPutUpForSale(CancellationToken cancellationToken)
    {
        if (!await _addon.WaitForAddon("ContextMenu", _timeout, cancellationToken))
            return false;

        return await _addon.ClickAddonButton("ContextMenu", 0);
    }

    private async Task<bool> WaitForAdjustPriceScreen(CancellationToken cancellationToken)
    {
        return await _addon.WaitForAddon("RetainerSell", _timeout, cancellationToken);
    }

    private async Task ReadHqStatus(CommandContext context)
    {
        var itemText = await _addon.ReadAddonText("RetainerSell", 0);
        context.IsHq = itemText?.Contains("\uE03C") ?? false;
    }
}
