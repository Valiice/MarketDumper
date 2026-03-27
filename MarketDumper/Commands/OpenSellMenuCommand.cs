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
        // After selecting a retainer, a Talk dialogue appears first ("My lord. What would you ask of me?")
        // We need to dismiss it before the SelectString menu shows up
        if (await _addon.WaitForAddon("Talk", TimeSpan.FromSeconds(3), cancellationToken))
        {
            _addon.ClickAddonButton("Talk", 0);
            await Task.Delay(300, cancellationToken);
        }

        // Wait for the retainer menu (SelectString addon)
        if (!await _addon.WaitForAddon("SelectString", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "SelectString addon not visible");

        // Click "Sell items" option
        if (!_addon.ClickAddonButton("SelectString", 0))
            return new CommandResult(CommandStatus.Retry, "Failed to click sell items option");

        // Wait for second menu to appear, then click "Market Board" option
        if (!await _addon.WaitForAddon("SelectString", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "SelectString addon not visible after sell items");

        if (!_addon.ClickAddonButton("SelectString", 0))
            return new CommandResult(CommandStatus.Retry, "Failed to click market board option");

        return new CommandResult(CommandStatus.Success);
    }
}
