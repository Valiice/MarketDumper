using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MarketDumper.Services;

public class AddonInteractor : IAddonInteractor
{
    private readonly IGameGui _gameGui;
    private readonly IFramework _framework;
    private readonly IPluginLog _log;

    public AddonInteractor(IGameGui gameGui, IFramework framework, IPluginLog log)
    {
        _gameGui = gameGui;
        _framework = framework;
        _log = log;
    }

    public async Task<bool> WaitForAddon(string addonName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ready = await RunOnFrameworkThread(() =>
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

    public bool IsAddonVisible(string addonName)
    {
        unsafe
        {
            var addon = GetAddon(addonName);
            return addon != null && addon->IsVisible;
        }
    }

    public bool ClickAddonButton(string addonName, int nodeIndex)
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
                        // Fire callback: select retainer by index
                        // CallbackType 2, params: [Int 2, Int retainerIndex, Int 0]
                        FireCallback(addon, 3, 2, nodeIndex, 0);
                        return true;

                    case "SelectString":
                        // Fire callback: select menu option by index
                        FireCallback(addon, 1, nodeIndex);
                        return true;

                    case "RetainerSellList":
                        // Fire callback: select inventory slot
                        FireCallback(addon, 2, nodeIndex, 0);
                        return true;

                    case "RetainerSell":
                        // nodeIndex 1 = "Compare Prices" button
                        // nodeIndex 2 = "List Item" / confirm button
                        if (nodeIndex == 1)
                        {
                            // Compare Prices
                            FireCallback(addon, 1, 0);
                        }
                        else if (nodeIndex == 2)
                        {
                            // Confirm / List Item
                            FireCallback(addon, 1, 0);
                        }
                        return true;

                    case "InputNumeric":
                        // nodeIndex 1 = OK button - handled via SetAddonInputValue + confirm
                        FireCallback(addon, 1, nodeIndex);
                        return true;

                    case "SelectYesno":
                        // nodeIndex 0 = Yes, nodeIndex 1 = No
                        FireCallback(addon, 1, nodeIndex);
                        return true;

                    case "ContextMenu":
                        FireCallback(addon, 2, 0, nodeIndex);
                        return true;

                    case "Talk":
                        // Click to advance/dismiss dialogue
                        addon->FireCallbackInt(0);
                        return true;

                    case "ItemSearchResult":
                        // Usually just need to wait for it, not click
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
    }

    public bool SetAddonInputValue(string addonName, int nodeIndex, int value)
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
                        // Fire callback with the numeric value to set and confirm
                        FireCallback(addon, 2, value, 0);
                        return true;

                    case "RetainerSell":
                        // Set the price value - fire callback with price
                        FireCallback(addon, 4, value);
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
    }

    public string? ReadAddonText(string addonName, int nodeIndex)
    {
        _log.Information($"ReadAddonText: {addonName} node {nodeIndex}");

        try
        {
            unsafe
            {
                var addon = GetAddon(addonName);
                if (addon == null)
                    return null;

                // Walk the node list to find the text node at the given index
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

                // Try reading component text nodes
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
    }

    public bool RightClickInventoryItem(int containerIndex, int slotIndex)
    {
        _log.Information($"RightClickInventoryItem: container {containerIndex} slot {slotIndex}");

        try
        {
            unsafe
            {
                var inventoryTypes = new[]
                {
                    FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory1,
                    FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory2,
                    FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory3,
                    FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory4,
                };

                if (containerIndex < 0 || containerIndex >= inventoryTypes.Length)
                {
                    _log.Error($"RightClickInventoryItem: invalid container index {containerIndex}");
                    return false;
                }

                var inventoryType = inventoryTypes[containerIndex];

                var inventoryManager = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
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

                // Use the inventory grid addon to trigger context menu
                // The inventory addon name depends on which tab is active
                var gridAddonName = $"InventoryGrid{containerIndex}E";
                var gridAddon = GetAddon(gridAddonName);
                if (gridAddon == null)
                {
                    gridAddonName = $"InventoryGrid{containerIndex}";
                    gridAddon = GetAddon(gridAddonName);
                }

                if (gridAddon != null)
                {
                    // Fire callback on inventory grid: type 2 (right-click), slot index
                    FireCallback(gridAddon, 3, 2, slotIndex, 1);
                    _log.Information($"RightClickInventoryItem: fired callback on {gridAddonName} slot {slotIndex}");
                    return true;
                }

                // Fallback: try InventoryExpansion
                var expansionAddon = GetAddon("InventoryExpansion");
                if (expansionAddon != null)
                {
                    var globalSlot = containerIndex * 35 + slotIndex;
                    FireCallback(expansionAddon, 3, 2, globalSlot, 1);
                    _log.Information($"RightClickInventoryItem: fired callback on InventoryExpansion slot {globalSlot}");
                    return true;
                }

                _log.Error("RightClickInventoryItem: could not find inventory addon");
                return false;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"RightClickInventoryItem error: {ex.Message}");
            return false;
        }
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

    private unsafe void FireCallback(AtkUnitBase* addon, int numArgs, params int[] args)
    {
        var atkValues = stackalloc AtkValue[numArgs];
        for (var i = 0; i < numArgs && i < args.Length; i++)
        {
            atkValues[i].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
            atkValues[i].Int = args[i];
        }
        addon->FireCallback((uint)numArgs, atkValues);
    }

    private async Task<T> RunOnFrameworkThread<T>(Func<T> func)
    {
        return await _framework.RunOnFrameworkThread(func);
    }
}
