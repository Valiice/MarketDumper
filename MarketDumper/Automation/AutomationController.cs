using System;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Commands;
using MarketDumper.Services;
using Dalamud.Plugin.Services;

namespace MarketDumper.Automation;

public enum AutomationState
{
    Idle,
    Running,
    Paused,
    Stopping
}

public class AutomationController
{
    private readonly ISellRuleManager _sellRuleManager;
    private readonly IInventoryScanner _inventoryScanner;
    private readonly ICommandFactory _commandFactory;
    private readonly IPluginLog _log;
    private readonly IChatGui _chat;
    private readonly int _maxRetries;

    private CancellationTokenSource? _cts;
    private Task? _runTask;

    public AutomationState State { get; private set; } = AutomationState.Idle;
    public string CurrentAction { get; private set; } = string.Empty;
    public int CurrentStep { get; private set; }
    public int TotalSteps { get; private set; }
    public string? LastError { get; private set; }

    public event Action? OnStateChanged;

    public AutomationController(
        ISellRuleManager sellRuleManager,
        IInventoryScanner inventoryScanner,
        ICommandFactory commandFactory,
        IPluginLog log,
        IChatGui chat,
        int maxRetries = 3)
    {
        _sellRuleManager = sellRuleManager;
        _inventoryScanner = inventoryScanner;
        _commandFactory = commandFactory;
        _log = log;
        _chat = chat;
        _maxRetries = maxRetries;
    }

    public void Start(int[] freeSlotsPerRetainer)
    {
        if (State != AutomationState.Idle)
            return;

        _cts = new CancellationTokenSource();
        State = AutomationState.Running;
        LastError = null;
        OnStateChanged?.Invoke();

        _runTask = Task.Run(() => RunAsync(freeSlotsPerRetainer, _cts.Token));
    }

    public void Stop()
    {
        if (State != AutomationState.Running)
            return;

        State = AutomationState.Stopping;
        _cts?.Cancel();
        OnStateChanged?.Invoke();
    }

    private async Task RunAsync(int[] freeSlotsPerRetainer, CancellationToken cancellationToken)
    {
        try
        {
            var rules = _sellRuleManager.GetEnabledRules();
            var matches = _inventoryScanner.FindMatchingItems(rules);

            if (matches.Count == 0)
            {
                _chat.Print("[MarketDumper] No matching items found in inventory.");
                return;
            }

            var planner = new JobPlanner(_commandFactory);
            var commands = planner.GenerateCommands(matches, new(rules), freeSlotsPerRetainer);

            if (commands.Count == 0)
            {
                _chat.Print("[MarketDumper] No retainer slots available.");
                return;
            }

            var queue = new CommandQueue(_maxRetries);
            queue.OnProgress += (current, total, desc) =>
            {
                CurrentStep = current;
                TotalSteps = total;
                CurrentAction = desc;
                OnStateChanged?.Invoke();
            };

            foreach (var cmd in commands)
                queue.Enqueue(cmd);

            _chat.Print($"[MarketDumper] Starting: {commands.Count} operations queued.");

            var result = await queue.ExecuteAsync(cancellationToken);

            if (result.Completed)
                _chat.Print($"[MarketDumper] Done! Listed {result.CommandsExecuted / 4} items.");
            else
                _chat.Print($"[MarketDumper] Stopped: {result.ErrorMessage}");

            LastError = result.ErrorMessage;
        }
        catch (OperationCanceledException)
        {
            _chat.Print("[MarketDumper] Stopped by user.");
        }
        catch (Exception ex)
        {
            _log.Error($"Automation error: {ex}");
            _chat.Print($"[MarketDumper] Error: {ex.Message}");
            LastError = ex.Message;
        }
        finally
        {
            State = AutomationState.Idle;
            CurrentAction = string.Empty;
            OnStateChanged?.Invoke();
        }
    }
}
