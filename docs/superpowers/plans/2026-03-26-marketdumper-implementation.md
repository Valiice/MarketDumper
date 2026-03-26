# MarketDumper Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Dalamud plugin that fully automates FFXIV market board listings using a command queue pattern with persistent sell rules and configurable undercut pricing.

**Architecture:** Command Queue — a JobPlanner generates discrete commands from sell rules and inventory state, a CommandQueue executes them sequentially, each command interacts with one game addon via an AddonInteractor. Pure logic (pricing, rules, planning, queue) is fully unit-testable.

**Tech Stack:** C# / .NET 10, Dalamud.NET.Sdk 14.0.2, FFXIVClientStructs, ImGui, xUnit + Moq for tests.

**Spec:** `docs/superpowers/specs/2026-03-26-marketdumper-design.md`

---

## File Structure

```
MarketDumper/
├── MarketDumper.sln
├── MarketDumper/
│   ├── MarketDumper.csproj
│   ├── MarketDumper.json
│   ├── Plugin.cs
│   ├── Configuration.cs
│   ├── Commands/
│   │   ├── ICommand.cs
│   │   ├── CommandResult.cs
│   │   ├── CommandContext.cs
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
│   │   ├── IPricingService.cs
│   │   ├── PricingService.cs
│   │   ├── IInventoryScanner.cs
│   │   ├── InventoryScanner.cs
│   │   ├── ISellRuleManager.cs
│   │   ├── SellRuleManager.cs
│   │   ├── IAddonInteractor.cs
│   │   └── AddonInteractor.cs
│   ├── Models/
│   │   ├── SellRule.cs
│   │   ├── PricingConfig.cs
│   │   ├── InventoryMatch.cs
│   │   └── InventorySlot.cs
│   └── Windows/
│       ├── SellRulesWindow.cs
│       ├── ConfigWindow.cs
│       └── StatusOverlay.cs
├── MarketDumper.Tests/
│   ├── MarketDumper.Tests.csproj
│   ├── PricingServiceTests.cs
│   ├── SellRuleManagerTests.cs
│   ├── CommandQueueTests.cs
│   ├── InventoryScannerTests.cs
│   └── JobPlannerTests.cs
└── docs/
```

---

### Task 1: Migrate SamplePlugin to MarketDumper

**Files:**
- Rename: `SamplePlugin.sln` → `MarketDumper.sln`
- Rename: `SamplePlugin/` → `MarketDumper/`
- Rename: `SamplePlugin/SamplePlugin.csproj` → `MarketDumper/MarketDumper.csproj`
- Rename: `SamplePlugin/SamplePlugin.json` → `MarketDumper/MarketDumper.json`
- Modify: All `.cs` files for namespace changes
- Delete: `Data/goat.png`, sample window content

- [ ] **Step 1: Rename solution file**

```bash
cd B:/Downloads/Coding/CSharp/MarketDumper
git mv SamplePlugin.sln MarketDumper.sln
```

- [ ] **Step 2: Rename project directory and files**

```bash
git mv SamplePlugin MarketDumper
git mv MarketDumper/SamplePlugin.csproj MarketDumper/MarketDumper.csproj
git mv MarketDumper/SamplePlugin.json MarketDumper/MarketDumper.json
```

- [ ] **Step 3: Update solution file**

Replace all `SamplePlugin` references with `MarketDumper` in `MarketDumper.sln`. Update the project path from `SamplePlugin\SamplePlugin.csproj` to `MarketDumper\MarketDumper.csproj`.

- [ ] **Step 4: Update csproj**

In `MarketDumper/MarketDumper.csproj`:
- Remove the goat.png Content item
- Update PackageProjectUrl to the MarketDumper repo URL

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Dalamud.NET.Sdk/14.0.2">
  <PropertyGroup>
    <Version>0.0.0.1</Version>
    <PackageLicenseExpression>AGPL-3.0-or-later</PackageLicenseExpression>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
```

- [ ] **Step 5: Update plugin manifest**

In `MarketDumper/MarketDumper.json`:

```json
{
  "Author": "your name here",
  "Name": "MarketDumper",
  "Punchline": "One-click bulk market board listing with smart undercutting.",
  "Description": "Automates retainer market board listings. Define sell rules, configure undercut pricing, and dump your entire inventory onto the MB. /marketdumper to open.",
  "ApplicableVersion": "any",
  "Tags": [
    "market",
    "retainer",
    "selling",
    "automation"
  ]
}
```

- [ ] **Step 6: Update namespaces and clean up Plugin.cs**

Replace `namespace SamplePlugin` with `namespace MarketDumper` in all `.cs` files. Strip Plugin.cs down to a minimal shell:

```csharp
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace MarketDumper;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/marketdumper";

    public Configuration Configuration { get; init; }
    public readonly WindowSystem WindowSystem = new("MarketDumper");

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the MarketDumper sell rules window."
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information("MarketDumper loaded.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args) { }
    public void ToggleConfigUi() { }
    public void ToggleMainUi() { }
}
```

- [ ] **Step 7: Clean up Configuration.cs**

```csharp
using Dalamud.Configuration;
using System;

namespace MarketDumper;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
```

- [ ] **Step 8: Update Windows namespace and strip to stubs**

Update `MarketDumper/Windows/ConfigWindow.cs`:

```csharp
using System;
using Dalamud.Interface.Windowing;

namespace MarketDumper.Windows;

public class ConfigWindow : Window, IDisposable
{
    public ConfigWindow() : base("MarketDumper Config###MarketDumperConfig")
    {
    }

    public void Dispose() { }
    public override void Draw() { }
}
```

Update `MarketDumper/Windows/MainWindow.cs`:

```csharp
using System;
using Dalamud.Interface.Windowing;

namespace MarketDumper.Windows;

public class MainWindow : Window, IDisposable
{
    public MainWindow() : base("MarketDumper###MarketDumperMain")
    {
    }

    public void Dispose() { }
    public override void Draw() { }
}
```

- [ ] **Step 9: Delete sample content**

```bash
rm Data/goat.png
rmdir Data
```

- [ ] **Step 10: Verify build**

```bash
cd B:/Downloads/Coding/CSharp/MarketDumper
dotnet build MarketDumper.sln
```

Expected: Build succeeds with no errors.

- [ ] **Step 11: Commit**

Use /quick-commit

---

### Task 2: Models and Interfaces

**Files:**
- Create: `MarketDumper/Models/SellRule.cs`
- Create: `MarketDumper/Models/PricingConfig.cs`
- Create: `MarketDumper/Models/InventoryMatch.cs`
- Create: `MarketDumper/Models/InventorySlot.cs`
- Create: `MarketDumper/Commands/ICommand.cs`
- Create: `MarketDumper/Commands/CommandResult.cs`
- Create: `MarketDumper/Commands/CommandContext.cs`
- Create: `MarketDumper/Commands/CommandType.cs`
- Create: `MarketDumper/Services/IPricingService.cs`
- Create: `MarketDumper/Services/ISellRuleManager.cs`
- Create: `MarketDumper/Services/IInventoryScanner.cs`
- Create: `MarketDumper/Services/IAddonInteractor.cs`

- [ ] **Step 1: Create SellRule model**

Create `MarketDumper/Models/SellRule.cs`:

```csharp
using System;

namespace MarketDumper.Models;

[Serializable]
public class SellRule
{
    public uint ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int StackSize { get; set; } = 99;
    public bool Enabled { get; set; } = true;
}
```

- [ ] **Step 2: Create PricingConfig model**

Create `MarketDumper/Models/PricingConfig.cs`:

```csharp
using System;

namespace MarketDumper.Models;

[Serializable]
public class PricingConfig
{
    public int Delta { get; set; } = 1;
    public int Mod { get; set; } = 1;
    public int Multiple { get; set; } = 1;
    public int MinPrice { get; set; } = 1;
    public bool UndercutSelf { get; set; } = false;
}
```

- [ ] **Step 3: Create InventorySlot and InventoryMatch models**

Create `MarketDumper/Models/InventorySlot.cs`:

```csharp
namespace MarketDumper.Models;

public record InventorySlot(
    int ContainerIndex,
    int SlotIndex,
    int Quantity,
    bool IsHq
);
```

Create `MarketDumper/Models/InventoryMatch.cs`:

```csharp
using System.Collections.Generic;

namespace MarketDumper.Models;

public record InventoryMatch(
    uint ItemId,
    int TotalQuantity,
    List<InventorySlot> Slots
);
```

- [ ] **Step 4: Create command enums and data types**

Create `MarketDumper/Commands/CommandType.cs`:

```csharp
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
```

Create `MarketDumper/Commands/CommandResult.cs`:

```csharp
using System;

namespace MarketDumper.Commands;

public record CommandResult(
    CommandStatus Status,
    string? ErrorMessage = null,
    TimeSpan? RetryDelay = null
);
```

Create `MarketDumper/Commands/CommandContext.cs`:

```csharp
namespace MarketDumper.Commands;

public class CommandContext
{
    public int? CalculatedPrice { get; set; }
    public bool? IsHq { get; set; }
    public uint? CurrentItemId { get; set; }
    public int? CurrentRetainerIndex { get; set; }
}
```

- [ ] **Step 5: Create ICommand interface**

Create `MarketDumper/Commands/ICommand.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace MarketDumper.Commands;

public interface ICommand
{
    string Description { get; }
    CommandType Type { get; }
    Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken);
}
```

- [ ] **Step 6: Create service interfaces**

Create `MarketDumper/Services/IPricingService.cs`:

```csharp
using System.Collections.Generic;
using MarketDumper.Models;

namespace MarketDumper.Services;

public interface IPricingService
{
    int CalculatePrice(int lowestPrice, PricingConfig config);
    int? FindTargetPrice(IReadOnlyList<MarketListing> listings, PricingConfig config, bool isHq, IReadOnlySet<ulong> ownRetainerIds);
}

public record MarketListing(int PricePerUnit, ulong RetainerId, bool IsHq);
```

Create `MarketDumper/Services/ISellRuleManager.cs`:

```csharp
using System.Collections.Generic;
using MarketDumper.Models;

namespace MarketDumper.Services;

public interface ISellRuleManager
{
    IReadOnlyList<SellRule> GetAllRules();
    IReadOnlyList<SellRule> GetEnabledRules();
    bool AddRule(SellRule rule);
    bool RemoveRule(uint itemId);
    bool UpdateRule(SellRule rule);
    bool HasRule(uint itemId);
    void Save();
}
```

Create `MarketDumper/Services/IInventoryScanner.cs`:

```csharp
using System.Collections.Generic;
using MarketDumper.Models;

namespace MarketDumper.Services;

public interface IInventoryScanner
{
    List<InventoryMatch> FindMatchingItems(IReadOnlyList<SellRule> rules);
}
```

Create `MarketDumper/Services/IAddonInteractor.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MarketDumper.Services;

