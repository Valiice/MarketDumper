# MarketDumper Design Spec

## Problem

FFXIV forces players to manually click through retainer menus for every single market board listing. Selling 9,999 materials in stacks of 99 means 100+ manual listing cycles. Existing plugins still require per-listing confirmation clicks.

## Solution

MarketDumper fully automates the retainer listing loop. Users define persistent sell rules (item + stack size), configure global undercut pricing, and press one button. The plugin drives the entire flow across all retainers — selecting retainers, picking items, splitting stacks, fetching market prices, setting undercut prices, and confirming listings — until inventory is empty or retainer slots are exhausted.

## Architecture: Command Queue

The system uses a **Command Queue** pattern. A planner generates an ordered sequence of discrete commands from sell rules and inventory state. A dispatcher executes them one at a time. Each command encapsulates a single addon interaction.

```
┌─────────────┐     ┌──────────────┐     ┌──────────────────┐
│  Sell Rules  │────>│ Job Planner  │────>│  Command Queue    │
│  (persisted) │     │ (generates   │     │  (executes        │
│              │     │  commands)   │     │   sequentially)   │
└─────────────┘     └──────────────┘     └────────┬─────────┘
                                                   │
                                          ┌────────▼─────────┐
                                          │ Command Handlers  │
                                          │ (one per addon    │
                                          │  interaction)     │
                                          └────────┬─────────┘
                                                   │
                                          ┌────────▼─────────┐
                                          │ Addon Interactor  │
                                          │ (waits, clicks,   │
                                          │  reads addons)    │
                                          └──────────────────┘
```

### Flow

1. User triggers "Start Dumping"
2. `JobPlanner` reads enabled sell rules, scans inventory via `InventoryScanner`, determines available retainer slots, and generates an ordered list of commands
3. `CommandQueue` dequeues and executes commands sequentially
4. Each command uses `AddonInteractor` for game UI interaction
5. On success, next command runs. On failure, retry or abort.
6. When a retainer's slots are full, the planner inserts commands to move to the next retainer automatically

## Components

### ICommand Interface

```csharp
public interface ICommand
{
    string Description { get; }
    CommandType Type { get; }
    Task<CommandResult> ExecuteAsync();
}

public record CommandResult(
    CommandStatus Status,       // Success, Retry, Abort
    string? ErrorMessage,
    TimeSpan? RetryDelay
);

public enum CommandStatus { Success, Retry, Abort }

public enum CommandType
{
    SelectRetainer,
    OpenSellMenu,
    SelectItem,
    FetchMarketPrice,
    SetPrice,
    ConfirmListing
}
```

### Command Sequence (per item per retainer)

```
1. SelectRetainerCommand(retainerIndex)
2. OpenSellMenuCommand()
3. SelectItemCommand(inventorySlot, stackSize)
4. FetchMarketPriceCommand(itemId)
5. SetPriceCommand(calculatedPrice)
6. ConfirmListingCommand()
```

Steps 3-6 repeat for each stack until the retainer's slots are full, then step 1 fires for the next retainer.

### Queue Execution

```
while queue has commands AND not aborted:
    command = queue.Dequeue()
    for attempt in 1..maxRetries:
        result = command.ExecuteAsync()
        if Success -> next command
        if Retry   -> wait(result.RetryDelay), retry
        if Abort   -> halt, notify user
```

### Error Handling

- Addon doesn't appear within timeout -> Retry (up to 3 attempts)
- Addon has unexpected state -> Abort with descriptive error
- Retainer slots full mid-run -> skip to next retainer
- No more retainers available -> finish, report remaining inventory
- Emergency stop (hotkey/button) -> immediate abort, leave game state as-is

## Sell Rules

### SellRule Data Model

```csharp
public class SellRule
{
    uint ItemId;
    string ItemName;      // Display name cached from Lumina
    int StackSize;        // Desired stack size (e.g., 99)
    bool Enabled;         // Toggle without deleting
}
```

