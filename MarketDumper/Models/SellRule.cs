using System;

namespace MarketDumper.Models;

[Serializable]
public class SellRule
{
    public uint ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int StackSize { get; set; } = 99;
    public bool AllowPartial { get; set; } = false;
    public bool Enabled { get; set; } = true;
}
