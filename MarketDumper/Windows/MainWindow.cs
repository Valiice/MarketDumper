using System;
using Dalamud.Interface.Windowing;

namespace MarketDumper.Windows;

public class MainWindow : Window, IDisposable
{
    public MainWindow() : base("MarketDumper###MarketDumperMain")
    {
    }

    public void Dispose() { }
    public override void Draw() { }
}
