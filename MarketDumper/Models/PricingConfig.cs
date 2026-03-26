using System;

namespace MarketDumper.Models;

[Serializable]
public class PricingConfig
{
    public int Delta { get; set; } = 1;
    public int Mod { get; set; } = 1;
    public int Multiple { get; set; } = 1;
    public int MinPrice { get; set; } = 1;
    public bool UndercutSelf { get; set; } = false;
}
