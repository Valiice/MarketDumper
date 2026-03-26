using System.Collections.Generic;
using MarketDumper.Models;

namespace MarketDumper.Services;

public interface ISellRuleManager
{
    IReadOnlyList<SellRule> GetAllRules();
    IReadOnlyList<SellRule> GetEnabledRules();
    bool AddRule(SellRule rule);
    bool RemoveRule(uint itemId);
    bool UpdateRule(SellRule rule);
    bool HasRule(uint itemId);
    void Save();
}
