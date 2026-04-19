using System.Threading;
using System.Threading.Tasks;

namespace MarketDumper.Commands;

public interface ICommand
{
    string Description { get; }
    CommandType Type { get; }
    Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken);
}
