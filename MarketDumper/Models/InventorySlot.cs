namespace MarketDumper.Models;

public record InventorySlot(
    int ContainerIndex,
    int SlotIndex,
    int Quantity,
    bool IsHq
);
