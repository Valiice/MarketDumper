using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Automation;
using MarketDumper.Commands;
using Moq;
using Xunit;

namespace MarketDumper.Tests;

public class CommandQueueTests
{
    private static Mock<ICommand> CreateMockCommand(CommandResult result, string description = "Test")
    {
        var mock = new Mock<ICommand>();
        mock.Setup(c => c.Description).Returns(description);
        mock.Setup(c => c.Type).Returns(CommandType.SelectRetainer);
        mock.Setup(c => c.ExecuteAsync(It.IsAny<CommandContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    [Fact]
    public async Task ExecuteAsync_EmptyQueue_CompletesSuccessfully()
    {
        var queue = new CommandQueue(maxRetries: 3);
        var result = await queue.ExecuteAsync(CancellationToken.None);
        Assert.True(result.Completed);
        Assert.Equal(0, result.CommandsExecuted);
    }

    [Fact]
    public async Task ExecuteAsync_SingleSuccessCommand_ExecutesIt()
    {
        var cmd = CreateMockCommand(new CommandResult(CommandStatus.Success));
        var queue = new CommandQueue(maxRetries: 3);
        queue.Enqueue(cmd.Object);
        var result = await queue.ExecuteAsync(CancellationToken.None);
        Assert.True(result.Completed);
        Assert.Equal(1, result.CommandsExecuted);
        cmd.Verify(c => c.ExecuteAsync(It.IsAny<CommandContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleCommands_ExecutesInOrder()
    {
        var order = new List<int>();
        var cmd1 = new Mock<ICommand>();
        cmd1.Setup(c => c.Description).Returns("First");
        cmd1.Setup(c => c.Type).Returns(CommandType.SelectRetainer);
        cmd1.Setup(c => c.ExecuteAsync(It.IsAny<CommandContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add(1))
            .ReturnsAsync(new CommandResult(CommandStatus.Success));
        var cmd2 = new Mock<ICommand>();
        cmd2.Setup(c => c.Description).Returns("Second");
        cmd2.Setup(c => c.Type).Returns(CommandType.OpenSellMenu);
        cmd2.Setup(c => c.ExecuteAsync(It.IsAny<CommandContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add(2))
            .ReturnsAsync(new CommandResult(CommandStatus.Success));
        var queue = new CommandQueue(maxRetries: 3);
        queue.Enqueue(cmd1.Object);
        queue.Enqueue(cmd2.Object);
        await queue.ExecuteAsync(CancellationToken.None);
        Assert.Equal(new[] { 1, 2 }, order);
    }

    [Fact]
    public async Task ExecuteAsync_RetryThenSuccess_RetriesCommand()
    {
        var attempts = 0;
        var cmd = new Mock<ICommand>();
        cmd.Setup(c => c.Description).Returns("Retry test");
        cmd.Setup(c => c.Type).Returns(CommandType.SelectRetainer);
        cmd.Setup(c => c.ExecuteAsync(It.IsAny<CommandContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                attempts++;
                return attempts < 3
                    ? new CommandResult(CommandStatus.Retry, RetryDelay: TimeSpan.Zero)
                    : new CommandResult(CommandStatus.Success);
            });
        var queue = new CommandQueue(maxRetries: 3);
        queue.Enqueue(cmd.Object);
        var result = await queue.ExecuteAsync(CancellationToken.None);
        Assert.True(result.Completed);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_RetryExhausted_Aborts()
    {
        var cmd = CreateMockCommand(new CommandResult(CommandStatus.Retry, RetryDelay: TimeSpan.Zero));
        var queue = new CommandQueue(maxRetries: 3);
        queue.Enqueue(cmd.Object);
        var result = await queue.ExecuteAsync(CancellationToken.None);
        Assert.False(result.Completed);
        Assert.Contains("retries exhausted", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_AbortCommand_StopsImmediately()
    {
        var cmd1 = CreateMockCommand(new CommandResult(CommandStatus.Abort, "Something broke"));
        var cmd2 = CreateMockCommand(new CommandResult(CommandStatus.Success));
        var queue = new CommandQueue(maxRetries: 3);
        queue.Enqueue(cmd1.Object);
        queue.Enqueue(cmd2.Object);
        var result = await queue.ExecuteAsync(CancellationToken.None);
        Assert.False(result.Completed);
        Assert.Equal("Something broke", result.ErrorMessage);
        cmd2.Verify(c => c.ExecuteAsync(It.IsAny<CommandContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_StopsGracefully()
    {
        var cts = new CancellationTokenSource();
        var cmd = new Mock<ICommand>();
        cmd.Setup(c => c.Description).Returns("Cancel test");
        cmd.Setup(c => c.Type).Returns(CommandType.SelectRetainer);
        cmd.Setup(c => c.ExecuteAsync(It.IsAny<CommandContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ReturnsAsync(new CommandResult(CommandStatus.Success));
        var cmd2 = CreateMockCommand(new CommandResult(CommandStatus.Success), "Should not run");
        var queue = new CommandQueue(maxRetries: 3);
        queue.Enqueue(cmd.Object);
        queue.Enqueue(cmd2.Object);
        var result = await queue.ExecuteAsync(cts.Token);
        Assert.False(result.Completed);
        cmd2.Verify(c => c.ExecuteAsync(It.IsAny<CommandContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void CommandCount_ReflectsQueueState()
    {
        var cmd = CreateMockCommand(new CommandResult(CommandStatus.Success));
        var queue = new CommandQueue(maxRetries: 3);
        Assert.Equal(0, queue.CommandCount);
        queue.Enqueue(cmd.Object);
        Assert.Equal(1, queue.CommandCount);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsProgressViaCallback()
    {
        var progressUpdates = new List<(int current, int total, string description)>();
        var cmd1 = CreateMockCommand(new CommandResult(CommandStatus.Success), "Step 1");
        var cmd2 = CreateMockCommand(new CommandResult(CommandStatus.Success), "Step 2");
        var queue = new CommandQueue(maxRetries: 3);
        queue.OnProgress += (current, total, desc) => progressUpdates.Add((current, total, desc));
        queue.Enqueue(cmd1.Object);
        queue.Enqueue(cmd2.Object);
        await queue.ExecuteAsync(CancellationToken.None);
        Assert.Equal(2, progressUpdates.Count);
        Assert.Equal((1, 2, "Step 1"), progressUpdates[0]);
        Assert.Equal((2, 2, "Step 2"), progressUpdates[1]);
    }
}