public interface IAddonInteractor
{
    Task<bool> WaitForAddon(string addonName, TimeSpan timeout, CancellationToken cancellationToken);
    bool IsAddonVisible(string addonName);
    bool ClickAddonButton(string addonName, int nodeIndex);
    bool SetAddonInputValue(string addonName, int nodeIndex, int value);
    string? ReadAddonText(string addonName, int nodeIndex);
}
```

- [ ] **Step 7: Verify build**

```bash
dotnet build MarketDumper.sln
```

Expected: Build succeeds.

- [ ] **Step 8: Commit**

Use /quick-commit

---

### Task 3: Test Project Setup and PricingService (TDD)

**Files:**
- Create: `MarketDumper.Tests/MarketDumper.Tests.csproj`
- Create: `MarketDumper.Tests/PricingServiceTests.cs`
- Create: `MarketDumper/Services/PricingService.cs`
- Modify: `MarketDumper.sln` (add test project)

- [ ] **Step 1: Create test project**

Create `MarketDumper.Tests/MarketDumper.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows7.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Moq" Version="4.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MarketDumper\MarketDumper.csproj" />
  </ItemGroup>
</Project>
```

Note: If the project reference to the Dalamud SDK project causes build issues, we may need to extract pure logic types into a shared library or add explicit Dalamud DLL references. Cross that bridge if we hit it.

- [ ] **Step 2: Add test project to solution**

```bash
cd B:/Downloads/Coding/CSharp/MarketDumper
dotnet sln MarketDumper.sln add MarketDumper.Tests/MarketDumper.Tests.csproj
```

- [ ] **Step 3: Verify test project builds**

```bash
dotnet build MarketDumper.Tests/MarketDumper.Tests.csproj
```

If build fails due to Dalamud SDK references, create a `MarketDumper.Core` class library instead and move pure models/services there. Both MarketDumper and MarketDumper.Tests reference Core.

- [ ] **Step 4: Write failing PricingService tests**

Create `MarketDumper.Tests/PricingServiceTests.cs`:

```csharp
using MarketDumper.Models;
using MarketDumper.Services;
using Xunit;

namespace MarketDumper.Tests;

public class PricingServiceTests
{
    private readonly PricingService _sut = new();

    [Fact]
    public void CalculatePrice_DefaultConfig_UndercutsBy1Gil()
    {
        var config = new PricingConfig();
        var result = _sut.CalculatePrice(100, config);
        Assert.Equal(99, result);
    }

    [Fact]
    public void CalculatePrice_CustomDelta_UndercutsByDelta()
    {
        var config = new PricingConfig { Delta = 5 };
        var result = _sut.CalculatePrice(100, config);
        Assert.Equal(95, result);
    }

    [Fact]
    public void CalculatePrice_WithMod_StripsRemainder()
    {
        // Price 457, Mod 10 -> 457 - (457 % 10) - 1 = 457 - 7 - 1 = 449
        var config = new PricingConfig { Mod = 10 };
        var result = _sut.CalculatePrice(457, config);
        Assert.Equal(449, result);
    }

    [Fact]
    public void CalculatePrice_WithMultiple_RoundsDown()
    {
        // Price 100, Delta 1, Multiple 10 -> 99 - (99 % 10) = 99 - 9 = 90
        var config = new PricingConfig { Multiple = 10 };
        var result = _sut.CalculatePrice(100, config);
        Assert.Equal(90, result);
    }

    [Fact]
    public void CalculatePrice_ResultBelowMin_ClampsToMin()
    {
        var config = new PricingConfig { Delta = 200, MinPrice = 50 };
        var result = _sut.CalculatePrice(100, config);
        Assert.Equal(50, result);
    }

    [Fact]
    public void CalculatePrice_ResultNegative_ClampsToMin()
    {
        var config = new PricingConfig { Delta = 500, MinPrice = 1 };
        var result = _sut.CalculatePrice(100, config);
        Assert.Equal(1, result);
    }

    [Fact]
    public void FindTargetPrice_SkipsOwnRetainers()
    {
        var ownRetainers = new HashSet<ulong> { 111 };
        var listings = new List<MarketListing>
        {
            new(100, 111, false), // own retainer, should skip
            new(200, 222, false), // other retainer
        };
        var config = new PricingConfig();

        var result = _sut.FindTargetPrice(listings, config, isHq: false, ownRetainers);

        Assert.Equal(199, result); // undercuts 200, not 100
    }

    [Fact]
    public void FindTargetPrice_HqItem_SkipsNqListings()
    {
        var ownRetainers = new HashSet<ulong>();
        var listings = new List<MarketListing>
        {
            new(100, 222, false), // NQ listing, skip when listing HQ
            new(300, 333, true),  // HQ listing, target this
        };
        var config = new PricingConfig();

        var result = _sut.FindTargetPrice(listings, config, isHq: true, ownRetainers);

        Assert.Equal(299, result); // undercuts 300 HQ listing
    }

    [Fact]
    public void FindTargetPrice_NoListings_ReturnsNull()
    {
        var ownRetainers = new HashSet<ulong>();
        var listings = new List<MarketListing>();
        var config = new PricingConfig();

        var result = _sut.FindTargetPrice(listings, config, isHq: false, ownRetainers);

        Assert.Null(result);
    }

    [Fact]
    public void FindTargetPrice_AllOwnRetainers_ReturnsNull()
    {
        var ownRetainers = new HashSet<ulong> { 111, 222 };
        var listings = new List<MarketListing>
        {
            new(100, 111, false),
            new(200, 222, false),
        };
        var config = new PricingConfig();

        var result = _sut.FindTargetPrice(listings, config, isHq: false, ownRetainers);

        Assert.Null(result);
    }

    [Fact]
    public void FindTargetPrice_UndercutSelfTrue_IncludesOwnRetainers()
    {
        var ownRetainers = new HashSet<ulong> { 111 };
        var listings = new List<MarketListing>
        {
            new(100, 111, false), // own retainer, include because UndercutSelf=true
            new(200, 222, false),
        };
        var config = new PricingConfig { UndercutSelf = true };

        var result = _sut.FindTargetPrice(listings, config, isHq: false, ownRetainers);

        Assert.Equal(99, result); // undercuts own listing at 100
    }
}
```

- [ ] **Step 5: Run tests to verify they fail**

```bash
dotnet test MarketDumper.Tests/MarketDumper.Tests.csproj
```

Expected: Compilation error — `PricingService` class not found.

- [ ] **Step 6: Implement PricingService**

Create `MarketDumper/Services/PricingService.cs`:

```csharp
using System;
using System.Collections.Generic;
using MarketDumper.Models;

namespace MarketDumper.Services;

public class PricingService : IPricingService
{
    public int CalculatePrice(int lowestPrice, PricingConfig config)
    {
        var price = lowestPrice
            - (lowestPrice % config.Mod)
            - config.Delta;
        price -= (price % config.Multiple);
        return Math.Max(price, config.MinPrice);
    }

