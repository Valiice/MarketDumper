using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MarketDumper.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration _configuration;

    public ConfigWindow(Configuration configuration)
        : base("MarketDumper - Config###MarketDumperConfig")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(350, 280);
        SizeCondition = ImGuiCond.FirstUseEver;
        _configuration = configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var config = _configuration.PricingConfig;

        ImGui.Text("Undercut Settings");
        ImGui.Separator();

        var delta = config.Delta;
        if (ImGui.InputInt("Undercut Amount (Delta)", ref delta))
        {
            config.Delta = Math.Max(0, delta);
            _configuration.Save();
        }

        var mod = config.Mod;
        if (ImGui.InputInt("Price Modulo (Mod)", ref mod))
        {
            config.Mod = Math.Max(1, mod);
            _configuration.Save();
        }

        var multiple = config.Multiple;
        if (ImGui.InputInt("Round to Multiple", ref multiple))
        {
            config.Multiple = Math.Max(1, multiple);
            _configuration.Save();
        }

        var minPrice = config.MinPrice;
        if (ImGui.InputInt("Minimum Price", ref minPrice))
        {
            config.MinPrice = Math.Max(1, minPrice);
            _configuration.Save();
        }

        ImGui.Spacing();

        var undercutSelf = config.UndercutSelf;
        if (ImGui.Checkbox("Undercut own retainers", ref undercutSelf))
        {
            config.UndercutSelf = undercutSelf;
            _configuration.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Defaults: Delta=1, Mod=1, Multiple=1, Min=1");
        ImGui.TextDisabled("With defaults, this is a simple 1-gil undercut.");
    }
}
