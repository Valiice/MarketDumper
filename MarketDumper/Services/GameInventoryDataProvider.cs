using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace MarketDumper.Services;

public class GameInventoryDataProvider : IInventoryDataProvider
{
    private static readonly InventoryType[] PlayerBags =
    {
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    };

    public unsafe List<InventoryItemData> GetAllInventoryItems()
    {
        var results = new List<InventoryItemData>();
        var manager = InventoryManager.Instance();
        if (manager == null) return results;

        for (var containerIdx = 0; containerIdx < PlayerBags.Length; containerIdx++)
        {
            var container = manager->GetInventoryContainer(PlayerBags[containerIdx]);
            if (container == null) continue;

            for (var slotIdx = 0; slotIdx < container->Size; slotIdx++)
            {
                var item = container->GetInventorySlot(slotIdx);
                if (item == null || item->ItemId == 0) continue;

                results.Add(new InventoryItemData(
                    ItemId: item->ItemId,
                    Quantity: (int)item->Quantity,
                    IsHq: item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality),
                    ContainerIndex: containerIdx,
                    SlotIndex: slotIdx
                ));
            }
        }
        return results;
    }
}
