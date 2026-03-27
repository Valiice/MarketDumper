using System;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class ConfirmListingCommand : ICommand
{
    private readonly IAddonInteractor _addon;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _interactionDelay;

    public string Description => "Confirm listing";
    public CommandType Type => CommandType.ConfirmListing;

    public ConfirmListingCommand(IAddonInteractor addon, TimeSpan timeout, TimeSpan interactionDelay)
    {
        _addon = addon;
        _timeout = timeout;
        _interactionDelay = interactionDelay;
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Wait for RetainerSell addon
        if (!await _addon.WaitForAddon("RetainerSell", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "RetainerSell addon not visible");

        // Click confirm button
        if (!_addon.ClickAddonButton("RetainerSell", 1))
            return new CommandResult(CommandStatus.Retry, "Failed to click confirm button");

        // Handle SelectYesno dialog if it appears
        await Task.Delay(200, cancellationToken);
        if (_addon.IsAddonVisible("SelectYesno"))
        {
            if (!_addon.ClickAddonButton("SelectYesno", 0))
                return new CommandResult(CommandStatus.Retry, "Failed to click Yes on confirmation dialog");
        }

        // Delay between listings to avoid rate limiting
        if (_interactionDelay > TimeSpan.Zero)
            await Task.Delay(_interactionDelay, cancellationToken);

        return new CommandResult(CommandStatus.Success);
    }
}
