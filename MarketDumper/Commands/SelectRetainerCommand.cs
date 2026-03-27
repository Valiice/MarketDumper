using System;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class SelectRetainerCommand : ICommand
{
    private readonly int _retainerIndex;
    private readonly IAddonInteractor _addon;
    private readonly TimeSpan _timeout;

    public string Description => $"Select retainer {_retainerIndex}";
    public CommandType Type => CommandType.SelectRetainer;

    public SelectRetainerCommand(int retainerIndex, IAddonInteractor addon, TimeSpan timeout)
    {
        _retainerIndex = retainerIndex;
        _addon = addon;
        _timeout = timeout;
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!await _addon.WaitForAddon("RetainerList", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "RetainerList addon not visible");

        if (!_addon.ClickAddonButton("RetainerList", _retainerIndex))
            return new CommandResult(CommandStatus.Retry, $"Failed to click retainer {_retainerIndex}");

        context.CurrentRetainerIndex = _retainerIndex;
        return new CommandResult(CommandStatus.Success);
    }
}
