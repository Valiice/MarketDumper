using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using MarketDumper.Models;

namespace MarketDumper.Services;

public class RetainerListingReader : IRetainerListingReader
{
    private readonly IFramework _framework;
    private readonly IPluginLog _log;

    public RetainerListingReader(IFramework framework, IPluginLog log)
    {
        _framework = framework;
        _log = log;
    }

    public Task<List<RetainerListing>> ReadListingsAsync()
    {
        return _framework.RunOnFrameworkThread(() =>
        {
            var result = new List<RetainerListing>();
            try
            {
                unsafe
                {
                    var container = InventoryManager.Instance()
                        ->GetInventoryContainer(InventoryType.RetainerMarket);
                    if (container == null)
                    {
                        _log.Warning("RetainerListingReader: RetainerMarket container is null");
                        return result;
                    }

                    for (var i = 0; i < container->Size; i++)
                    {
                        var slot = container->GetInventorySlot(i);
                        if (slot == null || slot->ItemId == 0) continue;
                        result.Add(new RetainerListing(i, slot->ItemId, (int)slot->Quantity));
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"RetainerListingReader error: {ex.Message}");
            }
            return result;
        });
    }
}