### SellRuleManager

- CRUD operations on sell rules
- Persisted via Dalamud's `IPluginConfiguration` (JSON serialized)
- One rule per item ID (duplicate prevention)
- Validates: stack size 1-999, item must be marketable (Lumina `Item` sheet check)

### Creating Rules

- `IContextMenu` integration adds "Add to MarketDumper" to item right-click menus
- Only shown for marketable items
- Opens a small dialog to set stack size (default: 99), saves rule

## Pricing Service

### Formula (from PennyPincher)

```csharp
public int CalculatePrice(int lowestPrice, PricingConfig config)
{
    var price = lowestPrice
        - (lowestPrice % config.Mod)
        - config.Delta;
    price -= (price % config.Multiple);
    return Math.Max(price, config.MinPrice);
}
```

With defaults (Mod=1, Delta=1, Multiple=1, MinPrice=1), this is a classic 1-gil undercut.

### Global Pricing Config

```csharp
public class PricingConfig
{
    int Delta = 1;
    int Mod = 1;
    int Multiple = 1;
    int MinPrice = 1;
    bool UndercutSelf = false;
}
```

### Filtering Logic

```
for each listing (sorted cheapest first):
    if listing is from own retainer AND !UndercutSelf -> skip
    if item being listed is HQ AND listing is NQ -> skip
    first non-skipped listing is the target to undercut
```

### HQ/NQ Handling

- Auto-detected at listing time by reading the `RetainerSell` addon's item name node for the HQ icon character (`\uE03C`)
- When listing HQ, only undercut other HQ listings
- No separate sell rules needed for HQ vs NQ

### Edge Cases

