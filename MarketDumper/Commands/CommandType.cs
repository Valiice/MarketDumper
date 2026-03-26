namespace MarketDumper.Commands;

public enum CommandType
{
    SelectRetainer,
    OpenSellMenu,
    SelectItem,
    FetchMarketPrice,
    SetPrice,
    ConfirmListing
}

public enum CommandStatus
{
    Success,
    Retry,
    Abort
}
