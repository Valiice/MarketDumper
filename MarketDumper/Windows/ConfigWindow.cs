using System;
using Dalamud.Interface.Windowing;

namespace MarketDumper.Windows;

public class ConfigWindow : Window, IDisposable
{
    public ConfigWindow() : base("MarketDumper Config###MarketDumperConfig")
    {
    }

    public void Dispose() { }
    public override void Draw() { }
}
