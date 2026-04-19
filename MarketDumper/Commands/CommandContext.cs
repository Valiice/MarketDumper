using System.Collections.Generic;

namespace MarketDumper.Commands;

public class CommandContext
{
    public int? CalculatedPrice { get; set; }
    public bool? IsHq { get; set; }
    public uint? CurrentItemId { get; set; }
    public int? CurrentRetainerIndex { get; set; }
    public int? PendingStackSize { get; set; }
    public List<string> Messages { get; } = new();

    public void Log(string message) => Messages.Add(message);
}
