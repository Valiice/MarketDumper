namespace MarketDumper.Commands;

public class CommandContext
{
    public int? CalculatedPrice { get; set; }
    public bool? IsHq { get; set; }
    public uint? CurrentItemId { get; set; }
    public int? CurrentRetainerIndex { get; set; }
}
