using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using MarketDumper.Automation;

namespace MarketDumper.Windows;

public class StatusOverlay : Window, IDisposable
{
    private readonly AutomationController _automation;

    public StatusOverlay(AutomationController automation)
        : base("MarketDumper Status###MarketDumperStatus",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        _automation = automation;
    }

    public void Dispose() { }

    public override bool DrawConditions()
    {
        return _automation.State != AutomationState.Idle;
    }

    public override void Draw()
    {
        ImGui.Text(_automation.CurrentAction);

        if (_automation.TotalSteps > 0)
        {
            var progress = (float)_automation.CurrentStep / _automation.TotalSteps;
            ImGui.ProgressBar(progress, new Vector2(300, 0),
                $"{_automation.CurrentStep}/{_automation.TotalSteps}");
        }

        if (_automation.LastError != null)
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), _automation.LastError);

        ImGui.Spacing();

        if (ImGui.Button("Stop", new Vector2(300, 0)))
            _automation.Stop();
    }
}
