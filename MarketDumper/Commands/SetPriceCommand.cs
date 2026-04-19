using System;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class SetPriceCommand : ICommand
{
    private readonly IAddonInteractor _addon;
    private readonly TimeSpan _timeout;

    public string Description => "Set listing price";
    public CommandType Type => CommandType.SetPrice;

    public SetPriceCommand(IAddonInteractor addon, TimeSpan timeout)
    {
        _addon = addon;
        _timeout = timeout;
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.CalculatedPrice is not { } price)
            return new CommandResult(CommandStatus.Abort, "No calculated price available");

        // Wait for RetainerSell addon
        if (!await _addon.WaitForAddon("RetainerSell", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "RetainerSell addon not visible");

        // Set the price input
        if (!await _addon.SetAddonInputValue("RetainerSell", 0, price))
            return new CommandResult(CommandStatus.Retry, "Failed to set price input");

        return new CommandResult(CommandStatus.Success);
    }
}
