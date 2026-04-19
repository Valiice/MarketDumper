using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MarketDumper.Services;

public class AddonInteractor : IAddonInteractor
{
    private readonly IGameGui _gameGui;
    private readonly IFramework _framework;
    private readonly IPluginLog _log;
    private readonly nint _openForItemSlotAddr;

    public AddonInteractor(IGameGui gameGui, IFramework framework, IPluginLog log, ISigScanner sigScanner)
    {
        _gameGui = gameGui;
        _framework = framework;
        _log = log;

        try
        {
            _openForItemSlotAddr = sigScanner.ScanText("83 B9 ?? ?? ?? ?? ?? 7E ?? 39 91");
            _log.Information("Resolved AgentInventoryContext.OpenForItemSlot");
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to resolve OpenForItemSlot signature: {ex.Message}");
        }
    }

    public async Task<bool> WaitForAddon(string addonName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ready = await _framework.RunOnFrameworkThread(() =>
            {
                unsafe
                {
                    var addon = GetAddon(addonName);
                    return addon != null && addon->IsVisible && addon->IsReady;
                }
            });

            if (ready)
                return true;

            await Task.Delay(100, cancellationToken);
        }

        _log.Warning($"Timed out waiting for addon: {addonName}");
        return false;
    }

    public Task<bool> IsAddonVisible(string addonName)
    {
        return _framework.RunOnFrameworkThread(() =>
        {
            unsafe
            {
                var addon = GetAddon(addonName);
                return addon != null && addon->IsVisible;
            }
        });
    }

    public Task<bool> ClickAddonButton(string addonName, int nodeIndex)
    {
        return _framework.RunOnFrameworkThread(() =>
        {
            _log.Information($"ClickAddonButton: {addonName} node {nodeIndex}");

            try
            {
                unsafe
                {
                    var addon = GetAddon(addonName);
                    if (addon == null)
                    {
                        _log.Error($"ClickAddonButton: addon {addonName} not found");
                        return false;
                    }

                    switch (addonName)
                    {
                        case "RetainerList":
                            FireCallback(addon, true, 2, nodeIndex, 0, 0);
                            return true;

                        case "SelectString":
                            FireCallback(addon, true, nodeIndex);
                            return true;

                        case "RetainerSellList":
                            FireCallback(addon, true, nodeIndex, 0);
                            return true;

                        case "RetainerSell":
                            if (nodeIndex == 0)
                                FireCallback(addon, true, 4);
                            else if (nodeIndex == 1)
                                FireCallback(addon, true, 0);
                            return true;

                        case "InputNumeric":
                            FireCallback(addon, true, nodeIndex);
                            return true;

                        case "SelectYesno":
                            FireCallback(addon, true, nodeIndex);
                            return true;

                        case "ContextMenu":
                            FireCallback(addon, true, 0, nodeIndex, 0);
                            return true;

                        case "Talk":
                            addon->FireCallbackInt(0);
                            return true;

                        case "ItemSearchResult":
                            return true;

                        default:
                            _log.Warning($"ClickAddonButton: unknown addon {addonName}");
                            addon->FireCallbackInt(nodeIndex);
                            return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"ClickAddonButton error: {ex.Message}");
                return false;
            }
        });
    }

    public Task<bool> SetAddonInputValue(string addonName, int nodeIndex, int value)
    {
        return _framework.RunOnFrameworkThread(() =>
        {
            _log.Information($"SetAddonInputValue: {addonName} node {nodeIndex} = {value}");

            try
            {
                unsafe
                {
                    var addon = GetAddon(addonName);
                    if (addon == null)
                    {
                        _log.Error($"SetAddonInputValue: addon {addonName} not found");
                        return false;
                    }

                    switch (addonName)
                    {
                        case "InputNumeric":
                            FireCallback(addon, true, value, 0);
                            return true;

                        case "RetainerSell":
                            if (nodeIndex == 0)
                                FireCallback(addon, true, 2, value);
                            else if (nodeIndex == 1)
                                FireCallback(addon, true, 3, value);
                            return true;

                        default:
                            _log.Warning($"SetAddonInputValue: unknown addon {addonName}");
                            return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"SetAddonInputValue error: {ex.Message}");
                return false;
            }
        });
    }

    public Task<string?> ReadAddonText(string addonName, int nodeIndex)
    {
        return _framework.RunOnFrameworkThread<string?>(() =>
        {
            _log.Information($"ReadAddonText: {addonName} node {nodeIndex}");

            try
            {
                unsafe
                {
                    var addon = GetAddon(addonName);
                    if (addon == null)
                        return null;

                    var nodeCount = addon->UldManager.NodeListCount;
                    if (nodeIndex >= nodeCount)
                        return null;

                    var node = addon->UldManager.NodeList[nodeIndex];
                    if (node == null)
                        return null;

                    if (node->Type == NodeType.Text)
                    {
                        var textNode = (AtkTextNode*)node;
                        if (textNode->NodeText.BufUsed > 0)
                            return textNode->NodeText.ToString();
                    }

                    if ((int)node->Type >= 1000 && node->GetComponent() != null)
                    {
                        var component = node->GetComponent();
                        if (component->UldManager.NodeListCount > 0)
                        {
                            var innerNode = component->UldManager.NodeList[0];
                            if (innerNode != null && innerNode->Type == NodeType.Text)
                            {
                                var textNode = (AtkTextNode*)innerNode;
                                if (textNode->NodeText.BufUsed > 0)
                                    return textNode->NodeText.ToString();
                            }
                        }
                    }

                    return null;
                }
            }
            catch (Exception ex)
            {
                _log.Error($"ReadAddonText error: {ex.Message}");
                return null;
            }
        });
    }

    public Task<bool> RightClickInventoryItem(int containerIndex, int slotIndex)
    {
        return _framework.RunOnFrameworkThread(() =>
        {
            _log.Information($"RightClickInventoryItem: container {containerIndex} slot {slotIndex}");

            try
            {
                unsafe
                {
                    if (_openForItemSlotAddr == nint.Zero)
                    {
                        _log.Error("RightClickInventoryItem: OpenForItemSlot not resolved");
                        return false;
                    }

                    var inventoryTypes = new[]
                    {
                        InventoryType.Inventory1,
                        InventoryType.Inventory2,
                        InventoryType.Inventory3,
                        InventoryType.Inventory4,
                    };

                    if (containerIndex < 0 || containerIndex >= inventoryTypes.Length)
                    {
                        _log.Error($"RightClickInventoryItem: invalid container index {containerIndex}");
                        return false;
                    }

                    var inventoryType = inventoryTypes[containerIndex];

                    var inventoryManager = InventoryManager.Instance();
                    if (inventoryManager == null)
                    {
                        _log.Error("RightClickInventoryItem: InventoryManager is null");
                        return false;
                    }

                    var container = inventoryManager->GetInventoryContainer(inventoryType);
                    if (container == null || slotIndex >= container->Size)
                    {
                        _log.Error($"RightClickInventoryItem: invalid slot {slotIndex} for container {containerIndex}");
                        return false;
                    }

                    var item = container->GetInventorySlot(slotIndex);
                    if (item == null || item->ItemId == 0)
                    {
                        _log.Error($"RightClickInventoryItem: no item at container {containerIndex} slot {slotIndex}");
                        return false;
                    }

                    var agent = GetAgentInventoryContext();
                    if (agent == null)
                    {
                        _log.Error("RightClickInventoryItem: AgentInventoryContext is null");
                        return false;
                    }

                    ((delegate* unmanaged<AgentInventoryContext*, InventoryType, int, int, uint, void>)_openForItemSlotAddr)(
                        agent, inventoryType, slotIndex, 0, 0);

                    _log.Information($"RightClickInventoryItem: called OpenForItemSlot for {inventoryType} slot {slotIndex}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _log.Error($"RightClickInventoryItem error: {ex.Message}");
                return false;
            }
        });
    }

    public Task ExecuteGameCommand(string command)
    {
        return _framework.RunOnFrameworkThread(() =>
        {
            _log.Information($"ExecuteGameCommand: {command}");
            try
            {
                unsafe
                {
                    var uiModule = UIModule.Instance();
                    if (uiModule == null) return;

                    var shellModule = uiModule->GetRaptureShellModule();
                    if (shellModule == null) return;

                    Utf8String utf8 = default;
                    utf8.SetString(command);
                    shellModule->ExecuteCommandInner(&utf8, uiModule);
                    utf8.Dtor();
                }
            }
            catch (Exception ex)
            {
                _log.Error($"ExecuteGameCommand error: {ex.Message}");
            }
        });
    }

    public Task<bool> RightClickRetainerListing(int slotIndex)
    {
        return _framework.RunOnFrameworkThread(() =>
        {
            _log.Information($"RightClickRetainerListing: slot {slotIndex}");
            try
            {
                unsafe
                {
                    var addon = GetAddon("RetainerSellList");
                    if (addon == null)
                    {
                        _log.Error("RightClickRetainerListing: RetainerSellList not found");
                        return false;
                    }

                    // Fire the right-click callback on the RetainerSellList addon directly.
                    // arg[0]=0 selects the row, arg[1]=slotIndex is the row, arg[2]=1 signals right-click.
                    FireCallback(addon, true, 0, slotIndex, 1);
                    _log.Information($"RightClickRetainerListing: fired FireCallback(0, {slotIndex}, 1) on RetainerSellList");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _log.Error($"RightClickRetainerListing error: {ex.Message}");
                return false;
            }
        });
    }

    public Task<bool> CloseAddon(string addonName)
    {
        return _framework.RunOnFrameworkThread(() =>
        {
            _log.Information($"CloseAddon: {addonName}");
            try
            {
                unsafe
                {
                    var addon = GetAddon(addonName);
                    if (addon == null)
                    {
                        _log.Warning($"CloseAddon: {addonName} not found");
                        return false;
                    }
                    addon->Close(true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _log.Error($"CloseAddon error: {ex.Message}");
                return false;
            }
        });
    }

    private unsafe AgentInventoryContext* GetAgentInventoryContext()
    {
        var framework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();
        if (framework == null) return null;
        var uiModule = framework->GetUIModule();
        if (uiModule == null) return null;
        var agents = uiModule->GetAgentModule();
        if (agents == null) return null;
        return (AgentInventoryContext*)agents->GetAgentByInternalId(AgentId.InventoryContext);
    }

    private unsafe AtkUnitBase* GetAddon(string name)
    {
        try
        {
            var addonPtr = _gameGui.GetAddonByName(name);
            if (addonPtr.IsNull)
                return null;
            return (AtkUnitBase*)addonPtr.Address;
        }
        catch
        {
            return null;
        }
    }

    private unsafe void FireCallback(AtkUnitBase* addon, bool updateState, params int[] args)
    {
        var atkValues = stackalloc AtkValue[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            atkValues[i].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
            atkValues[i].Int = args[i];
        }
        addon->FireCallback((uint)args.Length, atkValues, updateState);
    }
}
