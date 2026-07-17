using Dalamud.Configuration;
using MarketDumper.Models;
using System;
using System.Collections.Generic;

namespace MarketDumper;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public List<SellRule> SellRules { get; set; } = new();
    public PricingConfig PricingConfig { get; set; } = new();
    public bool AutoDumpEnabled { get; set; } = true;
    public int AutoDumpDelaySeconds { get; set; } = 5;
    public bool DebugLogging { get; set; } = false;
    public Dictionary<ulong, Dictionary<ulong, RetainerSnapshot>> RetainerSnapshots { get; set; } = new();

    // Serializes saves against collection mutations: the UI thread and the
    // automation task both mutate this object and save it.
    public static readonly object SyncRoot = new();

    public void Save()
    {
        lock (SyncRoot)
        {
            Plugin.PluginInterface.SavePluginConfig(this);
        }
    }
}
