using System;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class CloseRetainerCommand : ICommand
{
    private readonly IAddonInteractor _addon;
    private readonly TimeSpan _timeout;

    public string Description => "Close retainer and return to retainer list";
    public CommandType Type => CommandType.CloseRetainer;

    public CloseRetainerCommand(IAddonInteractor addon, TimeSpan timeout)
    {
        _addon = addon;
        _timeout = timeout;
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (await _addon.IsAddonVisible("ItemSearchResult"))
        {
            await _addon.CloseAddon("ItemSearchResult");
            await Task.Delay(200, cancellationToken);
        }

        if (await _addon.IsAddonVisible("RetainerSellList"))
        {
            await _addon.CloseAddon("RetainerSellList");
            await Task.Delay(200, cancellationToken);
        }

        if (await _addon.IsAddonVisible("SelectString"))
            await _addon.CloseAddon("SelectString");

        await Task.Delay(2000, cancellationToken);

        if (!await _addon.WaitForAddon("RetainerList", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "RetainerList not visible after closing retainer");

        context.CurrentRetainerIndex = null;
        return new CommandResult(CommandStatus.Success);
    }
}
