using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.NativeWrapper;
using Dalamud.Plugin.Services;

namespace MarketDumper.Services;

public class AddonInteractor : IAddonInteractor
{
    private readonly IGameGui _gameGui;
    private readonly IPluginLog _log;

    public AddonInteractor(IGameGui gameGui, IPluginLog log)
    {
        _gameGui = gameGui;
        _log = log;
    }

    public async Task<bool> WaitForAddon(string addonName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsAddonVisible(addonName))
                return true;
            await Task.Delay(100, cancellationToken);
        }
        _log.Warning($"Timed out waiting for addon: {addonName}");
        return false;
    }

    public bool IsAddonVisible(string addonName)
    {
        var addon = GetAddon(addonName);
        return !addon.IsNull && addon.IsVisible;
    }

    public bool ClickAddonButton(string addonName, int nodeIndex)
    {
        _log.Information($"ClickAddonButton: {addonName} node {nodeIndex}");
        return true; // Stub - will be implemented during integration testing
    }

    public bool SetAddonInputValue(string addonName, int nodeIndex, int value)
    {
        _log.Information($"SetAddonInputValue: {addonName} node {nodeIndex} = {value}");
        return true; // Stub - will be implemented during integration testing
    }

    public string? ReadAddonText(string addonName, int nodeIndex)
    {
        _log.Information($"ReadAddonText: {addonName} node {nodeIndex}");
        return null; // Stub - will be implemented during integration testing
    }

    private AtkUnitBasePtr GetAddon(string name)
    {
        try
        {
            return _gameGui.GetAddonByName(name);
        }
        catch
        {
            return new AtkUnitBasePtr(nint.Zero);
        }
    }
}
