# Retainer Listing Consolidation

## Problem

When a player deposits items in multiple farming sessions, the retainer accumulates many small
listings for the same item (e.g. three listings of 10, 5, 28 Rose Garnet instead of one listing
of 43). This happens because each deposit goes into a separate inventory slot and each slot
becomes a separate retainer listing.

## Goal

Before listing new items, return any existing retainer listings that are below the configured
StackSize — but only for items the player currently has in their inventory. The returned items
are merged with the new inventory via a sort, then listed as consolidated stacks.

## Trigger condition

A listing qualifies for return when all three are true:
1. The item matches an enabled sell rule.
2. The same item exists in the player's inventory right now.
3. The listed quantity is less than the rule's StackSize.

## Command flow (per retainer)

```
SelectRetainer
OpenSellList              opens RetainerSellList (current listings)
ConsolidateListings       reads listings, returns qualifying ones in reverse slot order
SortInventory             /itemsort execute inventory + 1500ms wait
OpenSellMenu              navigates to the new-listing flow
SelectItem × N
FetchMarketPrice × N
SetPrice × N
ConfirmListing × N
CloseRetainer
```

The consolidation prefix is only emitted when the player has inventory items matching sell rules
(same guard as today). If nothing qualifies for return, `ConsolidateListingsCommand` exits
immediately without interacting with the addon.

## New components

### `RetainerListing` model
```
{ int SlotIndex, uint ItemId, int Quantity }
```
Plain data, no logic.

### `IRetainerListingReader` / `RetainerListingReader`
Reads the open `RetainerSellList` addon and returns `List<RetainerListing>`.
Uses `FFXIVClientStructs` `RetainerManager` for item IDs and quantities — same pattern as
existing `InventoryManager` usage in `AddonInteractor`. Interface is mockable for tests.

### `ConsolidateRetainerListingsCommand`
Dependencies: `IRetainerListingReader`, sell rules, current player inventory matches.

Execution:
1. Read all listings.
2. Filter to qualifying listings (trigger condition above).
3. If none qualify, return `Success`.
4. Right-click each qualifying listing in **reverse slot order** (prevents index shifting as
   slots are removed).
5. Click "Return Items to Inventory" in the context menu for each.
6. Return `Success`.

### `AddonInteractor` additions
- `RightClickRetainerListing(int slotIndex)` — right-clicks a slot in `RetainerSellList`.
- `ClickAddonButton("RetainerSellList", ...)` case for navigating to the new-listing screen
  after consolidation completes.

### `JobPlanner` changes
Accepts optional retainer listing data. When present, emits
`OpenSellList + ConsolidateListings + SortInventory` before the existing listing block for
each retainer.

## Edge cases

| Scenario | Behaviour |
|---|---|
| Player inventory full | Skip that item, log warning, continue with remaining items |
| Multiple undersized listings for same item | Return all of them |
| Nothing to consolidate | `ConsolidateListingsCommand` no-ops, run continues normally |
| Combined qty still < StackSize | One smaller listing — still correct, avoids fragmentation |
| Combined qty > 99 | Normal stack-split logic applies (99 + remainder) |
| Sort timeout | 1500ms wait kept; next scan picks up whatever consolidated |
