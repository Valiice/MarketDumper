using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Keys;
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
    private readonly ConsolidationPlanner _planner;
    private readonly IRetainerSnapshotCache _snapshotCache;
    private readonly IFramework _framework;
    private readonly IKeyState _keyState;
    private readonly Func<RetainerInfo[]> _getRetainerInfo;
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
        ConsolidationPlanner planner,
        IRetainerSnapshotCache snapshotCache,
        IFramework framework,
        IKeyState keyState,
        Func<RetainerInfo[]> getRetainerInfo,
        int maxRetries = 3)
    {
        _sellRuleManager = sellRuleManager;
        _inventoryScanner = inventoryScanner;
        _commandFactory = commandFactory;
        _log = log;
        _chat = chat;
        _addonLifecycle = addonLifecycle;
        _addonInteractor = addonInteractor;
        _planner = planner;
        _snapshotCache = snapshotCache;
        _framework = framework;
        _keyState = keyState;
        _getRetainerInfo = getRetainerInfo;
        _maxRetries = maxRetries;
    }

    public void SetCommandFactory(ICommandFactory commandFactory) => _commandFactory = commandFactory;

    public void Start(TimeSpan? startDelay = null)
    {
        if (State != AutomationState.Idle)
            return;

        _cts = new CancellationTokenSource();
        State = AutomationState.Running;
        LastError = null;
        OnStateChanged?.Invoke();

        try
        {
            _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "Talk", OnTalkAddon);
            _addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "Talk", OnTalkAddon);
            _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "InputNumeric", OnInputNumericAddon);

            _framework.Update += OnFrameworkUpdate;
            var retainerInfo = _getRetainerInfo();
            // Default settle delay keeps the retainer UI from being clicked mid-open
            var delay = startDelay ?? TimeSpan.FromSeconds(2);
            _runTask = Task.Run(() => RunAsync(retainerInfo, delay, _cts.Token));
        }
        catch (Exception ex)
        {
            _log.Error($"Start failed: {ex}");
            _framework.Update -= OnFrameworkUpdate;
            _addonLifecycle.UnregisterListener(OnTalkAddon);
            _addonLifecycle.UnregisterListener(OnInputNumericAddon);
            State = AutomationState.Idle;
            LastError = ex.Message;
            OnStateChanged?.Invoke();
        }
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
        // A run stalled past the wait would otherwise leave these subscribed after unload.
        _framework.Update -= OnFrameworkUpdate;
        _addonLifecycle.UnregisterListener(OnTalkAddon);
        _addonLifecycle.UnregisterListener(OnInputNumericAddon);
        _cts?.Dispose();
    }

    private void DumpRunSnapshot(
        IReadOnlyList<SellRule> rules,
        IReadOnlyList<InventoryMatch> matches,
        RetainerInfo[] retainers,
        int freeInventorySlots)
    {
        _log.Information("[Diag] ===== run snapshot =====");
        _log.Information($"[Diag] Free inventory slots: {freeInventorySlots}");
        foreach (var r in rules)
            _log.Information($"[Diag] Rule: '{r.ItemName}' (item {r.ItemId}) stack={r.StackSize} partial={r.AllowPartial} enabled={r.Enabled}");
        foreach (var m in matches)
        {
            var slots = string.Join(", ", m.Slots.Select(s => $"c{s.ContainerIndex}/s{s.SlotIndex}x{s.Quantity}"));
            _log.Information($"[Diag] Inventory: item {m.ItemId} total={m.TotalQuantity} in [{slots}]");
        }
        for (var i = 0; i < retainers.Length; i++)
        {
            var r = retainers[i];
            _log.Information($"[Diag] Retainer {i}: id={r.RetainerId} listings={r.MarketItemCount} gil={r.Gil} freeSellSlots={r.FreeSellSlots}");
        }
        _log.Information("[Diag] ========================");
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (State == AutomationState.Running && _keyState[VirtualKey.ESCAPE])
        {
            _chat.Print("[MarketDumper] Escape pressed — aborting.");
            Stop();
        }
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

    private async Task RunAsync(RetainerInfo[] initialRetainers, TimeSpan startDelay, CancellationToken cancellationToken)
    {
        try
        {
            var retainers = initialRetainers;

            // Announced grace period: the run can be aborted before anything is touched.
            if (startDelay > TimeSpan.FromSeconds(2))
                _chat.Print($"[MarketDumper] Auto-starting in {(int)startDelay.TotalSeconds}s — press Escape to cancel.");
            await Task.Delay(startDelay, cancellationToken);

            // Sort player inventory first to merge stacks and free up slots for returned items
            _chat.Print("[MarketDumper] Sorting inventory to free up slots before consolidation...");
            await _addonInteractor.ExecuteGameCommand("/itemsort execute inventory");
            await Task.Delay(1500, cancellationToken);

            // Immutable snapshot: UI edits mid-run apply from the next run.
            var runRules = _sellRuleManager.GetEnabledRulesSnapshot();
            var matchesForConsolidation = _inventoryScanner.FindMatchingItems(runRules);
            var freeInventorySlots = await _addonInteractor.GetFreeInventorySlots();

            if (Diag.Enabled)
                DumpRunSnapshot(runRules, matchesForConsolidation, retainers, freeInventorySlots);

            var consolidationCmds = new List<ICommand>();
            for (var i = 0; i < retainers.Length; i++)
            {
                var info = retainers[i];
                var cached = _snapshotCache.TryGetValid(info.RetainerId, info.MarketItemCount, info.Gil, out var cachedListings)
                    ? cachedListings
                    : null;

                var plan = _planner.PlanRetainer(cached, matchesForConsolidation, runRules, freeInventorySlots);
                if (Diag.Enabled)
                {
                    var cacheState = cached == null ? "cache MISS/invalid" : $"cache valid ({cached.Count} listings)";
                    var actions = plan.Actions.Count == 0
                        ? "no actions"
                        : string.Join(", ", plan.Actions.Select(a => $"return item {a.ItemId} x{a.Quantity} (slot {a.SlotIndex})"));
                    _log.Information($"[Diag] Retainer {i}: {cacheState} -> plan {plan.Kind}: {actions}");
                }
                if (plan.Kind == RetainerPlanKind.Skip)
                {
                    _log.Information($"[Consolidate] Retainer {i}: cache valid, nothing to do — skipping");
                    continue;
                }

                consolidationCmds.Add(_commandFactory.CreateSelectRetainer(i));
                consolidationCmds.Add(_commandFactory.CreateOpenSellMenu());
                // Matches are captured once per run; the command re-plans from a fresh read.
                consolidationCmds.Add(_commandFactory.CreateConsolidateListings(
                    matchesForConsolidation, runRules, info.RetainerId, info.Gil));
                consolidationCmds.Add(_commandFactory.CreateCloseRetainer());
            }

            if (consolidationCmds.Count > 0)
            {
                _chat.Print("[MarketDumper] Consolidating retainer listings...");
                var consolidationResult = await ExecuteQueueAsync(consolidationCmds, cancellationToken);

                if (consolidationResult.Completed)
                {
                    await _addonInteractor.ExecuteGameCommand("/itemsort execute inventory");
                    await Task.Delay(1500, cancellationToken);
                }

                // Returns freed sell slots the Start()-time snapshot doesn't know about.
                retainers = await _framework.RunOnFrameworkThread(() => _getRetainerInfo());
            }

            // Listing loop
            var freeSlotsPerRetainer = retainers.Select(r => r.FreeSellSlots).ToArray();
            var totalListed = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rules = runRules;
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

                // Listing changed retainer contents; force a re-read next run.
                for (var i = 0; i < retainers.Length; i++)
                    if (planner.SlotsUsedPerRetainer[i] > 0)
                        _snapshotCache.Invalidate(retainers[i].RetainerId);

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
            _snapshotCache.Flush();
            _framework.Update -= OnFrameworkUpdate;
            _addonLifecycle.UnregisterListener(OnTalkAddon);
            _addonLifecycle.UnregisterListener(OnInputNumericAddon);
            LastFinishTime = DateTime.UtcNow;
            State = AutomationState.Idle;
            CurrentAction = string.Empty;
            OnStateChanged?.Invoke();
        }
    }
}
