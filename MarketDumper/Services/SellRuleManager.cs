using System;
using System.Collections.Generic;
using System.Linq;
using MarketDumper.Models;

namespace MarketDumper.Services;

public class SellRuleManager : ISellRuleManager
{
    private readonly List<SellRule> _rules;
    private readonly Action _saveConfig;

    public SellRuleManager(List<SellRule> rules, Action saveConfig)
    {
        _rules = rules;
        _saveConfig = saveConfig;
    }

    public IReadOnlyList<SellRule> GetAllRules() => _rules.AsReadOnly();

    public IReadOnlyList<SellRule> GetEnabledRules() =>
        _rules.Where(r => r.Enabled).ToList().AsReadOnly();

    public bool AddRule(SellRule rule)
    {
        if (_rules.Any(r => r.ItemId == rule.ItemId))
            return false;
        _rules.Add(rule);
        return true;
    }

    public bool RemoveRule(uint itemId)
    {
        var index = _rules.FindIndex(r => r.ItemId == itemId);
        if (index < 0) return false;
        _rules.RemoveAt(index);
        return true;
    }

    public bool UpdateRule(SellRule rule)
    {
        var index = _rules.FindIndex(r => r.ItemId == rule.ItemId);
        if (index < 0) return false;
        _rules[index] = rule;
        return true;
    }

    public bool HasRule(uint itemId) => _rules.Any(r => r.ItemId == itemId);

    public void Save() => _saveConfig();
}
