namespace MarketDumper.Commands;

public enum CommandType
{
    SelectRetainer,
    OpenSellMenu,
    SelectItem,
    FetchMarketPrice,
    SetPrice,
    ConfirmListing,
    CloseRetainer
}

public enum CommandStatus
{
    Success,
    Retry,
    Abort
}
