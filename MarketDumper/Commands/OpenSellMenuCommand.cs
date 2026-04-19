using System;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class OpenSellMenuCommand : ICommand
{
    private readonly IAddonInteractor _addon;
    private readonly TimeSpan _timeout;

    public string Description => "Open sell menu";
    public CommandType Type => CommandType.OpenSellMenu;

    public OpenSellMenuCommand(IAddonInteractor addon, TimeSpan timeout)
    {
        _addon = addon;
        _timeout = timeout;
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!await _addon.WaitForAddon("SelectString", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "SelectString addon not visible");

        if (!await ClickSellOnMarket())
            return new CommandResult(CommandStatus.Retry, "Failed to click sell on market option");

        if (!await _addon.WaitForAddon("RetainerSellList", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "RetainerSellList did not open");

        await Task.Delay(500, cancellationToken);
        return new CommandResult(CommandStatus.Success);
    }

    private Task<bool> ClickSellOnMarket()
    {
        return _addon.ClickAddonButton("SelectString", 2);
    }
}