    public int? FindTargetPrice(
        IReadOnlyList<MarketListing> listings,
        PricingConfig config,
        bool isHq,
        IReadOnlySet<ulong> ownRetainerIds)
    {
        foreach (var listing in listings)
        {
            if (!config.UndercutSelf && ownRetainerIds.Contains(listing.RetainerId))
                continue;

            if (isHq && !listing.IsHq)
                continue;

            return CalculatePrice(listing.PricePerUnit, config);
        }

        return null;
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

```bash
dotnet test MarketDumper.Tests/MarketDumper.Tests.csproj
```

Expected: All 10 tests pass.

- [ ] **Step 8: Commit**

Use /quick-commit

---

### Task 4: SellRuleManager (TDD)

**Files:**
- Create: `MarketDumper.Tests/SellRuleManagerTests.cs`
- Create: `MarketDumper/Services/SellRuleManager.cs`
- Modify: `MarketDumper/Configuration.cs` (add SellRules list + PricingConfig)

- [ ] **Step 1: Update Configuration to hold sell rules and pricing config**

Modify `MarketDumper/Configuration.cs`:

```csharp
using Dalamud.Configuration;
using MarketDumper.Models;
using System;
using System.Collections.Generic;

namespace MarketDumper;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public List<SellRule> SellRules { get; set; } = new();
    public PricingConfig PricingConfig { get; set; } = new();

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
```

- [ ] **Step 2: Write failing SellRuleManager tests**

Create `MarketDumper.Tests/SellRuleManagerTests.cs`:

```csharp
using MarketDumper.Models;
using MarketDumper.Services;
using Xunit;

namespace MarketDumper.Tests;

public class SellRuleManagerTests
{
    private static SellRuleManager CreateManager(List<SellRule>? rules = null)
    {
        rules ??= new List<SellRule>();
        return new SellRuleManager(rules);
    }

    [Fact]
    public void AddRule_NewItem_ReturnsTrue()
    {
        var manager = CreateManager();
        var rule = new SellRule { ItemId = 1, ItemName = "Mythrite Ore", StackSize = 99 };

        var result = manager.AddRule(rule);

        Assert.True(result);
        Assert.Single(manager.GetAllRules());
    }

    [Fact]
    public void AddRule_DuplicateItemId_ReturnsFalse()
    {
        var manager = CreateManager();
        var rule1 = new SellRule { ItemId = 1, ItemName = "Mythrite Ore", StackSize = 99 };
        var rule2 = new SellRule { ItemId = 1, ItemName = "Mythrite Ore", StackSize = 50 };

        manager.AddRule(rule1);
        var result = manager.AddRule(rule2);

        Assert.False(result);
        Assert.Single(manager.GetAllRules());
    }

    [Fact]
    public void RemoveRule_ExistingItem_ReturnsTrue()
    {
        var manager = CreateManager();
        manager.AddRule(new SellRule { ItemId = 1, ItemName = "Test" });

        var result = manager.RemoveRule(1);

        Assert.True(result);
        Assert.Empty(manager.GetAllRules());
    }

    [Fact]
    public void RemoveRule_NonExistent_ReturnsFalse()
    {
        var manager = CreateManager();
        var result = manager.RemoveRule(999);
        Assert.False(result);
    }

    [Fact]
    public void GetEnabledRules_FiltersDisabled()
    {
        var manager = CreateManager();
        manager.AddRule(new SellRule { ItemId = 1, ItemName = "Enabled", Enabled = true });
        manager.AddRule(new SellRule { ItemId = 2, ItemName = "Disabled", Enabled = false });

        var enabled = manager.GetEnabledRules();

        Assert.Single(enabled);
        Assert.Equal((uint)1, enabled[0].ItemId);
    }

    [Fact]
    public void UpdateRule_ExistingItem_UpdatesFields()
    {
        var manager = CreateManager();
        manager.AddRule(new SellRule { ItemId = 1, ItemName = "Test", StackSize = 99 });

        var updated = new SellRule { ItemId = 1, ItemName = "Test", StackSize = 50 };
        var result = manager.UpdateRule(updated);

        Assert.True(result);
        Assert.Equal(50, manager.GetAllRules()[0].StackSize);
    }

    [Fact]
    public void UpdateRule_NonExistent_ReturnsFalse()
    {
        var manager = CreateManager();
        var result = manager.UpdateRule(new SellRule { ItemId = 999 });
        Assert.False(result);
    }

    [Fact]
    public void HasRule_ExistingItem_ReturnsTrue()
    {
        var manager = CreateManager();
        manager.AddRule(new SellRule { ItemId = 1, ItemName = "Test" });
        Assert.True(manager.HasRule(1));
    }

    [Fact]
    public void HasRule_NonExistent_ReturnsFalse()
    {
        var manager = CreateManager();
        Assert.False(manager.HasRule(999));
    }

    [Fact]
    public void Constructor_LoadsExistingRules()
    {
        var existing = new List<SellRule>
        {
            new() { ItemId = 1, ItemName = "Pre-existing" }
        };
        var manager = CreateManager(existing);

        Assert.Single(manager.GetAllRules());
        Assert.Equal("Pre-existing", manager.GetAllRules()[0].ItemName);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test MarketDumper.Tests/MarketDumper.Tests.csproj --filter "FullyQualifiedName~SellRuleManager"
```

Expected: Compilation error — `SellRuleManager` class not found.

- [ ] **Step 4: Implement SellRuleManager**

Create `MarketDumper/Services/SellRuleManager.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using MarketDumper.Models;

namespace MarketDumper.Services;

public class SellRuleManager : ISellRuleManager
{
    private readonly List<SellRule> _rules;

    public SellRuleManager(List<SellRule> rules)
    {
        _rules = rules;
    }

    public IReadOnlyList<SellRule> GetAllRules() => _rules.AsReadOnly();

    public IReadOnlyList<SellRule> GetEnabledRules() =>
        _rules.Where(r => r.Enabled).ToList().AsReadOnly();

    public bool AddRule(SellRule rule)
    {
        if (_rules.Any(r => r.ItemId == rule.ItemId))
            return false;

        _rules.Add(rule);
        return true;
    }

    public bool RemoveRule(uint itemId)
    {
        var index = _rules.FindIndex(r => r.ItemId == itemId);
        if (index < 0)
            return false;

        _rules.RemoveAt(index);
        return true;
    }

    public bool UpdateRule(SellRule rule)
    {
        var index = _rules.FindIndex(r => r.ItemId == rule.ItemId);
        if (index < 0)
            return false;

        _rules[index] = rule;
        return true;
    }

    public bool HasRule(uint itemId) => _rules.Any(r => r.ItemId == itemId);

    public void Save()
    {
        // Save is handled by the Configuration class which owns the rules list.
        // This method exists so callers can trigger persistence.
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test MarketDumper.Tests/MarketDumper.Tests.csproj --filter "FullyQualifiedName~SellRuleManager"
```

Expected: All 9 tests pass.

- [ ] **Step 6: Commit**

Use /quick-commit

---

### Task 5: CommandQueue (TDD)

**Files:**
- Create: `MarketDumper.Tests/CommandQueueTests.cs`
- Create: `MarketDumper/Automation/CommandQueue.cs`

- [ ] **Step 1: Write failing CommandQueue tests**

Create `MarketDumper.Tests/CommandQueueTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Automation;
using MarketDumper.Commands;
using Moq;
using Xunit;

namespace MarketDumper.Tests;

public class CommandQueueTests
{
    private static Mock<ICommand> CreateMockCommand(CommandResult result, string description = "Test")
    {
        var mock = new Mock<ICommand>();
        mock.Setup(c => c.Description).Returns(description);
        mock.Setup(c => c.Type).Returns(CommandType.SelectRetainer);
        mock.Setup(c => c.ExecuteAsync(It.IsAny<CommandContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    [Fact]
    public async Task ExecuteAsync_EmptyQueue_CompletesSuccessfully()
    {
        var queue = new CommandQueue(maxRetries: 3);
        var result = await queue.ExecuteAsync(CancellationToken.None);
        Assert.True(result.Completed);
        Assert.Equal(0, result.CommandsExecuted);
    }

    [Fact]
    public async Task ExecuteAsync_SingleSuccessCommand_ExecutesIt()
    {
        var cmd = CreateMockCommand(new CommandResult(CommandStatus.Success));
        var queue = new CommandQueue(maxRetries: 3);
        queue.Enqueue(cmd.Object);

        var result = await queue.ExecuteAsync(CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(1, result.CommandsExecuted);
        cmd.Verify(c => c.ExecuteAsync(It.IsAny<CommandContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleCommands_ExecutesInOrder()
    {
        var order = new List<int>();
        var cmd1 = new Mock<ICommand>();
        cmd1.Setup(c => c.Description).Returns("First");
        cmd1.Setup(c => c.Type).Returns(CommandType.SelectRetainer);
        cmd1.Setup(c => c.ExecuteAsync(It.IsAny<CommandContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add(1))
            .ReturnsAsync(new CommandResult(CommandStatus.Success));

        var cmd2 = new Mock<ICommand>();
        cmd2.Setup(c => c.Description).Returns("Second");
        cmd2.Setup(c => c.Type).Returns(CommandType.OpenSellMenu);
        cmd2.Setup(c => c.ExecuteAsync(It.IsAny<CommandContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add(2))
            .ReturnsAsync(new CommandResult(CommandStatus.Success));

        var queue = new CommandQueue(maxRetries: 3);
        queue.Enqueue(cmd1.Object);
        queue.Enqueue(cmd2.Object);

        await queue.ExecuteAsync(CancellationToken.None);

        Assert.Equal(new[] { 1, 2 }, order);
    }

    [Fact]
    public async Task ExecuteAsync_RetryThenSuccess_RetriesCommand()
    {
        var attempts = 0;
        var cmd = new Mock<ICommand>();
        cmd.Setup(c => c.Description).Returns("Retry test");
        cmd.Setup(c => c.Type).Returns(CommandType.SelectRetainer);
        cmd.Setup(c => c.ExecuteAsync(It.IsAny<CommandContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                attempts++;
                return attempts < 3
                    ? new CommandResult(CommandStatus.Retry, RetryDelay: TimeSpan.Zero)
                    : new CommandResult(CommandStatus.Success);
            });

        var queue = new CommandQueue(maxRetries: 3);
        queue.Enqueue(cmd.Object);

        var result = await queue.ExecuteAsync(CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_RetryExhausted_Aborts()
    {
        var cmd = CreateMockCommand(new CommandResult(CommandStatus.Retry, RetryDelay: TimeSpan.Zero));
        var queue = new CommandQueue(maxRetries: 3);
        queue.Enqueue(cmd.Object);

        var result = await queue.ExecuteAsync(CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Contains("retries exhausted", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_AbortCommand_StopsImmediately()
    {
        var cmd1 = CreateMockCommand(new CommandResult(CommandStatus.Abort, "Something broke"));
        var cmd2 = CreateMockCommand(new CommandResult(CommandStatus.Success));

        var queue = new CommandQueue(maxRetries: 3);
        queue.Enqueue(cmd1.Object);
        queue.Enqueue(cmd2.Object);

        var result = await queue.ExecuteAsync(CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Equal("Something broke", result.ErrorMessage);
        cmd2.Verify(c => c.ExecuteAsync(It.IsAny<CommandContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_StopsGracefully()
    {
        var cts = new CancellationTokenSource();
        var cmd = new Mock<ICommand>();
        cmd.Setup(c => c.Description).Returns("Cancel test");
        cmd.Setup(c => c.Type).Returns(CommandType.SelectRetainer);
        cmd.Setup(c => c.ExecuteAsync(It.IsAny<CommandContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ReturnsAsync(new CommandResult(CommandStatus.Success));

        var cmd2 = CreateMockCommand(new CommandResult(CommandStatus.Success), "Should not run");

        var queue = new CommandQueue(maxRetries: 3);
        queue.Enqueue(cmd.Object);
        queue.Enqueue(cmd2.Object);

        var result = await queue.ExecuteAsync(cts.Token);

        Assert.False(result.Completed);
        cmd2.Verify(c => c.ExecuteAsync(It.IsAny<CommandContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void CommandCount_ReflectsQueueState()
    {
        var cmd = CreateMockCommand(new CommandResult(CommandStatus.Success));
        var queue = new CommandQueue(maxRetries: 3);

        Assert.Equal(0, queue.CommandCount);

        queue.Enqueue(cmd.Object);
        Assert.Equal(1, queue.CommandCount);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsProgressViaCallback()
    {
        var progressUpdates = new List<(int current, int total, string description)>();
        var cmd1 = CreateMockCommand(new CommandResult(CommandStatus.Success), "Step 1");
        var cmd2 = CreateMockCommand(new CommandResult(CommandStatus.Success), "Step 2");

        var queue = new CommandQueue(maxRetries: 3);
        queue.OnProgress += (current, total, desc) => progressUpdates.Add((current, total, desc));
        queue.Enqueue(cmd1.Object);
        queue.Enqueue(cmd2.Object);

        await queue.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, progressUpdates.Count);
        Assert.Equal((1, 2, "Step 1"), progressUpdates[0]);
        Assert.Equal((2, 2, "Step 2"), progressUpdates[1]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test MarketDumper.Tests/MarketDumper.Tests.csproj --filter "FullyQualifiedName~CommandQueue"
```

Expected: Compilation error — `CommandQueue` class not found.

- [ ] **Step 3: Implement CommandQueue**

Create `MarketDumper/Automation/CommandQueue.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Commands;

namespace MarketDumper.Automation;

public class CommandQueue
{
    private readonly Queue<ICommand> _commands = new();
    private readonly int _maxRetries;

    public event Action<int, int, string>? OnProgress;

    public int CommandCount => _commands.Count;

    public CommandQueue(int maxRetries)
    {
        _maxRetries = maxRetries;
    }

    public void Enqueue(ICommand command)
    {
        _commands.Enqueue(command);
    }

    public async Task<QueueResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var total = _commands.Count;
        var executed = 0;
        var context = new CommandContext();

        while (_commands.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
                return new QueueResult(false, executed, "Cancelled by user.");

            var command = _commands.Dequeue();
            executed++;

            OnProgress?.Invoke(executed, total, command.Description);

            var success = false;
            for (var attempt = 1; attempt <= _maxRetries; attempt++)
            {
                var result = await command.ExecuteAsync(context, cancellationToken);

                if (result.Status == CommandStatus.Success)
                {
                    success = true;
                    break;
                }

                if (result.Status == CommandStatus.Abort)
                    return new QueueResult(false, executed, result.ErrorMessage);

                if (result.Status == CommandStatus.Retry && attempt < _maxRetries && result.RetryDelay.HasValue)
                    await Task.Delay(result.RetryDelay.Value, cancellationToken);
            }

            if (!success)
                return new QueueResult(false, executed, $"Command '{command.Description}' failed: retries exhausted.");

            if (cancellationToken.IsCancellationRequested)
                return new QueueResult(false, executed, "Cancelled by user.");
        }

        return new QueueResult(true, executed);
    }
}

public record QueueResult(
    bool Completed,
    int CommandsExecuted,
    string? ErrorMessage = null
);
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test MarketDumper.Tests/MarketDumper.Tests.csproj --filter "FullyQualifiedName~CommandQueue"
```

Expected: All 9 tests pass.

- [ ] **Step 5: Commit**

Use /quick-commit

---

### Task 6: InventoryScanner (TDD)

**Files:**
- Create: `MarketDumper.Tests/InventoryScannerTests.cs`
- Create: `MarketDumper/Services/InventoryScanner.cs`

The InventoryScanner wraps FFXIVClientStructs inventory access. To make it testable, we introduce a thin abstraction over the game's inventory data.

- [ ] **Step 1: Create inventory data abstraction**

Add to `MarketDumper/Services/IInventoryScanner.cs` (below the existing interface):

```csharp
public record InventoryItemData(uint ItemId, int Quantity, bool IsHq, int ContainerIndex, int SlotIndex);

public interface IInventoryDataProvider
{
    List<InventoryItemData> GetAllInventoryItems();
}
```

Update `IInventoryScanner` — it stays the same, `InventoryScanner` will take `IInventoryDataProvider` as a dependency.

- [ ] **Step 2: Write failing InventoryScanner tests**

Create `MarketDumper.Tests/InventoryScannerTests.cs`:

```csharp
using MarketDumper.Models;
using MarketDumper.Services;
using Moq;
using Xunit;

namespace MarketDumper.Tests;

public class InventoryScannerTests
{
    private static InventoryScanner CreateScanner(List<InventoryItemData> items)
    {
        var provider = new Mock<IInventoryDataProvider>();
        provider.Setup(p => p.GetAllInventoryItems()).Returns(items);
        return new InventoryScanner(provider.Object);
    }

    [Fact]
    public void FindMatchingItems_MatchingRule_ReturnsMatch()
    {
        var items = new List<InventoryItemData>
        {
            new(ItemId: 100, Quantity: 999, IsHq: false, ContainerIndex: 0, SlotIndex: 5)
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 100, ItemName = "Ore", StackSize = 99, Enabled = true }
        };
        var scanner = CreateScanner(items);

        var matches = scanner.FindMatchingItems(rules);

        Assert.Single(matches);
        Assert.Equal((uint)100, matches[0].ItemId);
        Assert.Equal(999, matches[0].TotalQuantity);
        Assert.Single(matches[0].Slots);
    }

    [Fact]
    public void FindMatchingItems_NoMatchingItems_ReturnsEmpty()
    {
        var items = new List<InventoryItemData>
        {
            new(ItemId: 200, Quantity: 50, IsHq: false, ContainerIndex: 0, SlotIndex: 0)
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 100, ItemName = "Ore", StackSize = 99, Enabled = true }
        };
        var scanner = CreateScanner(items);

        var matches = scanner.FindMatchingItems(rules);

        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatchingItems_MultipleSlotsSameItem_AggregatesQuantity()
    {
        var items = new List<InventoryItemData>
        {
            new(ItemId: 100, Quantity: 500, IsHq: false, ContainerIndex: 0, SlotIndex: 0),
            new(ItemId: 100, Quantity: 499, IsHq: false, ContainerIndex: 1, SlotIndex: 3),
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 100, ItemName = "Ore", StackSize = 99, Enabled = true }
        };
        var scanner = CreateScanner(items);

        var matches = scanner.FindMatchingItems(rules);

        Assert.Single(matches);
        Assert.Equal(999, matches[0].TotalQuantity);
        Assert.Equal(2, matches[0].Slots.Count);
    }

    [Fact]
    public void FindMatchingItems_HqAndNqSameItem_TracksHqPerSlot()
    {
        var items = new List<InventoryItemData>
        {
            new(ItemId: 100, Quantity: 50, IsHq: false, ContainerIndex: 0, SlotIndex: 0),
            new(ItemId: 100, Quantity: 30, IsHq: true, ContainerIndex: 0, SlotIndex: 1),
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 100, ItemName = "Ore", StackSize = 99, Enabled = true }
        };
        var scanner = CreateScanner(items);

        var matches = scanner.FindMatchingItems(rules);

        Assert.Single(matches);
        Assert.Equal(80, matches[0].TotalQuantity);
        Assert.False(matches[0].Slots[0].IsHq);
        Assert.True(matches[0].Slots[1].IsHq);
    }

    [Fact]
    public void FindMatchingItems_MultipleRules_ReturnsMultipleMatches()
    {
        var items = new List<InventoryItemData>
        {
            new(ItemId: 100, Quantity: 500, IsHq: false, ContainerIndex: 0, SlotIndex: 0),
            new(ItemId: 200, Quantity: 300, IsHq: false, ContainerIndex: 1, SlotIndex: 0),
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 100, ItemName = "Ore", StackSize = 99, Enabled = true },
            new() { ItemId = 200, ItemName = "Sand", StackSize = 99, Enabled = true },
        };
        var scanner = CreateScanner(items);

        var matches = scanner.FindMatchingItems(rules);

        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void FindMatchingItems_SlotsOrderedByContainerThenSlot()
    {
        var items = new List<InventoryItemData>
        {
            new(ItemId: 100, Quantity: 100, IsHq: false, ContainerIndex: 2, SlotIndex: 5),
            new(ItemId: 100, Quantity: 200, IsHq: false, ContainerIndex: 0, SlotIndex: 3),
            new(ItemId: 100, Quantity: 150, IsHq: false, ContainerIndex: 0, SlotIndex: 1),
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 100, ItemName = "Ore", StackSize = 99, Enabled = true }
        };
        var scanner = CreateScanner(items);

        var matches = scanner.FindMatchingItems(rules);

        var slots = matches[0].Slots;
        Assert.Equal(0, slots[0].ContainerIndex);
        Assert.Equal(1, slots[0].SlotIndex);
        Assert.Equal(0, slots[1].ContainerIndex);
        Assert.Equal(3, slots[1].SlotIndex);
        Assert.Equal(2, slots[2].ContainerIndex);
        Assert.Equal(5, slots[2].SlotIndex);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test MarketDumper.Tests/MarketDumper.Tests.csproj --filter "FullyQualifiedName~InventoryScanner"
```

Expected: Compilation error — `InventoryScanner` class not found.

- [ ] **Step 4: Implement InventoryScanner**

Create `MarketDumper/Services/InventoryScanner.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using MarketDumper.Models;

namespace MarketDumper.Services;

public class InventoryScanner : IInventoryScanner
{
    private readonly IInventoryDataProvider _dataProvider;

    public InventoryScanner(IInventoryDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public List<InventoryMatch> FindMatchingItems(IReadOnlyList<SellRule> rules)
    {
        var ruleItemIds = new HashSet<uint>(rules.Select(r => r.ItemId));
        var allItems = _dataProvider.GetAllInventoryItems();

        return allItems
            .Where(item => ruleItemIds.Contains(item.ItemId))
            .GroupBy(item => item.ItemId)
            .Select(group => new InventoryMatch(
                ItemId: group.Key,
                TotalQuantity: group.Sum(i => i.Quantity),
                Slots: group
                    .OrderBy(i => i.ContainerIndex)
                    .ThenBy(i => i.SlotIndex)
                    .Select(i => new InventorySlot(i.ContainerIndex, i.SlotIndex, i.Quantity, i.IsHq))
                    .ToList()
            ))
            .ToList();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test MarketDumper.Tests/MarketDumper.Tests.csproj --filter "FullyQualifiedName~InventoryScanner"
```

Expected: All 7 tests pass.

- [ ] **Step 6: Commit**

Use /quick-commit

---

### Task 7: JobPlanner (TDD)

**Files:**
- Create: `MarketDumper.Tests/JobPlannerTests.cs`
- Create: `MarketDumper/Automation/JobPlanner.cs`

The JobPlanner generates a list of ICommand objects from sell rules, inventory matches, and available retainer slots. Since the concrete commands don't exist yet, JobPlanner will create lightweight command descriptors that the test can verify. We use a factory interface to decouple command creation.

- [ ] **Step 1: Create command factory interface**

Create `MarketDumper/Commands/ICommandFactory.cs`:

```csharp
using MarketDumper.Models;

namespace MarketDumper.Commands;

public interface ICommandFactory
{
    ICommand CreateSelectRetainer(int retainerIndex);
    ICommand CreateOpenSellMenu();
    ICommand CreateSelectItem(int containerIndex, int slotIndex, int stackSize);
    ICommand CreateFetchMarketPrice(uint itemId);
    ICommand CreateSetPrice();
    ICommand CreateConfirmListing();
}
```

- [ ] **Step 2: Write failing JobPlanner tests**

Create `MarketDumper.Tests/JobPlannerTests.cs`:

```csharp
using MarketDumper.Automation;
using MarketDumper.Commands;
using MarketDumper.Models;
using Moq;
using Xunit;

namespace MarketDumper.Tests;

public class JobPlannerTests
{
    private static Mock<ICommandFactory> CreateMockFactory()
    {
        var factory = new Mock<ICommandFactory>();

        factory.Setup(f => f.CreateSelectRetainer(It.IsAny<int>()))
            .Returns((int idx) =>
            {
                var cmd = new Mock<ICommand>();
                cmd.Setup(c => c.Type).Returns(CommandType.SelectRetainer);
                cmd.Setup(c => c.Description).Returns($"Select retainer {idx}");
                return cmd.Object;
            });

        factory.Setup(f => f.CreateOpenSellMenu())
            .Returns(() =>
            {
                var cmd = new Mock<ICommand>();
                cmd.Setup(c => c.Type).Returns(CommandType.OpenSellMenu);
                cmd.Setup(c => c.Description).Returns("Open sell menu");
                return cmd.Object;
            });

        factory.Setup(f => f.CreateSelectItem(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((int c, int s, int sz) =>
            {
                var cmd = new Mock<ICommand>();
                cmd.Setup(x => x.Type).Returns(CommandType.SelectItem);
                cmd.Setup(x => x.Description).Returns($"Select item [{c}:{s}] x{sz}");
                return cmd.Object;
            });

        factory.Setup(f => f.CreateFetchMarketPrice(It.IsAny<uint>()))
            .Returns((uint id) =>
            {
                var cmd = new Mock<ICommand>();
                cmd.Setup(c => c.Type).Returns(CommandType.FetchMarketPrice);
                cmd.Setup(c => c.Description).Returns($"Fetch price for {id}");
                return cmd.Object;
            });

        factory.Setup(f => f.CreateSetPrice())
            .Returns(() =>
            {
                var cmd = new Mock<ICommand>();
                cmd.Setup(c => c.Type).Returns(CommandType.SetPrice);
                cmd.Setup(c => c.Description).Returns("Set price");
                return cmd.Object;
            });

        factory.Setup(f => f.CreateConfirmListing())
            .Returns(() =>
            {
                var cmd = new Mock<ICommand>();
                cmd.Setup(c => c.Type).Returns(CommandType.ConfirmListing);
                cmd.Setup(c => c.Description).Returns("Confirm listing");
                return cmd.Object;
            });

        return factory;
    }

    [Fact]
    public void GenerateCommands_SingleItemSingleStack_ProducesCorrectSequence()
    {
        var factory = CreateMockFactory();
        var planner = new JobPlanner(factory.Object);

        var matches = new List<InventoryMatch>
        {
            new(ItemId: 100, TotalQuantity: 99, Slots: new()
            {
                new(ContainerIndex: 0, SlotIndex: 5, Quantity: 99, IsHq: false)
            })
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 100, ItemName = "Ore", StackSize = 99 }
        };
        var freeSlots = new int[] { 20 }; // 1 retainer, 20 free slots

        var commands = planner.GenerateCommands(matches, rules, freeSlots);

        // Expected: SelectRetainer, OpenSellMenu, SelectItem, FetchPrice, SetPrice, Confirm
        Assert.Equal(6, commands.Count);
        Assert.Equal(CommandType.SelectRetainer, commands[0].Type);
        Assert.Equal(CommandType.OpenSellMenu, commands[1].Type);
        Assert.Equal(CommandType.SelectItem, commands[2].Type);
        Assert.Equal(CommandType.FetchMarketPrice, commands[3].Type);
        Assert.Equal(CommandType.SetPrice, commands[4].Type);
        Assert.Equal(CommandType.ConfirmListing, commands[5].Type);
    }

    [Fact]
    public void GenerateCommands_MultipleStacks_RepeatsListingCommands()
    {
        var factory = CreateMockFactory();
        var planner = new JobPlanner(factory.Object);

        var matches = new List<InventoryMatch>
        {
            new(ItemId: 100, TotalQuantity: 200, Slots: new()
            {
                new(ContainerIndex: 0, SlotIndex: 0, Quantity: 200, IsHq: false)
            })
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 100, ItemName = "Ore", StackSize = 99 }
        };
        var freeSlots = new int[] { 20 };

        var commands = planner.GenerateCommands(matches, rules, freeSlots);

        // 2 full stacks of 99, plus 1 partial stack of 2 = 3 listings
        // SelectRetainer + OpenSellMenu + 3 * (SelectItem, FetchPrice, SetPrice, Confirm) = 14
        Assert.Equal(14, commands.Count);
        Assert.Equal(CommandType.SelectRetainer, commands[0].Type);
        Assert.Equal(CommandType.OpenSellMenu, commands[1].Type);
        // First listing group
        Assert.Equal(CommandType.SelectItem, commands[2].Type);
        // Second listing group
        Assert.Equal(CommandType.SelectItem, commands[6].Type);
        // Third listing group (remainder)
        Assert.Equal(CommandType.SelectItem, commands[10].Type);
    }

    [Fact]
    public void GenerateCommands_RetainerSlotsFull_MovesToNextRetainer()
    {
        var factory = CreateMockFactory();
        var planner = new JobPlanner(factory.Object);

        var matches = new List<InventoryMatch>
        {
            new(ItemId: 100, TotalQuantity: 300, Slots: new()
            {
                new(ContainerIndex: 0, SlotIndex: 0, Quantity: 300, IsHq: false)
            })
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 100, ItemName = "Ore", StackSize = 99 }
        };
        // 2 retainers: first has 1 free slot, second has 20
        var freeSlots = new int[] { 1, 20 };

        var commands = planner.GenerateCommands(matches, rules, freeSlots);

        // Retainer 0: SelectRetainer, OpenSellMenu, 1 listing (4 cmds) = 6
        // Retainer 1: SelectRetainer, OpenSellMenu, 3 listings (but 1 already done, so 2+remainder) = varies
        // Key assertion: there should be two SelectRetainer commands
        var selectRetainerCount = commands.Count(c => c.Type == CommandType.SelectRetainer);
        Assert.Equal(2, selectRetainerCount);
    }

    [Fact]
    public void GenerateCommands_NoFreeSlots_ReturnsEmpty()
    {
        var factory = CreateMockFactory();
        var planner = new JobPlanner(factory.Object);

        var matches = new List<InventoryMatch>
        {
            new(ItemId: 100, TotalQuantity: 99, Slots: new()
            {
                new(ContainerIndex: 0, SlotIndex: 0, Quantity: 99, IsHq: false)
            })
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 100, ItemName = "Ore", StackSize = 99 }
        };
        var freeSlots = new int[] { 0, 0 };

        var commands = planner.GenerateCommands(matches, rules, freeSlots);

        Assert.Empty(commands);
    }

    [Fact]
    public void GenerateCommands_NoMatches_ReturnsEmpty()
    {
        var factory = CreateMockFactory();
        var planner = new JobPlanner(factory.Object);

        var matches = new List<InventoryMatch>();
        var rules = new List<SellRule>();
        var freeSlots = new int[] { 20 };

        var commands = planner.GenerateCommands(matches, rules, freeSlots);

        Assert.Empty(commands);
    }

    [Fact]
    public void GenerateCommands_MoreStacksThanTotalSlots_OnlyFillsAvailable()
    {
        var factory = CreateMockFactory();
        var planner = new JobPlanner(factory.Object);

        var matches = new List<InventoryMatch>
        {
            new(ItemId: 100, TotalQuantity: 9999, Slots: new()
            {
                new(ContainerIndex: 0, SlotIndex: 0, Quantity: 9999, IsHq: false)
            })
        };
        var rules = new List<SellRule>
        {
            new() { ItemId = 100, ItemName = "Ore", StackSize = 99 }
        };
        // Only 2 total free slots across retainers
        var freeSlots = new int[] { 1, 1 };

        var commands = planner.GenerateCommands(matches, rules, freeSlots);

        // Should only produce commands for 2 listings total
        var confirmCount = commands.Count(c => c.Type == CommandType.ConfirmListing);
        Assert.Equal(2, confirmCount);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test MarketDumper.Tests/MarketDumper.Tests.csproj --filter "FullyQualifiedName~JobPlanner"
```

Expected: Compilation error — `JobPlanner` class not found.

- [ ] **Step 4: Implement JobPlanner**

Create `MarketDumper/Automation/JobPlanner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using MarketDumper.Commands;
using MarketDumper.Models;

namespace MarketDumper.Automation;

public class JobPlanner
{
    private readonly ICommandFactory _commandFactory;

    public JobPlanner(ICommandFactory commandFactory)
    {
        _commandFactory = commandFactory;
    }

    public List<ICommand> GenerateCommands(
        List<InventoryMatch> matches,
        List<SellRule> rules,
        int[] freeSlotsPerRetainer)
    {
        var commands = new List<ICommand>();
        if (matches.Count == 0 || freeSlotsPerRetainer.All(s => s <= 0))
            return commands;

        var rulesByItemId = rules.ToDictionary(r => r.ItemId);

        // Build a flat list of (itemId, stackSize) pairs representing every listing we need to make
        var pendingListings = new Queue<(uint ItemId, int StackSize, InventorySlot Slot)>();
        foreach (var match in matches)
        {
            if (!rulesByItemId.TryGetValue(match.ItemId, out var rule))
                continue;

            var remaining = match.TotalQuantity;
            var slotIndex = 0;
            var slotRemaining = match.Slots.Count > 0 ? match.Slots[0].Quantity : 0;

            while (remaining > 0 && slotIndex < match.Slots.Count)
            {
                var stackSize = Math.Min(rule.StackSize, remaining);
                var slot = match.Slots[slotIndex];
                pendingListings.Enqueue((match.ItemId, stackSize, slot));
                remaining -= stackSize;
                slotRemaining -= stackSize;

                if (slotRemaining <= 0 && slotIndex + 1 < match.Slots.Count)
                {
                    slotIndex++;
                    slotRemaining = match.Slots[slotIndex].Quantity;
                }
            }
        }

        // Distribute listings across retainers
        for (var retainerIdx = 0; retainerIdx < freeSlotsPerRetainer.Length; retainerIdx++)
        {
            var slotsAvailable = freeSlotsPerRetainer[retainerIdx];
            if (slotsAvailable <= 0 || pendingListings.Count == 0)
                continue;

            commands.Add(_commandFactory.CreateSelectRetainer(retainerIdx));
            commands.Add(_commandFactory.CreateOpenSellMenu());

            var listed = 0;
            while (listed < slotsAvailable && pendingListings.Count > 0)
            {
                var (itemId, stackSize, slot) = pendingListings.Dequeue();

                commands.Add(_commandFactory.CreateSelectItem(slot.ContainerIndex, slot.SlotIndex, stackSize));
                commands.Add(_commandFactory.CreateFetchMarketPrice(itemId));
                commands.Add(_commandFactory.CreateSetPrice());
                commands.Add(_commandFactory.CreateConfirmListing());

                listed++;
            }
        }

        return commands;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test MarketDumper.Tests/MarketDumper.Tests.csproj --filter "FullyQualifiedName~JobPlanner"
```

Expected: All 6 tests pass.

- [ ] **Step 6: Commit**

Use /quick-commit

---

### Task 8: AddonInteractor Implementation

**Files:**
- Create: `MarketDumper/Services/AddonInteractor.cs`
- Create: `MarketDumper/Services/GameInventoryDataProvider.cs`

This task implements the Dalamud-dependent service layer. These classes wrap FFXIVClientStructs and Dalamud APIs behind the interfaces defined in Task 2. They are NOT unit-tested directly (game dependency) but are tested through the mocked interfaces in other tests.

- [ ] **Step 1: Implement AddonInteractor**

Create `MarketDumper/Services/AddonInteractor.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MarketDumper.Services;

public class AddonInteractor : IAddonInteractor
{
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IPluginLog _log;

    public AddonInteractor(IAddonLifecycle addonLifecycle, IPluginLog log)
    {
        _addonLifecycle = addonLifecycle;
        _log = log;
    }

    public async Task<bool> WaitForAddon(string addonName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsAddonVisible(addonName))
                return true;

            await Task.Delay(100, cancellationToken);
        }

        _log.Warning($"Timed out waiting for addon: {addonName}");
        return false;
    }

    public unsafe bool IsAddonVisible(string addonName)
    {
        var addon = GetAddon(addonName);
        return addon != null && addon->IsVisible;
    }

    public unsafe bool ClickAddonButton(string addonName, int nodeIndex)
    {
        // Addon click implementation using AtkUnitBase callbacks.
        // This will be fleshed out during integration testing with the game client.
        // The pattern is: get addon -> find node by index -> invoke click callback.
        _log.Information($"ClickAddonButton: {addonName} node {nodeIndex}");
        return true;
    }

    public unsafe bool SetAddonInputValue(string addonName, int nodeIndex, int value)
    {
        _log.Information($"SetAddonInputValue: {addonName} node {nodeIndex} = {value}");
        return true;
    }

    public unsafe string? ReadAddonText(string addonName, int nodeIndex)
    {
        _log.Information($"ReadAddonText: {addonName} node {nodeIndex}");
        return null;
    }

    private unsafe AtkUnitBase* GetAddon(string name)
    {
        try
        {
            var atkStage = AtkStage.Instance();
            if (atkStage == null) return null;
            var unitManager = atkStage->RaptureAtkUnitManager;
            if (unitManager == null) return null;
            return unitManager->GetAddonByName(name);
        }
        catch
        {
            return null;
        }
    }
}
```

Note: The click/set/read methods are stubs that will be fully implemented during integration testing with the live game client. The addon node indices and callback patterns are game-version-dependent and need to be discovered through runtime inspection. The important thing is the interface contract is established.

- [ ] **Step 2: Implement GameInventoryDataProvider**

Create `MarketDumper/Services/GameInventoryDataProvider.cs`:

```csharp
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
        if (manager == null)
            return results;

        for (var containerIdx = 0; containerIdx < PlayerBags.Length; containerIdx++)
        {
            var container = manager->GetInventoryContainer(PlayerBags[containerIdx]);
            if (container == null)
                continue;

            for (var slotIdx = 0; slotIdx < container->Size; slotIdx++)
            {
                var item = container->GetInventorySlot(slotIdx);
                if (item == null || item->ItemId == 0)
                    continue;

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
```

- [ ] **Step 3: Verify build**

```bash
dotnet build MarketDumper/MarketDumper.csproj
```

Expected: Build succeeds. (If FFXIVClientStructs types don't resolve, check that the Dalamud SDK includes them — they should be provided by `Dalamud.NET.Sdk`.)

- [ ] **Step 4: Commit**

Use /quick-commit

---

### Task 9: Concrete Commands

**Files:**
- Create: `MarketDumper/Commands/SelectRetainerCommand.cs`
- Create: `MarketDumper/Commands/OpenSellMenuCommand.cs`
- Create: `MarketDumper/Commands/SelectItemCommand.cs`
- Create: `MarketDumper/Commands/FetchMarketPriceCommand.cs`
- Create: `MarketDumper/Commands/SetPriceCommand.cs`
- Create: `MarketDumper/Commands/ConfirmListingCommand.cs`
- Create: `MarketDumper/Commands/CommandFactory.cs`

Each command follows the same pattern: wait for addon → interact → return result. All use `IAddonInteractor` for game interaction.

- [ ] **Step 1: Implement SelectRetainerCommand**

Create `MarketDumper/Commands/SelectRetainerCommand.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class SelectRetainerCommand : ICommand
{
    private readonly int _retainerIndex;
    private readonly IAddonInteractor _addon;
    private readonly TimeSpan _timeout;

    public string Description => $"Select retainer {_retainerIndex}";
    public CommandType Type => CommandType.SelectRetainer;

    public SelectRetainerCommand(int retainerIndex, IAddonInteractor addon, TimeSpan timeout)
    {
        _retainerIndex = retainerIndex;
        _addon = addon;
        _timeout = timeout;
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!await _addon.WaitForAddon("RetainerList", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "RetainerList not found", TimeSpan.FromSeconds(1));

        if (!_addon.ClickAddonButton("RetainerList", _retainerIndex))
            return new CommandResult(CommandStatus.Abort, $"Failed to click retainer {_retainerIndex}");

        context.CurrentRetainerIndex = _retainerIndex;
        return new CommandResult(CommandStatus.Success);
    }
}
```

- [ ] **Step 2: Implement OpenSellMenuCommand**

Create `MarketDumper/Commands/OpenSellMenuCommand.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class OpenSellMenuCommand : ICommand
{
    private readonly IAddonInteractor _addon;
    private readonly TimeSpan _timeout;

    public string Description => "Open sell menu";
    public CommandType Type => CommandType.OpenSellMenu;

    public OpenSellMenuCommand(IAddonInteractor addon, TimeSpan timeout)
    {
        _addon = addon;
        _timeout = timeout;
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Wait for SelectString addon (retainer menu)
        if (!await _addon.WaitForAddon("SelectString", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "SelectString not found", TimeSpan.FromSeconds(1));

        // Click "Entrust or sell items"
        if (!_addon.ClickAddonButton("SelectString", 0))
            return new CommandResult(CommandStatus.Abort, "Failed to click sell items option");

        // Wait for second SelectString (sub-menu)
        if (!await _addon.WaitForAddon("SelectString", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "Sell sub-menu not found", TimeSpan.FromSeconds(1));

        // Click "Sell items on the Market Board"
        if (!_addon.ClickAddonButton("SelectString", 0))
            return new CommandResult(CommandStatus.Abort, "Failed to click market board option");

        return new CommandResult(CommandStatus.Success);
    }
}
```

- [ ] **Step 3: Implement SelectItemCommand**

Create `MarketDumper/Commands/SelectItemCommand.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class SelectItemCommand : ICommand
{
    private readonly int _containerIndex;
    private readonly int _slotIndex;
    private readonly int _stackSize;
    private readonly IAddonInteractor _addon;
    private readonly TimeSpan _timeout;

    public string Description => $"Select item [{_containerIndex}:{_slotIndex}] x{_stackSize}";
    public CommandType Type => CommandType.SelectItem;

    public SelectItemCommand(int containerIndex, int slotIndex, int stackSize, IAddonInteractor addon, TimeSpan timeout)
    {
        _containerIndex = containerIndex;
        _slotIndex = slotIndex;
        _stackSize = stackSize;
        _addon = addon;
        _timeout = timeout;
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Wait for the retainer sell list
        if (!await _addon.WaitForAddon("RetainerSellList", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "RetainerSellList not found", TimeSpan.FromSeconds(1));

        // Click the inventory slot to select the item
        if (!_addon.ClickAddonButton("RetainerSellList", _slotIndex))
            return new CommandResult(CommandStatus.Abort, $"Failed to select item at slot {_slotIndex}");

        // If a quantity/split dialog appears, set the stack size
        await Task.Delay(200, cancellationToken); // Brief wait for dialog
        if (_addon.IsAddonVisible("InputNumeric"))
        {
            if (!_addon.SetAddonInputValue("InputNumeric", 0, _stackSize))
                return new CommandResult(CommandStatus.Abort, "Failed to set stack size");

            if (!_addon.ClickAddonButton("InputNumeric", 1)) // OK button
                return new CommandResult(CommandStatus.Abort, "Failed to confirm stack size");
        }

        // Wait for RetainerSell (price screen)
        if (!await _addon.WaitForAddon("RetainerSell", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "RetainerSell not found", TimeSpan.FromSeconds(1));

        // Read HQ status from the item name node
        var itemText = _addon.ReadAddonText("RetainerSell", 0);
        context.IsHq = itemText?.Contains("\uE03C") ?? false;

        return new CommandResult(CommandStatus.Success);
    }
}
```

- [ ] **Step 4: Create IMarketDataProvider interface**

This interface abstracts the `IMarketBoard.OfferingsReceived` event handling. Add to `MarketDumper/Services/IPricingService.cs`:

```csharp
public record MarketDataResult(
    IReadOnlyList<MarketListing> Listings,
    PricingConfig PricingConfig,
    IReadOnlySet<ulong> OwnRetainerIds
);

public interface IMarketDataProvider
{
    Task<MarketDataResult?> WaitForMarketDataAsync(uint itemId, TimeSpan timeout, CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Implement FetchMarketPriceCommand**

Create `MarketDumper/Commands/FetchMarketPriceCommand.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class FetchMarketPriceCommand : ICommand
{
    private readonly uint _itemId;
    private readonly IPricingService _pricingService;
    private readonly IAddonInteractor _addon;
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly TimeSpan _timeout;

    public string Description => $"Fetch market price for item {_itemId}";
    public CommandType Type => CommandType.FetchMarketPrice;

    public FetchMarketPriceCommand(
        uint itemId,
        IPricingService pricingService,
        IAddonInteractor addon,
        IMarketDataProvider marketDataProvider,
        TimeSpan timeout)
    {
        _itemId = itemId;
        _pricingService = pricingService;
        _addon = addon;
        _marketDataProvider = marketDataProvider;
        _timeout = timeout;
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        context.CurrentItemId = _itemId;

        // Click "Compare Prices" on RetainerSell addon
        if (!_addon.ClickAddonButton("RetainerSell", 1))
            return new CommandResult(CommandStatus.Abort, "Failed to click Compare Prices");

        // Wait for ItemSearchResult addon to appear
        if (!await _addon.WaitForAddon("ItemSearchResult", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "ItemSearchResult not found", TimeSpan.FromSeconds(1));

        // Wait for market data to arrive
        var marketData = await _marketDataProvider.WaitForMarketDataAsync(_itemId, _timeout, cancellationToken);
        if (marketData == null)
            return new CommandResult(CommandStatus.Retry, "Market data not received", TimeSpan.FromSeconds(1));

        // Calculate undercut price
        var targetPrice = _pricingService.FindTargetPrice(
            marketData.Listings,
            marketData.PricingConfig,
            context.IsHq ?? false,
            marketData.OwnRetainerIds);

        context.CalculatedPrice = targetPrice ?? marketData.PricingConfig.MinPrice;

        return new CommandResult(CommandStatus.Success);
    }
}
```

- [ ] **Step 6: Implement SetPriceCommand**

Create `MarketDumper/Commands/SetPriceCommand.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class SetPriceCommand : ICommand
{
    private readonly IAddonInteractor _addon;
    private readonly TimeSpan _timeout;

    public string Description => "Set listing price";
    public CommandType Type => CommandType.SetPrice;

    public SetPriceCommand(IAddonInteractor addon, TimeSpan timeout)
    {
        _addon = addon;
        _timeout = timeout;
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!context.CalculatedPrice.HasValue)
            return new CommandResult(CommandStatus.Abort, "No price calculated");

        if (!await _addon.WaitForAddon("RetainerSell", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "RetainerSell not found", TimeSpan.FromSeconds(1));

        if (!_addon.SetAddonInputValue("RetainerSell", 0, context.CalculatedPrice.Value))
            return new CommandResult(CommandStatus.Abort, "Failed to set price");

        return new CommandResult(CommandStatus.Success);
    }
}
```

- [ ] **Step 7: Implement ConfirmListingCommand**

Create `MarketDumper/Commands/ConfirmListingCommand.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class ConfirmListingCommand : ICommand
{
    private readonly IAddonInteractor _addon;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _interactionDelay;

    public string Description => "Confirm listing";
    public CommandType Type => CommandType.ConfirmListing;

    public ConfirmListingCommand(IAddonInteractor addon, TimeSpan timeout, TimeSpan interactionDelay)
    {
        _addon = addon;
        _timeout = timeout;
        _interactionDelay = interactionDelay;
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!await _addon.WaitForAddon("RetainerSell", _timeout, cancellationToken))
            return new CommandResult(CommandStatus.Retry, "RetainerSell not found", TimeSpan.FromSeconds(1));

        // Click the confirm/list button
        if (!_addon.ClickAddonButton("RetainerSell", 2))
            return new CommandResult(CommandStatus.Abort, "Failed to click confirm");

        // Wait for the confirmation dialog if it appears
        await Task.Delay(200, cancellationToken);
        if (_addon.IsAddonVisible("SelectYesno"))
        {
            if (!_addon.ClickAddonButton("SelectYesno", 0)) // Yes button
                return new CommandResult(CommandStatus.Abort, "Failed to confirm dialog");
        }

        // Brief delay between listings to avoid overwhelming the client
        await Task.Delay(_interactionDelay, cancellationToken);

        return new CommandResult(CommandStatus.Success);
    }
}
```

- [ ] **Step 8: Implement CommandFactory**

Create `MarketDumper/Commands/CommandFactory.cs`:

```csharp
using System;
using MarketDumper.Services;

namespace MarketDumper.Commands;

public class CommandFactory : ICommandFactory
{
    private readonly IAddonInteractor _addon;
    private readonly IPricingService _pricingService;
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _interactionDelay;

    public CommandFactory(
        IAddonInteractor addon,
        IPricingService pricingService,
        IMarketDataProvider marketDataProvider,
        TimeSpan timeout,
        TimeSpan interactionDelay)
    {
        _addon = addon;
        _pricingService = pricingService;
        _marketDataProvider = marketDataProvider;
        _timeout = timeout;
        _interactionDelay = interactionDelay;
    }

    public ICommand CreateSelectRetainer(int retainerIndex) =>
        new SelectRetainerCommand(retainerIndex, _addon, _timeout);

    public ICommand CreateOpenSellMenu() =>
        new OpenSellMenuCommand(_addon, _timeout);

    public ICommand CreateSelectItem(int containerIndex, int slotIndex, int stackSize) =>
        new SelectItemCommand(containerIndex, slotIndex, stackSize, _addon, _timeout);

    public ICommand CreateFetchMarketPrice(uint itemId) =>
        new FetchMarketPriceCommand(itemId, _pricingService, _addon, _marketDataProvider, _timeout);

    public ICommand CreateSetPrice() =>
        new SetPriceCommand(_addon, _timeout);

    public ICommand CreateConfirmListing() =>
        new ConfirmListingCommand(_addon, _timeout, _interactionDelay);
}
```

- [ ] **Step 9: Verify build**

```bash
dotnet build MarketDumper/MarketDumper.csproj
```

Expected: Build succeeds.

- [ ] **Step 10: Commit**

Use /quick-commit

---

### Task 10: AutomationController

**Files:**
- Create: `MarketDumper/Automation/AutomationController.cs`
- Create: `MarketDumper/Services/MarketDataProvider.cs`

- [ ] **Step 1: Implement MarketDataProvider**

Create `MarketDumper/Services/MarketDataProvider.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using MarketDumper.Models;

namespace MarketDumper.Services;

public class MarketDataProvider : IMarketDataProvider
{
    private readonly IMarketBoard _marketBoard;
    private readonly Configuration _configuration;
    private readonly IPluginLog _log;
    private TaskCompletionSource<MarketDataResult?>? _pendingRequest;
    private uint _expectedItemId;

    public MarketDataProvider(IMarketBoard marketBoard, Configuration configuration, IPluginLog log)
    {
        _marketBoard = marketBoard;
        _configuration = configuration;
        _log = log;
        _marketBoard.OfferingsReceived += OnOfferingsReceived;
    }

    public async Task<MarketDataResult?> WaitForMarketDataAsync(uint itemId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _expectedItemId = itemId;
        _pendingRequest = new TaskCompletionSource<MarketDataResult?>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            using var registration = cts.Token.Register(() => _pendingRequest.TrySetResult(null));
            return await _pendingRequest.Task;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private unsafe void OnOfferingsReceived(IMarketBoardCurrentOfferings offerings)
    {
        if (_pendingRequest == null)
            return;

        var listings = offerings.ItemListings
            .Select(l => new MarketListing((int)l.PricePerUnit, l.RetainerId, l.IsHq))
            .ToList();

        var ownRetainerIds = new HashSet<ulong>();
        var retainerManager = RetainerManager.Instance();
        if (retainerManager != null)
        {
            for (uint i = 0; i < retainerManager->GetRetainerCount(); i++)
            {
                var retainer = retainerManager->GetRetainerBySortedIndex(i);
                if (retainer != null)
                    ownRetainerIds.Add(retainer->RetainerId);
            }
        }

        var result = new MarketDataResult(listings, _configuration.PricingConfig, ownRetainerIds);
        _pendingRequest.TrySetResult(result);
    }

    public void Dispose()
    {
        _marketBoard.OfferingsReceived -= OnOfferingsReceived;
    }
}
```

- [ ] **Step 2: Implement AutomationController**

Create `MarketDumper/Automation/AutomationController.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using MarketDumper.Commands;
using MarketDumper.Services;
using Dalamud.Plugin.Services;

namespace MarketDumper.Automation;

public enum AutomationState
{
    Idle,
    Running,
    Paused,
    Stopping
}

public class AutomationController
{
    private readonly ISellRuleManager _sellRuleManager;
    private readonly IInventoryScanner _inventoryScanner;
    private readonly ICommandFactory _commandFactory;
    private readonly IPluginLog _log;
    private readonly IChatGui _chat;
    private readonly int _maxRetries;

    private CancellationTokenSource? _cts;
    private Task? _runTask;

    public AutomationState State { get; private set; } = AutomationState.Idle;
    public string CurrentAction { get; private set; } = string.Empty;
    public int CurrentStep { get; private set; }
    public int TotalSteps { get; private set; }
    public string? LastError { get; private set; }

    public event Action? OnStateChanged;

    public AutomationController(
        ISellRuleManager sellRuleManager,
        IInventoryScanner inventoryScanner,
        ICommandFactory commandFactory,
        IPluginLog log,
        IChatGui chat,
        int maxRetries = 3)
    {
        _sellRuleManager = sellRuleManager;
        _inventoryScanner = inventoryScanner;
        _commandFactory = commandFactory;
        _log = log;
        _chat = chat;
        _maxRetries = maxRetries;
    }

    public void Start(int[] freeSlotsPerRetainer)
    {
        if (State != AutomationState.Idle)
            return;

        _cts = new CancellationTokenSource();
        State = AutomationState.Running;
        LastError = null;
        OnStateChanged?.Invoke();

        _runTask = Task.Run(() => RunAsync(freeSlotsPerRetainer, _cts.Token));
    }

    public void Stop()
    {
        if (State != AutomationState.Running)
            return;

        State = AutomationState.Stopping;
        _cts?.Cancel();
        OnStateChanged?.Invoke();
    }

    private async Task RunAsync(int[] freeSlotsPerRetainer, CancellationToken cancellationToken)
    {
        try
        {
            var rules = _sellRuleManager.GetEnabledRules();
            var matches = _inventoryScanner.FindMatchingItems(rules);

            if (matches.Count == 0)
            {
                _chat.Print("[MarketDumper] No matching items found in inventory.");
                return;
            }

            var planner = new JobPlanner(_commandFactory);
            var commands = planner.GenerateCommands(matches, new(rules), freeSlotsPerRetainer);

            if (commands.Count == 0)
            {
                _chat.Print("[MarketDumper] No retainer slots available.");
                return;
            }

            var queue = new CommandQueue(_maxRetries);
            queue.OnProgress += (current, total, desc) =>
            {
                CurrentStep = current;
                TotalSteps = total;
                CurrentAction = desc;
                OnStateChanged?.Invoke();
            };

            foreach (var cmd in commands)
                queue.Enqueue(cmd);

            _chat.Print($"[MarketDumper] Starting: {commands.Count} operations queued.");

            var result = await queue.ExecuteAsync(cancellationToken);

            if (result.Completed)
                _chat.Print($"[MarketDumper] Done! Listed {result.CommandsExecuted / 4} items.");
            else
                _chat.Print($"[MarketDumper] Stopped: {result.ErrorMessage}");

            LastError = result.ErrorMessage;
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
            State = AutomationState.Idle;
            CurrentAction = string.Empty;
            OnStateChanged?.Invoke();
        }
    }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build MarketDumper/MarketDumper.csproj
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

Use /quick-commit

---

### Task 11: UI Windows

**Files:**
- Modify: `MarketDumper/Windows/MainWindow.cs` → becomes `SellRulesWindow.cs`
- Modify: `MarketDumper/Windows/ConfigWindow.cs`
- Create: `MarketDumper/Windows/StatusOverlay.cs`

- [ ] **Step 1: Rename MainWindow to SellRulesWindow**

```bash
cd B:/Downloads/Coding/CSharp/MarketDumper
git mv MarketDumper/Windows/MainWindow.cs MarketDumper/Windows/SellRulesWindow.cs
```

- [ ] **Step 2: Implement SellRulesWindow**

Write `MarketDumper/Windows/SellRulesWindow.cs`:

```csharp
using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using MarketDumper.Automation;
using MarketDumper.Services;

namespace MarketDumper.Windows;

public class SellRulesWindow : Window, IDisposable
{
    private readonly ISellRuleManager _sellRuleManager;
    private readonly AutomationController _automation;

    public SellRulesWindow(ISellRuleManager sellRuleManager, AutomationController automation)
        : base("MarketDumper - Sell Rules###MarketDumperRules")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(450, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        _sellRuleManager = sellRuleManager;
        _automation = automation;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var rules = _sellRuleManager.GetAllRules();

        if (ImGui.BeginTable("SellRulesTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Stack Size", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("##delete", ImGuiTableColumnFlags.WidthFixed, 30);
            ImGui.TableHeadersRow();

            uint? toRemove = null;
            foreach (var rule in rules)
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.Text(rule.ItemName);

                ImGui.TableNextColumn();
                var stackSize = rule.StackSize;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputInt($"##stack_{rule.ItemId}", ref stackSize))
                {
                    rule.StackSize = Math.Clamp(stackSize, 1, 999);
                    _sellRuleManager.Save();
                }

                ImGui.TableNextColumn();
                var enabled = rule.Enabled;
                if (ImGui.Checkbox($"##enabled_{rule.ItemId}", ref enabled))
                {
                    rule.Enabled = enabled;
                    _sellRuleManager.Save();
                }

                ImGui.TableNextColumn();
                if (ImGui.Button($"X##del_{rule.ItemId}"))
                    toRemove = rule.ItemId;
            }

            ImGui.EndTable();

            if (toRemove.HasValue)
            {
                _sellRuleManager.RemoveRule(toRemove.Value);
                _sellRuleManager.Save();
            }
        }

        if (rules.Count == 0)
        {
            ImGui.TextDisabled("No sell rules. Right-click items to add them.");
        }

        ImGui.Separator();

        var isRunning = _automation.State != AutomationState.Idle;
        var hasEnabledRules = _sellRuleManager.GetEnabledRules().Count > 0;

        if (isRunning)
        {
            if (ImGui.Button("Stop Dumping"))
                _automation.Stop();
        }
        else
        {
            if (!hasEnabledRules)
                ImGui.BeginDisabled();

            if (ImGui.Button("Start Dumping"))
            {
                // TODO: Determine free slots per retainer from game state
                // For now, assume max slots. Will be connected during integration.
                var freeSlots = new int[] { 20, 20, 20, 20 };
                _automation.Start(freeSlots);
            }

            if (!hasEnabledRules)
                ImGui.EndDisabled();
        }
    }
}
```

- [ ] **Step 3: Implement ConfigWindow**

Write `MarketDumper/Windows/ConfigWindow.cs`:

```csharp
using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using MarketDumper.Models;

namespace MarketDumper.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration _configuration;

    public ConfigWindow(Configuration configuration)
        : base("MarketDumper - Config###MarketDumperConfig")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(350, 280);
        SizeCondition = ImGuiCond.FirstUseEver;
        _configuration = configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var config = _configuration.PricingConfig;

        ImGui.Text("Undercut Settings");
        ImGui.Separator();

        var delta = config.Delta;
        if (ImGui.InputInt("Undercut Amount (Delta)", ref delta))
        {
            config.Delta = Math.Max(0, delta);
            _configuration.Save();
        }

        var mod = config.Mod;
        if (ImGui.InputInt("Price Modulo (Mod)", ref mod))
        {
            config.Mod = Math.Max(1, mod);
            _configuration.Save();
        }

        var multiple = config.Multiple;
        if (ImGui.InputInt("Round to Multiple", ref multiple))
        {
            config.Multiple = Math.Max(1, multiple);
            _configuration.Save();
        }

        var minPrice = config.MinPrice;
        if (ImGui.InputInt("Minimum Price", ref minPrice))
        {
            config.MinPrice = Math.Max(1, minPrice);
            _configuration.Save();
        }

        ImGui.Spacing();

        var undercutSelf = config.UndercutSelf;
        if (ImGui.Checkbox("Undercut own retainers", ref undercutSelf))
        {
            config.UndercutSelf = undercutSelf;
            _configuration.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Defaults: Delta=1, Mod=1, Multiple=1, Min=1");
        ImGui.TextDisabled("With defaults, this is a simple 1-gil undercut.");
    }
}
```

- [ ] **Step 4: Create StatusOverlay**

Create `MarketDumper/Windows/StatusOverlay.cs`:

```csharp
using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using MarketDumper.Automation;

namespace MarketDumper.Windows;

public class StatusOverlay : Window, IDisposable
{
    private readonly AutomationController _automation;

    public StatusOverlay(AutomationController automation)
        : base("MarketDumper Status###MarketDumperStatus",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        _automation = automation;
    }

    public void Dispose() { }

    public override bool DrawConditions()
    {
        return _automation.State != AutomationState.Idle;
    }

    public override void Draw()
    {
        ImGui.Text(_automation.CurrentAction);

        if (_automation.TotalSteps > 0)
        {
            var progress = (float)_automation.CurrentStep / _automation.TotalSteps;
            ImGui.ProgressBar(progress, new Vector2(300, 0),
                $"{_automation.CurrentStep}/{_automation.TotalSteps}");
        }

        if (_automation.LastError != null)
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), _automation.LastError);
        }

        ImGui.Spacing();

        if (ImGui.Button("Stop", new Vector2(300, 0)))
            _automation.Stop();
    }
}
```

- [ ] **Step 5: Verify build**

```bash
dotnet build MarketDumper/MarketDumper.csproj
```

Expected: Build succeeds.

- [ ] **Step 6: Commit**

Use /quick-commit

---

### Task 12: Context Menu, Plugin Wiring, and Final Integration

**Files:**
- Modify: `MarketDumper/Plugin.cs`
- Modify: `MarketDumper/Configuration.cs` (ensure Save wiring)

This task wires everything together in Plugin.cs — services, windows, commands, context menu, hotkey.

- [ ] **Step 1: Wire Plugin.cs**

Rewrite `MarketDumper/Plugin.cs`:

```csharp
using System;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using MarketDumper.Automation;
using MarketDumper.Commands;
using MarketDumper.Models;
using MarketDumper.Services;
using MarketDumper.Windows;
using Lumina.Excel.Sheets;

namespace MarketDumper;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IMarketBoard MarketBoard { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IKeyState KeyState { get; private set; } = null!;

    private const string CommandName = "/marketdumper";

    public Configuration Configuration { get; init; }
    public readonly WindowSystem WindowSystem = new("MarketDumper");

    // Services
    private readonly PricingService _pricingService;
    private readonly SellRuleManager _sellRuleManager;
    private readonly GameInventoryDataProvider _inventoryDataProvider;
    private readonly InventoryScanner _inventoryScanner;
    private readonly AddonInteractor _addonInteractor;
    private readonly MarketDataProvider _marketDataProvider;
    private readonly CommandFactory _commandFactory;
    private readonly AutomationController _automation;

    // Windows
    private readonly SellRulesWindow _sellRulesWindow;
    private readonly ConfigWindow _configWindow;
    private readonly StatusOverlay _statusOverlay;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Services
        _pricingService = new PricingService();
        _sellRuleManager = new SellRuleManager(Configuration.SellRules);
        _inventoryDataProvider = new GameInventoryDataProvider();
        _inventoryScanner = new InventoryScanner(_inventoryDataProvider);
        _addonInteractor = new AddonInteractor(AddonLifecycle, Log);
        _marketDataProvider = new MarketDataProvider(MarketBoard, Configuration, Log);
        _commandFactory = new CommandFactory(
            _addonInteractor,
            _pricingService,
            _marketDataProvider,
            timeout: TimeSpan.FromSeconds(5),
            interactionDelay: TimeSpan.FromMilliseconds(200));
        _automation = new AutomationController(
            _sellRuleManager, _inventoryScanner, _commandFactory, Log, Chat);

        // Windows
        _sellRulesWindow = new SellRulesWindow(_sellRuleManager, _automation);
        _configWindow = new ConfigWindow(Configuration);
        _statusOverlay = new StatusOverlay(_automation);

        WindowSystem.AddWindow(_sellRulesWindow);
        WindowSystem.AddWindow(_configWindow);
        WindowSystem.AddWindow(_statusOverlay);

        // Commands
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the MarketDumper sell rules window."
        });

        // Context menu
        ContextMenu.OnMenuOpened += OnContextMenuOpened;

        // UI
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information("MarketDumper loaded.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        ContextMenu.OnMenuOpened -= OnContextMenuOpened;

        WindowSystem.RemoveAllWindows();
        _sellRulesWindow.Dispose();
        _configWindow.Dispose();
        _statusOverlay.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        _sellRulesWindow.Toggle();
    }

    public void ToggleConfigUi() => _configWindow.Toggle();
    public void ToggleMainUi() => _sellRulesWindow.Toggle();

    private void OnContextMenuOpened(Dalamud.Game.Gui.ContextMenu.IMenuOpenedArgs args)
    {
        // Only show for inventory items
        if (args.Target is not Dalamud.Game.Gui.ContextMenu.MenuTargetInventory inventoryTarget)
            return;

        var itemId = inventoryTarget.TargetItem.ItemId;

        // Check if item is marketable
        if (!DataManager.GetExcelSheet<Item>().TryGetRow(itemId, out var itemRow))
            return;

        if (itemRow.ItemSearchCategory.RowId == 0)
            return;

        // Don't add duplicate menu entry if rule already exists
        var label = _sellRuleManager.HasRule(itemId)
            ? "MarketDumper: Edit Rule"
            : "MarketDumper: Add Sell Rule";

        args.AddMenuItem(new Dalamud.Game.Gui.ContextMenu.MenuItem
        {
            Name = label,
            OnClicked = _ =>
            {
                if (!_sellRuleManager.HasRule(itemId))
                {
                    var rule = new SellRule
                    {
                        ItemId = itemId,
                        ItemName = itemRow.Name.ToString(),
                        StackSize = 99,
                        Enabled = true
                    };
                    _sellRuleManager.AddRule(rule);
                    Configuration.Save();
                    Chat.Print($"[MarketDumper] Added sell rule: {rule.ItemName} x{rule.StackSize}");
                }
                _sellRulesWindow.IsOpen = true;
            }
        });
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build MarketDumper/MarketDumper.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Run all tests**

```bash
dotnet test MarketDumper.Tests/MarketDumper.Tests.csproj -v normal
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

Use /quick-commit

---

## Summary

| Task | Description | Tests |
|------|-------------|-------|
| 1 | Project migration SamplePlugin → MarketDumper | Build verification |
| 2 | Models, enums, interfaces | Build verification |
| 3 | Test project + PricingService (TDD) | 10 unit tests |
| 4 | SellRuleManager (TDD) | 9 unit tests |
| 5 | CommandQueue (TDD) | 9 unit tests |
| 6 | InventoryScanner (TDD) | 7 unit tests |
| 7 | JobPlanner (TDD) | 6 unit tests |
| 8 | AddonInteractor + GameInventoryDataProvider | Build verification |
| 9 | All 6 concrete commands + CommandFactory | Build verification |
| 10 | AutomationController + MarketDataProvider | Build verification |
| 11 | 3 UI windows (SellRules, Config, StatusOverlay) | Build verification |
| 12 | Context menu + Plugin.cs wiring | Full build + all tests |

**Total: 41 unit tests across 5 test files**

Note: The AddonInteractor methods (ClickAddonButton, SetAddonInputValue, ReadAddonText) are stubbed with logging. The exact addon node indices and click callback patterns are game-version-dependent and must be discovered through runtime inspection with the live FFXIV client. This is expected — the architecture isolates this behind the IAddonInteractor interface so all other code is testable without the game running.
