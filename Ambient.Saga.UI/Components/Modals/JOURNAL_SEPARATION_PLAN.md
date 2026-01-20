# Journal Modal & Debug/Release Separation Plan

## Overview

Create a **classic RPG Journal** (J key) that serves as the unified information hub. The Journal displays standard RPG content and extends it with developer information when in debug mode.

**Key Principle:** One Journal, two modes - not separate systems.

### Goals

1. **Classic RPG Journal first** - Quests, Bestiary, Locations, Lore (what players expect)
2. **Developer info as extensions** - Additional details shown inline when debug mode is on
3. **Debug mode** - Classic content + developer extensions (pixel coords, RefNames, seeds, etc.)
4. **Release mode** - Classic content only

---

## Phase 1: GameConfiguration Service

Create a runtime toggle for debug/release mode rendering.

**File:** `Ambient.Saga.UI/Configuration/GameConfiguration.cs`

```csharp
namespace Ambient.Saga.UI.Configuration;

/// <summary>
/// Runtime configuration for UI rendering modes.
/// In Debug builds, developer info is shown by default.
/// In Release builds, only player-facing info is shown.
/// </summary>
public static class GameConfiguration
{
    /// <summary>
    /// When true, modals display developer-focused information
    /// (pixel coordinates, RefNames, generation seeds, etc.)
    /// </summary>
    public static bool ShowDeveloperInfo { get; set; } =
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>
    /// Allows runtime toggle (e.g., from a debug menu or console command)
    /// </summary>
    public static void ToggleDeveloperInfo() => ShowDeveloperInfo = !ShowDeveloperInfo;
}
```

---

## Phase 2: Audit Existing Modals

### Modals Requiring Separation

| Modal | Developer Info | Player Info |
|-------|---------------|-------------|
| **CharactersModal** | Pixel coordinates (X, Y) | Name, Type, Status, Faction |
| **QuestDetailModal** | RefName in header, ISO timestamps | Title, Description, Objectives, Rewards |
| **WorldSelectionScreen** | Generation seed, file paths, creation timestamp | World name, play time |

### Modals Already Player-Only (No Changes Needed)

- AvatarInfoModal
- BattleModal (UI portion - Debug.WriteLine already console-only)
- DialogueModal
- QuestLogModal
- QuestModal
- LootModal
- MerchantTradeModal
- AchievementsModal
- FactionReputationModal
- ArchetypeSelectionModal
- SpellSelectionModal
- ItemSelectionModal
- EquipmentChangeModal
- AffinityChangeModal
- StanceChangeModal
- PauseMenuModal

---

## Phase 3: Update Modals with Conditional Rendering

### CharactersModal Changes

**Current:**
```csharp
ImGui.Text($"Location: Pixel: ({character.X}, {character.Y})");
```

**Updated:**
```csharp
if (GameConfiguration.ShowDeveloperInfo)
{
    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"[DEV] Pixel: ({character.X}, {character.Y})");
}
```

### QuestDetailModal Changes

**Current:**
```csharp
ImGui.Text($"Quest: {quest.RefName}");
ImGui.Text($"Completed: {quest.CompletedAt:O}");
```

**Updated:**
```csharp
ImGui.Text(quest.DisplayName);
if (GameConfiguration.ShowDeveloperInfo)
{
    ImGui.SameLine();
    ImGui.TextColored(DevInfoColor, $"[{quest.RefName}]");
    ImGui.Text($"Completed: {quest.CompletedAt:O}");
}
else
{
    ImGui.Text($"Completed: {quest.CompletedAt:d}"); // Short date for players
}
```

### WorldSelectionScreen Changes

**Current:**
```csharp
ImGui.Text($"Seed: {world.Seed}");
ImGui.Text($"Path: {world.FilePath}");
ImGui.Text($"Created: {world.CreatedAt}");
```

**Updated:**
```csharp
if (GameConfiguration.ShowDeveloperInfo)
{
    ImGui.TextColored(DevInfoColor, $"[DEV] Seed: {world.Seed}");
    ImGui.TextColored(DevInfoColor, $"[DEV] Path: {world.FilePath}");
    ImGui.TextColored(DevInfoColor, $"[DEV] Created: {world.CreatedAt:O}");
}
```

---

## Phase 4: Create JournalModal

A classic RPG journal accessible via **J key** that aggregates player-facing information.

**File:** `Ambient.Saga.UI/Components/Modals/JournalModal.cs`

### Structure

```
+--------------------------------------------------+
|                    JOURNAL                    [X] |
+--------------------------------------------------+
| [Quests] [Bestiary] [Locations] [Lore] [Stats]   |
+--------------------------------------------------+
|                                                   |
|  (Tab content area)                               |
|                                                   |
+--------------------------------------------------+
```

### Tab Definitions

Each tab shows classic RPG content, with developer extensions shown in debug mode.

#### 1. Quests Tab
**Classic Content:**
- Active Quests with objectives and progress bars
- Completed Quests with completion dates
- Failed/Abandoned Quests history
- Click to expand quest details inline

**Developer Extensions (Debug Mode):**
- Quest RefName in brackets after title
- Saga RefName and instance ID
- Current stage RefName
- Transaction timestamps (ISO format)
- Objective type metadata

#### 2. Bestiary Tab
**Classic Content:**
- Characters Met - Name, description, faction
- Creatures Defeated - Combat statistics per enemy type
- Relationship status (Friendly/Hostile/Neutral)
- First encounter date

**Developer Extensions (Debug Mode):**
- Character RefName and archetype
- Pixel coordinates (X, Y)
- Spawn trigger RefName
- Character instance ID
- Alive/Defeated state with defeat transaction ID

