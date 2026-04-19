using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using MarketDumper.Automation;
using MarketDumper.Services;

namespace MarketDumper.Windows;

public class SellRulesWindow : Window, IDisposable
{
    private readonly ISellRuleManager _sellRuleManager;
    private readonly AutomationController _automation;
    private readonly Configuration _configuration;

    public SellRulesWindow(ISellRuleManager sellRuleManager, AutomationController automation, Configuration configuration)
        : base("MarketDumper - Sell Rules###MarketDumperRules")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(450, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        _sellRuleManager = sellRuleManager;
        _automation = automation;
        _configuration = configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var rules = _sellRuleManager.GetAllRules();

        if (ImGui.BeginTable("SellRulesTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Stack Size", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("##delete", ImGuiTableColumnFlags.WidthFixed, 30);
            ImGui.TableHeadersRow();

            uint? toRemove = null;
            foreach (var rule in rules)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(rule.ItemName);

                ImGui.TableNextColumn();
                var stackSize = rule.StackSize;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputInt($"##stack_{rule.ItemId}", ref stackSize))
                {
                    rule.StackSize = Math.Clamp(stackSize, 1, 999);
                    _sellRuleManager.Save();
                }

                ImGui.TableNextColumn();
                var enabled = rule.Enabled;
                if (ImGui.Checkbox($"##enabled_{rule.ItemId}", ref enabled))
                {
                    rule.Enabled = enabled;
                    _sellRuleManager.Save();
                }

                ImGui.TableNextColumn();
                if (ImGui.Button($"X##del_{rule.ItemId}"))
                    toRemove = rule.ItemId;
            }

            ImGui.EndTable();

            if (toRemove.HasValue)
            {
                _sellRuleManager.RemoveRule(toRemove.Value);
                _sellRuleManager.Save();
            }
        }

        if (rules.Count == 0)
            ImGui.TextDisabled("No sell rules. Right-click items to add them.");

        ImGui.Separator();

        var isRunning = _automation.State != AutomationState.Idle;
        var hasEnabledRules = _sellRuleManager.GetEnabledRules().Count > 0;

        if (isRunning)
        {
            if (ImGui.Button("Stop Dumping"))
                _automation.Stop();
        }
        else
        {
            if (!hasEnabledRules)
                ImGui.BeginDisabled();

            if (ImGui.Button("Start Dumping"))
                _automation.Start();

            if (!hasEnabledRules)
                ImGui.EndDisabled();
        }

        ImGui.SameLine();
        var autoDump = _configuration.AutoDumpEnabled;
        if (ImGui.Checkbox("Auto-dump on bell open", ref autoDump))
        {
            _configuration.AutoDumpEnabled = autoDump;
            _configuration.Save();
        }
    }
}