- No listings on market board -> use MinPrice as fallback
- All listings are own retainers -> keep current lowest price (don't undercut self to zero)
- Price calculation results in 0 or negative -> clamp to MinPrice

## Addon Interactor

Low-level layer for all game UI interaction. All commands use this.

### Responsibilities

- Wait for a specific addon to be ready (with configurable timeout, default ~5s)
- Read addon node values (text, numbers)
- Programmatically click addon buttons/nodes
- Detect current addon state

### Addon Interactions by Command

| Command | Addon | Action |
|---|---|---|
| SelectRetainerCommand | RetainerList | Click retainer entry by index |
| OpenSellMenuCommand | SelectString | Click "Entrust or sell items", then "Sell items on the Market Board" |
| SelectItemCommand | RetainerSellList + inventory | Select item, handle stack split quantity dialog |
| FetchMarketPriceCommand | ItemSearchResult | Trigger market data fetch, wait for OfferingsReceived |
| SetPriceCommand | RetainerSell | Write price into input field |
| ConfirmListingCommand | RetainerSell | Click confirm button |

### Stack Splitting

When an item has more units than the desired stack size, the game shows a quantity dialog. `SelectItemCommand` handles:
1. Select the item from inventory
2. If quantity dialog appears, input the desired stack size
3. Proceed to price screen

### Timing/Safety

- Every addon wait has a configurable timeout (default ~5s)
- Configurable delay between interactions (default ~200ms)
- All interactions verify the expected addon is visible before acting

## Automation Controller

Top-level orchestrator that owns the queue and coordinates the full run.

```
Start():
    1. InventoryScanner finds all items matching enabled sell rules
    2. JobPlanner generates the full command sequence across retainers
    3. CommandQueue begins execution

Stop():
    1. CommandQueue halts immediately
    2. Status updated to "Stopped by user"

Pause() / Resume():
    1. Queue pauses after current command completes
```

### Emergency Stop

- Configurable keybind via Dalamud's `IKeyState`
- Checked each frame during automation
- UI "Stop" button in status overlay
- Auto-abort on unexpected addon state or timeout exhaustion

## UI Windows

### 1. Sell Rules Window (`/marketdumper`)

- Table: Item Icon | Item Name | Stack Size | Enabled toggle | Delete button
- "Start Dumping" button (disabled if no rules enabled or no retainer bell nearby)
- Right-click context menu entries feed into this list

### 2. Config Window

- Undercut settings: Delta, Mod, Multiple, Min Price
- Don't undercut self toggle
- Automation settings: interaction delay, retry count, timeout
- Emergency stop hotkey binding

### 3. Status Overlay (during automation)

- Compact floating window:
  - Current action: "Listing Mythrite Ore x99 @ 450 gil"
  - Progress: "Retainer 2/4 - Slot 15/20"
  - Stop button

## Dalamud APIs Used

| API | Usage |
|---|---|
| IMarketBoard | OfferingsReceived event for market data |
| IAddonLifecycle | Detect addon open/close for timing |
| IContextMenu | Right-click "Add to MarketDumper" |
| IKeyState | Emergency stop hotkey |
| IDalamudPluginInterface | Config persistence, UI builder |
| ICommandManager | /marketdumper slash command |
| IChatGui | Status messages in game chat |
| IDataManager | Lumina Item sheet for validation |
| IPluginLog | Logging |
| RetainerManager (ClientStructs) | Enumerate own retainers, detect own listings |
| ItemOrderModule (ClientStructs) | Check active retainer context |
| AddonRetainerSell (ClientStructs) | Read item name/HQ status |

## Multi-Retainer Logic

- FFXIV retainers have max 20 listing slots each
- When a retainer's slots are full, `JobPlanner` inserts commands to close the current retainer and open the next
- Continues across all available retainers until inventory is empty or all retainers are full
- Final status reports: items listed, items remaining, retainers used

## Design Principles

- **SoC**: Each class has one job (pricing, inventory, addon interaction, queue execution, UI)
- **KISS**: Commands are simple — one addon interaction each. No multi-step commands.
- **DRY**: Addon interaction patterns centralized in `AddonInteractor`. Pricing logic in one place.
- **TDD**: `PricingService`, `SellRuleManager`, `JobPlanner`, and `CommandQueue` are pure logic — fully unit testable with mocks. `AddonInteractor` is behind an interface for mocking in integration tests.

## Project Structure

```
MarketDumper/
├── MarketDumper.sln
├── MarketDumper/
│   ├── MarketDumper.csproj
│   ├── MarketDumper.json
│   ├── Plugin.cs                    # Entry point, DI, lifecycle
│   ├── Configuration.cs             # IPluginConfiguration with SellRules + PricingConfig
│   ├── Commands/
│   │   ├── ICommand.cs
│   │   ├── CommandResult.cs
│   │   ├── CommandType.cs
│   │   ├── SelectRetainerCommand.cs
│   │   ├── OpenSellMenuCommand.cs
│   │   ├── SelectItemCommand.cs
│   │   ├── FetchMarketPriceCommand.cs
│   │   ├── SetPriceCommand.cs
│   │   └── ConfirmListingCommand.cs
│   ├── Automation/
│   │   ├── CommandQueue.cs
│   │   ├── AutomationController.cs
│   │   └── JobPlanner.cs
│   ├── Services/
│   │   ├── PricingService.cs
│   │   ├── InventoryScanner.cs
│   │   ├── SellRuleManager.cs
│   │   └── AddonInteractor.cs
│   ├── Models/
│   │   ├── SellRule.cs
│   │   └── PricingConfig.cs
│   └── Windows/
│       ├── SellRulesWindow.cs
│       ├── ConfigWindow.cs
│       └── StatusOverlay.cs
├── MarketDumper.Tests/
│   ├── MarketDumper.Tests.csproj
│   ├── PricingServiceTests.cs
│   ├── SellRuleManagerTests.cs
│   ├── JobPlannerTests.cs
│   └── CommandQueueTests.cs
└── docs/
    └── superpowers/
        └── specs/
            └── 2026-03-26-marketdumper-design.md
```
