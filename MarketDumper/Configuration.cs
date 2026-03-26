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

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
