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
        if (await DismissRetainerDialogue(cancellationToken))
            await Task.Delay(300, cancellationToken);

        if (!await _addon.WaitForAddon("SelectString", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "SelectString addon not visible");

        if (!ClickSellOnMarket())
            return new CommandResult(CommandStatus.Retry, "Failed to click sell on market option");

        return new CommandResult(CommandStatus.Success);
    }

    private async Task<bool> DismissRetainerDialogue(CancellationToken cancellationToken)
    {
        if (!await _addon.WaitForAddon("Talk", TimeSpan.FromSeconds(3), cancellationToken))
            return false;

        _addon.ClickAddonButton("Talk", 0);
        return true;
    }

    private bool ClickSellOnMarket()
    {
        return _addon.ClickAddonButton("SelectString", 2);
    }
}
