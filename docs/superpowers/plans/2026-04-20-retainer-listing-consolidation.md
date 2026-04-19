# Retainer Listing Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Before listing new items, return any existing retainer listings that are below the configured StackSize (when the same item is in the player's inventory) so items consolidate into fewer, larger listings.

**Architecture:** A consolidation pre-phase runs in `AutomationController.RunAsync()` before the existing listing loop. For each retainer it emits `SelectRetainer → OpenSellMenu → ConsolidateListings → CloseRetainer`. After all retainers, inventory is sorted. The existing listing loop then re-scans and lists with consolidated slot data. `ConsolidateRetainerListingsCommand` reads the open `RetainerSellList` market container at execution time and returns qualifying listings via right-click context menu.

**Tech Stack:** C# 12, Dalamud.NET.Sdk 14.0.2, FFXIVClientStructs (bundled with SDK), xUnit, Moq

---

## File map

| Action | Path |
|--------|------|
| Create | `MarketDumper/Models/RetainerListing.cs` |
| Create | `MarketDumper/Services/IRetainerListingReader.cs` |
| Create | `MarketDumper/Services/RetainerListingReader.cs` |
| Modify | `MarketDumper/Services/IAddonInteractor.cs` |
| Modify | `MarketDumper/Services/AddonInteractor.cs` |
| Create | `MarketDumper/Commands/ConsolidateRetainerListingsCommand.cs` |
| Modify | `MarketDumper/Commands/CommandType.cs` |
| Modify | `MarketDumper/Commands/ICommandFactory.cs` |
| Modify | `MarketDumper/Commands/CommandFactory.cs` |
| Modify | `MarketDumper/Automation/AutomationController.cs` |
| Modify | `MarketDumper/Plugin.cs` |
| Create | `MarketDumper.Tests/ConsolidateRetainerListingsCommandTests.cs` |

---

## Task 1: Add `RetainerListing` model

**Files:**
- Create: `MarketDumper/Models/RetainerListing.cs`

- [ ] **Step 1: Create the file**

```csharp
namespace MarketDumper.Models;

public record RetainerListing(int SlotIndex, uint ItemId, int Quantity);
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build MarketDumper/MarketDumper.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add MarketDumper/Models/RetainerListing.cs
git commit -m "Add RetainerListing model"
```

---

## Task 2: Add `IRetainerListingReader` interface

**Files:**
- Create: `MarketDumper/Services/IRetainerListingReader.cs`

- [ ] **Step 1: Create the interface**

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using MarketDumper.Models;

namespace MarketDumper.Services;