#### 3. Locations Tab
**Classic Content:**
- Discovered Locations with descriptions
- Region grouping
- Discovery date
- Notable NPCs at each location

**Developer Extensions (Debug Mode):**
- Location RefName
- GPS coordinates (Latitude/Longitude)
- Pixel bounds (min/max X, Y)
- Associated Saga RefNames
- Trigger activation history

#### 4. Lore Tab
**Classic Content:**
- Story Entries - Narrative snippets collected from dialogue
- World History - Background lore entries
- Character Biographies - Unlocked character backstories
- Collected Notes - Documents, letters found in world

**Developer Extensions (Debug Mode):**
- Source DialogueTree RefName and NodeId
- Unlock transaction ID and timestamp
- Lore entry RefName
- Prerequisite conditions met

#### 5. World Tab
**Classic Content:**
- World name and theme
- Play time (total and current session)
- Creation date (friendly format)

**Developer Extensions (Debug Mode):**
- World RefName and configuration path
- Generation seed
- File system paths
- Theme RefName and override count
- World bounds and chunk count

### Context

```csharp
// Uses MainViewModel as context (same as most player modals)
public record JournalContext(MainViewModel ViewModel);
```

### Key Binding

In input handler (likely `MainWindow.cs` or input processing):
```csharp
if (keyPressed == Keys.J && !IsModalOpen)
{
    _modalManager.OpenModal("Journal");
}
```

---

## Phase 5: File Structure

```
Ambient.Saga.UI/
├── Configuration/
│   └── GameConfiguration.cs          [NEW]
│
├── Components/
│   └── Modals/
│       ├── JournalModal.cs           [NEW]
│       ├── Adapters/
│       │   └── JournalModalAdapter.cs [NEW]
│       │
│       ├── CharactersModal.cs        [MODIFY - add conditional]
│       ├── QuestDetailModal.cs       [MODIFY - add conditional]
│       └── WorldSelectionScreen.cs   [MODIFY - add conditional]
```

---

## Phase 6: Implementation Order

1. **Create GameConfiguration.cs** - The toggle mechanism
2. **Update CharactersModal** - Add conditional around pixel coords
3. **Update QuestDetailModal** - Add conditional around RefName/timestamps
4. **Update WorldSelectionScreen** - Add conditional around seed/paths
5. **Create JournalModal.cs** - Basic structure with tabs
6. **Create JournalModalAdapter.cs** - Registry integration
7. **Register in ModalManager** - Add to modal registry
8. **Add J key binding** - Input handler update
9. **Populate Journal tabs** - Pull data from existing sources

---

## Phase 7: Testing Checklist

### Debug Mode (ShowDeveloperInfo = true)
- [ ] CharactersModal shows pixel coordinates with [DEV] prefix
- [ ] QuestDetailModal shows RefName and ISO timestamps
- [ ] WorldSelectionScreen shows seed, path, creation time
- [ ] All existing functionality preserved

### Release Mode (ShowDeveloperInfo = false)
- [ ] CharactersModal hides pixel coordinates
- [ ] QuestDetailModal shows only DisplayName and friendly dates
- [ ] WorldSelectionScreen shows only world name and play time
- [ ] Journal accessible and fully functional

### Journal Modal
- [ ] Opens with J key
- [ ] All 5 tabs render correctly
- [ ] Data pulled from correct sources
- [ ] Closes properly, lifecycle hooks work
- [ ] No developer info shown in Release mode

---

## Design Decisions

### Why Runtime Toggle vs Compile-Time Only?

A runtime toggle allows:
- Developers to test "player view" without rebuilding
- Future: Debug menu option for power users
- Easier QA testing of both modes

### Why One Journal With Two Modes?

The unified Journal approach means:
- **Single source of truth** - All information in one place
- **Classic RPG feel** - Players get familiar journal experience
- **Developer convenience** - Extra details inline, no context switching
- **Easier maintenance** - One modal to update, not two parallel systems

### How Developer Extensions Appear

Developer info appears **inline** with classic content, not in separate sections:

```
+------------------------------------------+
| The Dark Forest                          |  <- Classic: Location name
| [LOC_DARK_FOREST]                        |  <- Dev: RefName (gray, smaller)
|                                          |
| A dense woodland shrouded in mist...     |  <- Classic: Description
|                                          |
| Discovered: March 15, 2024               |  <- Classic: Friendly date
| [2024-03-15T14:32:01Z] [TXN: a4f2...]    |  <- Dev: ISO timestamp, transaction ID
|                                          |
| GPS: 35.6762, 139.6503                   |  <- Dev: Coordinates
| Pixel: (1024, 768) - (2048, 1536)        |  <- Dev: Bounds
+------------------------------------------+
```

### Visual Distinction for Developer Info

Developer extensions use:
- **Gray color** (`0.6, 0.6, 0.6, 1.0`) - Clearly secondary
- **Smaller font** where ImGui supports it
- **Brackets** around technical identifiers `[REF_NAME]`
- **[DEV]** prefix for standalone developer lines

### Existing Modals Strategy

The existing specialized modals (CharactersModal, QuestDetailModal, etc.) remain available and also get the debug/release conditional rendering. The Journal aggregates their data but doesn't replace them - power users and developers may prefer the focused modals.

---

## Future Considerations

- **Settings Toggle**: Add "Show Developer Info" to settings menu for power users
- **Journal Notifications**: Badge on J key icon when new entries added
- **Journal Search**: Filter across all tabs
- **Export Journal**: Save journal entries to file for sharing

