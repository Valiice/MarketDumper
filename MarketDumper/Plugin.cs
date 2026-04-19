using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using MarketDumper.Automation;
using MarketDumper.Commands;
using MarketDumper.Models;
using MarketDumper.Services;
using MarketDumper.Windows;
using Lumina.Excel.Sheets;

namespace MarketDumper;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IMarketBoard MarketBoard { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    private const string CommandName = "/marketdumper";

    public Configuration Configuration { get; init; }
    public readonly WindowSystem WindowSystem = new("MarketDumper");

    private readonly PricingService _pricingService;
    private readonly SellRuleManager _sellRuleManager;
    private readonly GameInventoryDataProvider _inventoryDataProvider;
    private readonly InventoryScanner _inventoryScanner;
    private readonly AddonInteractor _addonInteractor;
    private readonly MarketDataProvider _marketDataProvider;
    private readonly CommandFactory _commandFactory;
    private readonly AutomationController _automation;

    private readonly SellRulesWindow _sellRulesWindow;
    private readonly ConfigWindow _configWindow;
    private readonly StatusOverlay _statusOverlay;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _pricingService = new PricingService();
        _sellRuleManager = new SellRuleManager(Configuration.SellRules, Configuration.Save);
        _inventoryDataProvider = new GameInventoryDataProvider();
        _inventoryScanner = new InventoryScanner(_inventoryDataProvider);
        _addonInteractor = new AddonInteractor(GameGui, Framework, Log, SigScanner);
        _marketDataProvider = new MarketDataProvider(MarketBoard, Configuration, Log);
        var retainerListingReader = new RetainerListingReader(Framework, Log);
        _automation = new AutomationController(
            _sellRuleManager, _inventoryScanner, null!, Log, Chat, AddonLifecycle, _addonInteractor,
            GetRetainerFreeSlots);
        _commandFactory = new CommandFactory(
            _addonInteractor, _pricingService, _marketDataProvider, retainerListingReader,
            timeout: TimeSpan.FromSeconds(5),
            interactionDelay: TimeSpan.FromMilliseconds(500),
            setPendingStackSize: size => _automation.PendingStackSize = size);
        _automation.SetCommandFactory(_commandFactory);

        _sellRulesWindow = new SellRulesWindow(_sellRuleManager, _automation, Configuration);
        _configWindow = new ConfigWindow(Configuration);
        _statusOverlay = new StatusOverlay(_automation);

        WindowSystem.AddWindow(_sellRulesWindow);
        WindowSystem.AddWindow(_configWindow);
        WindowSystem.AddWindow(_statusOverlay);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the MarketDumper sell rules window."
        });

        ContextMenu.OnMenuOpened += OnContextMenuOpened;
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerList", OnRetainerListOpened);

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information("MarketDumper loaded.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        ContextMenu.OnMenuOpened -= OnContextMenuOpened;
        AddonLifecycle.UnregisterListener(OnRetainerListOpened);

        WindowSystem.RemoveAllWindows();
        _sellRulesWindow.Dispose();
        _configWindow.Dispose();
        _statusOverlay.Dispose();
        _marketDataProvider.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args) => _sellRulesWindow.Toggle();
    public void ToggleConfigUi() => _configWindow.Toggle();
    public void ToggleMainUi() => _sellRulesWindow.Toggle();

    private static unsafe int[] GetRetainerFreeSlots()
    {
        var rm = RetainerManager.Instance();
        if (rm == null)
            return new[] { 20 };

        var count = (int)rm->GetRetainerCount();
        if (count == 0)
            return new[] { 20 };

        var freeSlots = new int[count];
        for (var i = 0; i < count; i++)
        {
            var retainer = rm->GetRetainerBySortedIndex((uint)i);
            var used = retainer != null ? (int)retainer->MarketItemCount : 0;
            freeSlots[i] = Math.Max(0, 20 - used);
        }
        return freeSlots;
    }

    private void OnRetainerListOpened(AddonEvent type, AddonArgs args)
    {
        if (!Configuration.AutoDumpEnabled)
            return;
        if (_automation.State != AutomationState.Idle)
            return;

        // Suppress re-trigger when CloseRetainerCommand returns to RetainerList after a run
        if (_automation.LastFinishTime.HasValue &&
            DateTime.UtcNow - _automation.LastFinishTime.Value < TimeSpan.FromSeconds(5))
            return;

        var rules = _sellRuleManager.GetEnabledRules();
        if (rules.Count == 0)
            return;

        var matches = _inventoryScanner.FindMatchingItems(rules);
        if (matches.Count == 0)
            return;

        _automation.Start();
    }

    private void OnContextMenuOpened(Dalamud.Game.Gui.ContextMenu.IMenuOpenedArgs args)
    {
        if (args.Target is not Dalamud.Game.Gui.ContextMenu.MenuTargetInventory inventoryTarget)
            return;

        var targetItem = inventoryTarget.TargetItem;
        if (targetItem is not { } item)
            return;

        var itemId = item.ItemId;

        if (!DataManager.GetExcelSheet<Item>().TryGetRow(itemId, out var itemRow))
            return;

        if (itemRow.ItemSearchCategory.RowId == 0)
            return;

        var label = _sellRuleManager.HasRule(itemId)
            ? "MarketDumper: Edit Rule"
            : "MarketDumper: Add Sell Rule";

        args.AddMenuItem(new Dalamud.Game.Gui.ContextMenu.MenuItem
        {
            Name = label,
            UseDefaultPrefix = true,
            OnClicked = _ =>
            {
                if (!_sellRuleManager.HasRule(itemId))
                {
                    var rule = new SellRule
                    {
                        ItemId = itemId,
                        ItemName = itemRow.Name.ToString(),
                        StackSize = 99,
                        Enabled = true
                    };
                    _sellRuleManager.AddRule(rule);
                    Configuration.Save();
                    Chat.Print($"[MarketDumper] Added sell rule: {rule.ItemName} x{rule.StackSize}");
                }
                _sellRulesWindow.IsOpen = true;
            }
        });
    }
}
