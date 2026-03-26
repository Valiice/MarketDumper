using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Commands;

namespace MarketDumper.Automation;

public class CommandQueue
{
    private readonly Queue<ICommand> _commands = new();
    private readonly int _maxRetries;

    public event Action<int, int, string>? OnProgress;

    public int CommandCount => _commands.Count;

    public CommandQueue(int maxRetries)
    {
        _maxRetries = maxRetries;
    }

    public void Enqueue(ICommand command)
    {
        _commands.Enqueue(command);
    }

    public async Task<QueueResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var total = _commands.Count;
        var executed = 0;
        var context = new CommandContext();

        while (_commands.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
                return new QueueResult(false, executed, "Cancelled by user.");

            var command = _commands.Dequeue();
            executed++;

            OnProgress?.Invoke(executed, total, command.Description);

            var success = false;
            for (var attempt = 1; attempt <= _maxRetries; attempt++)
            {
                var result = await command.ExecuteAsync(context, cancellationToken);

                if (result.Status == CommandStatus.Success)
                {
                    success = true;
                    break;
                }

                if (result.Status == CommandStatus.Abort)
                    return new QueueResult(false, executed, result.ErrorMessage);

                if (result.Status == CommandStatus.Retry && attempt < _maxRetries && result.RetryDelay.HasValue)
                    await Task.Delay(result.RetryDelay.Value, cancellationToken);
            }

            if (!success)
                return new QueueResult(false, executed, $"Command '{command.Description}' failed: retries exhausted.");

            if (cancellationToken.IsCancellationRequested)
                return new QueueResult(false, executed, "Cancelled by user.");
        }

        return new QueueResult(true, executed);
    }
}

public record QueueResult(
    bool Completed,
    int CommandsExecuted,
    string? ErrorMessage = null
);
