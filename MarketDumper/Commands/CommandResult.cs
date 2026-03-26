using System;

namespace MarketDumper.Commands;

public record CommandResult(
    CommandStatus Status,
    string? ErrorMessage = null,
    TimeSpan? RetryDelay = null
);
