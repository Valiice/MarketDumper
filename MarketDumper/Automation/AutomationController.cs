using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MarketDumper.Commands;
using MarketDumper.Models;
using MarketDumper.Services;

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
    private ICommandFactory _commandFactory;
    private readonly IPluginLog _log;
    private readonly IChatGui _chat;
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IAddonInteractor _addonInteractor;
    private readonly Func<int[]> _getRetainerSlots;
    private readonly int _maxRetries;

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    internal volatile int PendingStackSize;

    public AutomationState State { get; private set; } = AutomationState.Idle;
    public string CurrentAction { get; private set; } = string.Empty;
    public int CurrentStep { get; private set; }
    public int TotalSteps { get; private set; }
    public string? LastError { get; private set; }
    public DateTime? LastFinishTime { get; private set; }

    public event Action? OnStateChanged;

    public AutomationController(
        ISellRuleManager sellRuleManager,
        IInventoryScanner inventoryScanner,
        ICommandFactory commandFactory,
        IPluginLog log,
        IChatGui chat,
        IAddonLifecycle addonLifecycle,
        IAddonInteractor addonInteractor,
        Func<int[]> getRetainerSlots,
        int maxRetries = 3)
    {
        _sellRuleManager = sellRuleManager;
        _inventoryScanner = inventoryScanner;
        _commandFactory = commandFactory;
        _log = log;
        _chat = chat;
        _addonLifecycle = addonLifecycle;
        _addonInteractor = addonInteractor;
        _getRetainerSlots = getRetainerSlots;
        _maxRetries = maxRetries;
    }

    public void SetCommandFactory(ICommandFactory commandFactory) => _commandFactory = commandFactory;

    public void Start()
    {
        if (State != AutomationState.Idle)
            return;

        _cts = new CancellationTokenSource();
        State = AutomationState.Running;
        LastError = null;
        OnStateChanged?.Invoke();

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "Talk", OnTalkAddon);
        _addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "Talk", OnTalkAddon);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "InputNumeric", OnInputNumericAddon);

        var freeSlotsPerRetainer = _getRetainerSlots();
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

    public void Dispose()
    {
        _cts?.Cancel();
        try { _runTask?.Wait(TimeSpan.FromSeconds(5)); } catch { /* cancelled or timed out */ }
        _cts?.Dispose();
    }

    private async void OnInputNumericAddon(AddonEvent type, AddonArgs args)
    {
        var stackSize = PendingStackSize;
        if (stackSize <= 0)
            return;

        PendingStackSize = 0;
        await _addonInteractor.SetAddonInputValue("InputNumeric", 0, stackSize);
        await _addonInteractor.ClickAddonButton("InputNumeric", 1);
    }

    private unsafe void OnTalkAddon(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null || !addon->IsVisible)
            return;
        addon->FireCallbackInt(0);
    }

    private async Task<QueueResult> ExecuteQueueAsync(
        List<ICommand> commands,
        CancellationToken cancellationToken)
    {
        var queue = new CommandQueue(_maxRetries);
        queue.OnProgress += (current, total, desc) =>
        {
            CurrentStep = current;
            TotalSteps = total;
            CurrentAction = desc;
            OnStateChanged?.Invoke();
        };
        queue.OnLog += msg => _chat.Print($"[MarketDumper] {msg}");

        foreach (var cmd in commands)
            queue.Enqueue(cmd);

        return await queue.ExecuteAsync(cancellationToken);
    }

    private async Task RunAsync(int[] freeSlotsPerRetainer, CancellationToken cancellationToken)
    {
        try
        {
            // Give the retainer UI a moment to fully settle before the first interaction
            await Task.Delay(2000, cancellationToken);

            // Consolidation pre-phase
            var rulesForConsolidation = _sellRuleManager.GetEnabledRules();
            var matchesForConsolidation = _inventoryScanner.FindMatchingItems(rulesForConsolidation);

            if (matchesForConsolidation.Count > 0)
            {
                var consolidationCmds = BuildConsolidationCommands(
                    matchesForConsolidation, rulesForConsolidation, freeSlotsPerRetainer.Length);

                if (consolidationCmds.Count > 0)
                {
                    _chat.Print("[MarketDumper] Consolidating retainer listings...");
                    var consolidationResult = await ExecuteQueueAsync(consolidationCmds, cancellationToken);

                    if (consolidationResult.Completed)
                    {
                        await _addonInteractor.ExecuteGameCommand("/itemsort execute inventory");
                        await Task.Delay(1500, cancellationToken);
                    }
                }
            }

            // Listing loop (unchanged)
            var totalListed = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rules = _sellRuleManager.GetEnabledRules();
                var matches = _inventoryScanner.FindMatchingItems(rules);

                if (matches.Count == 0)
                {
                    if (totalListed == 0)
                        _chat.Print("[MarketDumper] No matching items found in inventory.");
                    break;
                }

                var planner = new JobPlanner(_commandFactory);
                var commands = planner.GenerateCommands(matches, new(rules), freeSlotsPerRetainer);

                if (commands.Count == 0)
                {
                    if (totalListed == 0)
                        _chat.Print("[MarketDumper] No retainer slots available.");
                    break;
                }

                _chat.Print($"[MarketDumper] Starting: {commands.Count} operations queued.");

                var result = await ExecuteQueueAsync(commands, cancellationToken);
                totalListed += result.CommandsExecuted;

                if (!result.Completed)
                {
                    _chat.Print($"[MarketDumper] Stopped: {result.ErrorMessage}");
                    LastError = result.ErrorMessage;
                    break;
                }

                if (!planner.SkippedPartials)
                    break;

                _chat.Print("[MarketDumper] Sorting inventory to consolidate stacks...");
                await _addonInteractor.ExecuteGameCommand("/itemsort execute inventory");
                await Task.Delay(1500, cancellationToken);

                for (var i = 0; i < freeSlotsPerRetainer.Length; i++)
                    freeSlotsPerRetainer[i] = Math.Max(0, freeSlotsPerRetainer[i] - planner.SlotsUsedPerRetainer[i]);
            }

            _chat.Print("[MarketDumper] Done!");
            LastError = null;
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
            _addonLifecycle.UnregisterListener(OnTalkAddon);
            _addonLifecycle.UnregisterListener(OnInputNumericAddon);
            LastFinishTime = DateTime.UtcNow;
            State = AutomationState.Idle;
            CurrentAction = string.Empty;
            OnStateChanged?.Invoke();
        }
    }

    private List<ICommand> BuildConsolidationCommands(
        List<InventoryMatch> matches,
        IReadOnlyList<SellRule> rules,
        int retainerCount)
    {
        var commands = new List<ICommand>();
        for (var i = 0; i < retainerCount; i++)
        {
            commands.Add(_commandFactory.CreateSelectRetainer(i));
            commands.Add(_commandFactory.CreateOpenSellMenu());
            commands.Add(_commandFactory.CreateConsolidateListings(matches, rules));
            commands.Add(_commandFactory.CreateCloseRetainer());
        }
        return commands;
    }
}
