namespace MarketDumper.Services;

/// <summary>
/// Global toggle for diagnostic logging. When enabled, the automation writes
/// full state snapshots (inventory, retainer holdings, cache verdicts, planner
/// decisions, on-screen row texts) to the Dalamud log with a [Diag] prefix.
/// </summary>
public static class Diag
{
    public static volatile bool Enabled;
}