public interface IRetainerListingReader
{
    Task<List<RetainerListing>> ReadListingsAsync();
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build MarketDumper/MarketDumper.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add MarketDumper/Services/IRetainerListingReader.cs
git commit -m "Add IRetainerListingReader interface"
```

---

## Task 3: TDD `ConsolidateRetainerListingsCommand`

**Files:**
- Create: `MarketDumper/Commands/ConsolidateRetainerListingsCommand.cs`
- Create: `MarketDumper.Tests/ConsolidateRetainerListingsCommandTests.cs`

The command reads the open retainer's market listings, filters to items that (1) are in the player's inventory, (2) match a sell rule, and (3) have qty < that rule's StackSize. It then right-clicks each qualifying listing in **reverse slot order** (so higher slot indices are returned first — prevents index shifting as earlier slots disappear) and clicks "Return Items to Inventory".

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Commands;
using MarketDumper.Models;
using MarketDumper.Services;
using Moq;
using Xunit;

namespace MarketDumper.Tests;

public class ConsolidateRetainerListingsCommandTests
{
    private readonly Mock<IRetainerListingReader> _reader = new();
    private readonly Mock<IAddonInteractor> _addon = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static IReadOnlyList<SellRule> Rules(uint itemId, int stackSize = 99) =>
        new List<SellRule> { new() { ItemId = itemId, StackSize = stackSize, Enabled = true } };

    private static List<InventoryMatch> Matches(params uint[] itemIds)
    {
        var list = new List<InventoryMatch>();
        foreach (var id in itemIds)
            list.Add(new InventoryMatch(id, 10, new List<InventorySlot> { new(0, 0, 10, false) }));
        return list;
    }

    private ConsolidateRetainerListingsCommand Build(
        List<InventoryMatch> matches,
        IReadOnlyList<SellRule> rules) =>
        new(_reader.Object, _addon.Object, matches, rules, TimeSpan.FromSeconds(5));

    [Fact]
    public async Task Execute_NoListings_ReturnsSuccess()
    {
        _reader.Setup(r => r.ReadListingsAsync()).ReturnsAsync(new List<RetainerListing>());

        var result = await Build(Matches(1001), Rules(1001))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.RightClickRetainerListing(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Execute_ListingBelowStackSize_ItemInInventory_ReturnsIt()
    {
        _reader.Setup(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing> { new(0, 1001, 10) });

        _addon.Setup(a => a.RightClickRetainerListing(0)).ReturnsAsync(true);
        _addon.Setup(a => a.WaitForAddon("ContextMenu", It.IsAny<TimeSpan>(), _ct)).ReturnsAsync(true);
        _addon.Setup(a => a.ClickAddonButton("ContextMenu", 2)).ReturnsAsync(true);

        var result = await Build(Matches(1001), Rules(1001, stackSize: 99))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.RightClickRetainerListing(0), Times.Once);
        _addon.Verify(a => a.ClickAddonButton("ContextMenu", 2), Times.Once);
    }

    [Fact]
    public async Task Execute_ListingAtStackSize_NotReturned()
    {
        // A listing of exactly 99 when StackSize=99 should NOT be returned
        _reader.Setup(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing> { new(0, 1001, 99) });

        var result = await Build(Matches(1001), Rules(1001, stackSize: 99))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.RightClickRetainerListing(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Execute_ItemNotInInventory_NotReturned()
    {
        // Listing for item 1002, but player only has 1001
        _reader.Setup(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing> { new(0, 1002, 10) });

        var result = await Build(Matches(1001), Rules(1002, stackSize: 99))
            .ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(CommandStatus.Success, result.Status);
        _addon.Verify(a => a.RightClickRetainerListing(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Execute_MultipleListings_ReturnsInReverseSlotOrder()
    {
        // Slots 0, 1, 2 — all qualifying. Should right-click 2, then 1, then 0.
        _reader.Setup(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing>
            {
                new(0, 1001, 5),
                new(1, 1001, 8),
                new(2, 1001, 3),
            });

        var order = new List<int>();
        _addon.Setup(a => a.RightClickRetainerListing(It.IsAny<int>()))
            .Callback<int>(order.Add)
            .ReturnsAsync(true);
        _addon.Setup(a => a.WaitForAddon("ContextMenu", It.IsAny<TimeSpan>(), _ct)).ReturnsAsync(true);
        _addon.Setup(a => a.ClickAddonButton("ContextMenu", 2)).ReturnsAsync(true);

        await Build(Matches(1001), Rules(1001)).ExecuteAsync(new CommandContext(), _ct);

        Assert.Equal(new[] { 2, 1, 0 }, order);
    }

    [Fact]
    public async Task Execute_NoMatchingRule_NotReturned()
    {
        // Item 1001 is listed but there is no sell rule for it
        _reader.Setup(r => r.ReadListingsAsync())
            .ReturnsAsync(new List<RetainerListing> { new(0, 1001, 10) });

        var result = await Build(Matches(1001), new List<SellRule>())
            .ExecuteAsync(new CommandContext(), _ct);

        _addon.Verify(a => a.RightClickRetainerListing(It.IsAny<int>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test MarketDumper.Tests/MarketDumper.Tests.csproj --filter "ConsolidateRetainerListingsCommandTests" -v minimal`
Expected: Build error — `ConsolidateRetainerListingsCommand` and `RightClickRetainerListing` do not exist yet

- [ ] **Step 3: Add `CommandType.ConsolidateListings`**

In `MarketDumper/Commands/CommandType.cs`, add to the enum:
```csharp
public enum CommandType
{
    SelectRetainer,
    OpenSellMenu,
    SelectItem,
    FetchMarketPrice,
    SetPrice,
    ConfirmListing,
    CloseRetainer,
    ConsolidateListings,  // ← add
}
```

- [ ] **Step 4: Add `RightClickRetainerListing` to `IAddonInteractor`**

In `MarketDumper/Services/IAddonInteractor.cs`, add:
```csharp
Task<bool> RightClickRetainerListing(int slotIndex);
```

- [ ] **Step 5: Add stub implementation in `AddonInteractor`**

In `MarketDumper/Services/AddonInteractor.cs`, add (stub returns true for now):
```csharp
public Task<bool> RightClickRetainerListing(int slotIndex)
{
    return _framework.RunOnFrameworkThread(() =>
    {
        _log.Information($"RightClickRetainerListing: slot {slotIndex}");
        try
        {
            unsafe
            {
                var addon = GetAddon("RetainerSellList");
                if (addon == null)
                {
                    _log.Error("RightClickRetainerListing: RetainerSellList not found");
                    return false;
                }
                // FireCallback value for right-clicking a listing slot.
                // Verify in-game: open RetainerSellList, check which callback index
                // opens the context menu for a slot. Common values are 0 or 1 with slotIndex.
                FireCallback(addon, true, 0, slotIndex);
                return true;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"RightClickRetainerListing error: {ex.Message}");
            return false;
        }
    });
}
```

> **In-game verification required:** Before shipping, confirm the FireCallback args that open the ContextMenu for a RetainerSellList slot. Run the plugin with logging, right-click a listing manually, and observe which callback fires. Adjust the `FireCallback` call to match.

- [ ] **Step 6: Implement `ConsolidateRetainerListingsCommand`**

Create `MarketDumper/Commands/ConsolidateRetainerListingsCommand.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Models;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class ConsolidateRetainerListingsCommand : ICommand
{
    private readonly IRetainerListingReader _reader;
    private readonly IAddonInteractor _addon;
    private readonly HashSet<uint> _playerItemIds;
    private readonly Dictionary<uint, int> _stackSizeByItemId;
    private readonly TimeSpan _timeout;

    public string Description => "Return undersized retainer listings to inventory";
    public CommandType Type => CommandType.ConsolidateListings;

    public ConsolidateRetainerListingsCommand(
        IRetainerListingReader reader,
        IAddonInteractor addon,
        List<InventoryMatch> playerMatches,
        IReadOnlyList<SellRule> rules,
        TimeSpan timeout)
    {
        _reader = reader;
        _addon = addon;
        _timeout = timeout;
        _playerItemIds = playerMatches.Select(m => m.ItemId).ToHashSet();
        _stackSizeByItemId = rules.ToDictionary(r => r.ItemId, r => r.StackSize);
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var listings = await _reader.ReadListingsAsync();

        var toReturn = listings
            .Where(l => _playerItemIds.Contains(l.ItemId)
                     && _stackSizeByItemId.TryGetValue(l.ItemId, out var ss)
                     && l.Quantity < ss)
            .OrderByDescending(l => l.SlotIndex)
            .ToList();

        foreach (var listing in toReturn)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await _addon.RightClickRetainerListing(listing.SlotIndex))
                continue;

            if (!await _addon.WaitForAddon("ContextMenu", _timeout, cancellationToken))
                continue;

            await _addon.ClickAddonButton("ContextMenu", 2);
            await Task.Delay(300, cancellationToken);
        }

        return new CommandResult(CommandStatus.Success);
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test MarketDumper.Tests/MarketDumper.Tests.csproj --filter "ConsolidateRetainerListingsCommandTests" -v minimal`
Expected: All 6 tests pass

- [ ] **Step 8: Commit**

```bash
git add MarketDumper/Models/RetainerListing.cs
git add MarketDumper/Services/IRetainerListingReader.cs
git add MarketDumper/Services/IAddonInteractor.cs
git add MarketDumper/Services/AddonInteractor.cs
git add MarketDumper/Commands/CommandType.cs
git add MarketDumper/Commands/ConsolidateRetainerListingsCommand.cs
git add MarketDumper.Tests/ConsolidateRetainerListingsCommandTests.cs
git commit -m "Add ConsolidateRetainerListingsCommand with tests"
```

---

## Task 4: Wire up `ICommandFactory` and `CommandFactory`

**Files:**
- Modify: `MarketDumper/Commands/ICommandFactory.cs`
- Modify: `MarketDumper/Commands/CommandFactory.cs`

- [ ] **Step 1: Add factory method to `ICommandFactory`**

```csharp
using System.Collections.Generic;
using MarketDumper.Models;

public interface ICommandFactory
{
    ICommand CreateSelectRetainer(int retainerIndex);
    ICommand CreateOpenSellMenu();
    ICommand CreateSelectItem(int containerIndex, int slotIndex, int stackSize);
    ICommand CreateFetchMarketPrice(uint itemId);
    ICommand CreateSetPrice();
    ICommand CreateConfirmListing();
    ICommand CreateCloseRetainer();
    ICommand CreateConsolidateListings(List<InventoryMatch> playerMatches, IReadOnlyList<SellRule> rules);  // ← add
}
```

- [ ] **Step 2: Add `IRetainerListingReader` dependency to `CommandFactory` and implement**

`CommandFactory` needs `IRetainerListingReader` injected. Add the field and constructor parameter, then add the factory method:

```csharp
// Add field:
private readonly IRetainerListingReader _retainerListingReader;

// Add to constructor signature after IMarketDataProvider:
IRetainerListingReader retainerListingReader,

// Add to constructor body:
_retainerListingReader = retainerListingReader;

// Add factory method:
public ICommand CreateConsolidateListings(List<InventoryMatch> playerMatches, IReadOnlyList<SellRule> rules) =>
    new ConsolidateRetainerListingsCommand(_retainerListingReader, _addon, playerMatches, rules, _timeout);
```

- [ ] **Step 3: Build**

Run: `dotnet build MarketDumper/MarketDumper.csproj`
Expected: Build succeeded (Plugin.cs will fail — fix in next task)

- [ ] **Step 4: Update existing `JobPlannerTests` mock setup**

`JobPlannerTests` uses `Mock<ICommandFactory>`. Add a setup for the new method so existing tests still pass:

```csharp
_factory.Setup(f => f.CreateConsolidateListings(It.IsAny<List<InventoryMatch>>(), It.IsAny<IReadOnlyList<SellRule>>()))
    .Returns(MockCommand(CommandType.ConsolidateListings, "Consolidate listings"));
```

Add this to the `JobPlannerTests` constructor alongside the existing setups.

- [ ] **Step 5: Run all tests**

Run: `dotnet test MarketDumper.Tests/MarketDumper.Tests.csproj -v minimal`
Expected: All existing tests still pass

---

## Task 5: Implement `RetainerListingReader`

**Files:**
- Create: `MarketDumper/Services/RetainerListingReader.cs`

This reads the currently-open retainer's market items from `InventoryManager` using `InventoryType.RetainerMarket`. The container is populated by the game when a retainer is engaged and `RetainerSellList` is open.

- [ ] **Step 1: Create `RetainerListingReader`**

```csharp
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
```

> **Note:** `InventoryType.RetainerMarket` must exist in the FFXIVClientStructs version bundled with Dalamud SDK 14.0.2. If the build fails, check the `InventoryType` enum for the correct retainer market constant (may be named `RetainerMarket0` or similar). The container is only populated when a retainer is engaged.

- [ ] **Step 2: Build**

Run: `dotnet build MarketDumper/MarketDumper.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add MarketDumper/Services/RetainerListingReader.cs
git add MarketDumper/Services/IRetainerListingReader.cs
git add MarketDumper/Services/IAddonInteractor.cs
git add MarketDumper/Services/AddonInteractor.cs
git add MarketDumper/Commands/ICommandFactory.cs
git add MarketDumper/Commands/CommandFactory.cs
git add MarketDumper.Tests/JobPlannerTests.cs
git commit -m "Wire up RetainerListingReader and CommandFactory"
```

---

## Task 6: Update `AutomationController` with consolidation pre-phase

**Files:**
- Modify: `MarketDumper/Automation/AutomationController.cs`

The consolidation pre-phase runs once before the existing listing loop. It:
1. Scans player inventory to find matching items
2. For each retainer: SelectRetainer → OpenSellMenu → ConsolidateListings → CloseRetainer
3. After all retainers: sorts inventory and waits

The existing listing loop is unchanged.

- [ ] **Step 1: Extract queue execution into a helper method**

In `AutomationController`, add a private helper so both the consolidation phase and the listing loop can use it:

```csharp
private async Task<CommandQueue.ExecutionResult> ExecuteQueueAsync(
    List<ICommand> commands,
    CancellationToken cancellationToken)
{
    var queue = new CommandQueue(_maxRetries);
    queue.OnProgress += (current, total, desc) =>
    {
        CurrentStep = current;
        TotalSteps = total;
        CurrentAction = desc;
        OnStateChanged?.Invoke();
    };
    queue.OnLog += msg => _chat.Print($"[MarketDumper] {msg}");

    foreach (var cmd in commands)
        queue.Enqueue(cmd);

    return await queue.ExecuteAsync(cancellationToken);
}
```

- [ ] **Step 2: Add consolidation pre-phase to `RunAsync`**

Replace the existing queue construction in `RunAsync` with calls to `ExecuteQueueAsync`, and add the consolidation phase before the listing loop. The full updated `RunAsync`:

```csharp
private async Task RunAsync(int[] freeSlotsPerRetainer, CancellationToken cancellationToken)
{
    try
    {
        // Consolidation pre-phase
        var rulesForConsolidation = _sellRuleManager.GetEnabledRules();
        var matchesForConsolidation = _inventoryScanner.FindMatchingItems(rulesForConsolidation);

        if (matchesForConsolidation.Count > 0)
        {
            var consolidationCmds = BuildConsolidationCommands(
                matchesForConsolidation, rulesForConsolidation, freeSlotsPerRetainer.Length);

            if (consolidationCmds.Count > 0)
            {
                _chat.Print("[MarketDumper] Consolidating retainer listings...");
                var consolidationResult = await ExecuteQueueAsync(consolidationCmds, cancellationToken);

                if (consolidationResult.Completed)
                {
                    await _addonInteractor.ExecuteGameCommand("/itemsort execute inventory");
                    await Task.Delay(1500, cancellationToken);
                }
            }
        }

        // Listing loop (unchanged)
        var totalListed = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rules = _sellRuleManager.GetEnabledRules();
            var matches = _inventoryScanner.FindMatchingItems(rules);

            if (matches.Count == 0)
            {
                if (totalListed == 0)
                    _chat.Print("[MarketDumper] No matching items found in inventory.");
                break;
            }

            var planner = new JobPlanner(_commandFactory);
            var commands = planner.GenerateCommands(matches, new(rules), freeSlotsPerRetainer);

            if (commands.Count == 0)
            {
                if (totalListed == 0)
                    _chat.Print("[MarketDumper] No retainer slots available.");
                break;
            }

            _chat.Print($"[MarketDumper] Starting: {commands.Count} operations queued.");

            var result = await ExecuteQueueAsync(commands, cancellationToken);
            totalListed += result.CommandsExecuted;

            if (!result.Completed)
            {
                _chat.Print($"[MarketDumper] Stopped: {result.ErrorMessage}");
                LastError = result.ErrorMessage;
                break;
            }

            if (!planner.SkippedPartials)
                break;

            _chat.Print("[MarketDumper] Sorting inventory to consolidate stacks...");
            await _addonInteractor.ExecuteGameCommand("/itemsort execute inventory");
            await Task.Delay(1500, cancellationToken);

            for (var i = 0; i < freeSlotsPerRetainer.Length; i++)
                freeSlotsPerRetainer[i] = Math.Max(0, freeSlotsPerRetainer[i] - planner.SlotsUsedPerRetainer[i]);
        }

        _chat.Print("[MarketDumper] Done!");
        LastError = null;
    }
    catch (OperationCanceledException)
    {
        _chat.Print("[MarketDumper] Stopped by user.");
    }
    catch (Exception ex)
    {
        _log.Error($"Automation error: {ex}");
        _chat.Print($"[MarketDumper] Error: {ex.Message}");
        LastError = ex.Message;
    }
    finally
    {
        _addonLifecycle.UnregisterListener(OnTalkAddon);
        _addonLifecycle.UnregisterListener(OnInputNumericAddon);
        LastFinishTime = DateTime.UtcNow;
        State = AutomationState.Idle;
        CurrentAction = string.Empty;
        OnStateChanged?.Invoke();
    }
}

private List<ICommand> BuildConsolidationCommands(
    List<InventoryMatch> matches,
    IReadOnlyList<SellRule> rules,
    int retainerCount)
{
    var commands = new List<ICommand>();
    for (var i = 0; i < retainerCount; i++)
    {
        commands.Add(_commandFactory.CreateSelectRetainer(i));
        commands.Add(_commandFactory.CreateOpenSellMenu());
        commands.Add(_commandFactory.CreateConsolidateListings(matches, rules));
        commands.Add(_commandFactory.CreateCloseRetainer());
    }
    return commands;
}
```

- [ ] **Step 3: Build**

Run: `dotnet build MarketDumper/MarketDumper.csproj`
Expected: Build succeeded (Plugin.cs may still fail — next task)

- [ ] **Step 4: Run all tests**

Run: `dotnet test MarketDumper.Tests/MarketDumper.Tests.csproj -v minimal`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add MarketDumper/Automation/AutomationController.cs
git commit -m "Add consolidation pre-phase to AutomationController"
```

---

## Task 7: Wire up `RetainerListingReader` in `Plugin.cs`

**Files:**
- Modify: `MarketDumper/Plugin.cs`

`CommandFactory` now requires `IRetainerListingReader`. Construct `RetainerListingReader` and pass it in.

- [ ] **Step 1: Locate where `CommandFactory` is constructed in `Plugin.cs`**

Find the line that creates `CommandFactory` (it will have a compile error after Task 4). Add `RetainerListingReader` construction before it and pass it as an argument.

The change follows this pattern — find the existing `CommandFactory` constructor call and add the new parameter:

```csharp
// Add before CommandFactory construction:
var retainerListingReader = new RetainerListingReader(framework, log);

// Pass to CommandFactory (add as a new argument after marketDataProvider):
var commandFactory = new CommandFactory(
    addonInteractor,
    pricingService,
    marketDataProvider,
    retainerListingReader,   // ← add
    timeout,
    interactionDelay,
    controller.SetPendingStackSize);
```

> The exact variable names in `Plugin.cs` may differ — read the file first and match the existing style.

- [ ] **Step 2: Build**

Run: `dotnet build MarketDumper/MarketDumper.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Run all tests**

Run: `dotnet test MarketDumper.Tests/MarketDumper.Tests.csproj -v minimal`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add MarketDumper/Plugin.cs
git commit -m "Wire RetainerListingReader into CommandFactory in Plugin"
```

---

## Task 8: In-game verification and FireCallback tuning

**Files:**
- Modify: `MarketDumper/Services/AddonInteractor.cs` (if FireCallback args need adjustment)

The `RightClickRetainerListing` stub uses `FireCallback(addon, true, 0, slotIndex)`. This value may need to be adjusted based on in-game observation.

- [ ] **Step 1: Load the plugin and open a retainer with market listings**

Enable verbose logging. Open the retainer, let `OpenSellMenu` run so `RetainerSellList` is visible.

- [ ] **Step 2: Trigger the consolidation manually via "Start Dumping"**

Watch the plugin logs. Confirm:
- `RetainerListingReader` finds the expected listings (correct item IDs and quantities)
- `RightClickRetainerListing` fires without error
- `ContextMenu` appears after the right-click

If `ContextMenu` does not appear, the `FireCallback` args are wrong. Use a Dalamud callback explorer (e.g., the `AddonInspector` from Dalamud's developer tools) to find the correct callback for right-clicking a `RetainerSellList` row, then update:

```csharp
// In AddonInteractor.RightClickRetainerListing, adjust as needed:
FireCallback(addon, true, <correct_value>, slotIndex);
```

- [ ] **Step 3: Confirm "Return Items to Inventory" context menu index**

With `ContextMenu` open after right-clicking a listing, confirm the option order. If "Return Items to Inventory" is not at index 2, update `ConsolidateRetainerListingsCommand`:

```csharp
// Change 2 to the correct index:
await _addon.ClickAddonButton("ContextMenu", 2);
```

- [ ] **Step 4: End-to-end smoke test**

1. Have an item (e.g., Rose Garnet) listed on a retainer at qty < 99 (e.g., 10).
2. Put more of the same item in player inventory (e.g., 28).
3. Run MarketDumper.
4. Verify the 10-qty listing is returned, inventory has 38, and one listing of 38 is created.

- [ ] **Step 5: Commit any FireCallback adjustments**

```bash
git add MarketDumper/Services/AddonInteractor.cs
git add MarketDumper/Commands/ConsolidateRetainerListingsCommand.cs
git commit -m "Tune FireCallback args for RightClickRetainerListing"
```
