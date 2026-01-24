# Weekend Goals - UI Overhaul

## Philosophy

**HUD = Survival/Gameplay essentials only**
- Health, Stamina, Mana bars (resources that deplete in real-time)
- Temperature warning indicator (not full display - just "you're freezing/overheating")
- Minimal footprint - the 3D world is the focus

**Panels = RPG depth**
- J = Journal (quests, bestiary, locations, lore, history)
- I = Inventory (with equip capability - NEW)
- C = Character (stats, equipment, abilities)
- M = Map

**Messages = Text overlay**
- Floating text that fades (not HUD real estate)
- Toast-style notifications for events
- Battle narration, quest updates, warnings

**Boundary: DevelopmentOpenSource is self-contained**
- Ambient.Saga.UI works standalone
- Schema.Codex builds on top but isn't required
- All RPG UI lives here

---

## Day 1: Foundation

### 1.1 GameConfiguration Service (30 min)

The toggle that enables dev/release mode throughout the UI.

**File:** `Ambient.Saga.UI/Configuration/GameConfiguration.cs`

```csharp
namespace Ambient.Saga.UI.Configuration;

public static class GameConfiguration
{
    public static bool ShowDeveloperInfo { get; set; } =
#if DEBUG
        true;
#else
        false;
#endif

    public static void ToggleDeveloperInfo() => ShowDeveloperInfo = !ShowDeveloperInfo;
}
```

- [x] Create file (already existed with full implementation)
- [x] Test toggle works

### 1.2 Message Overlay System (2 hours)

Replace HUD message area with floating text overlay.

**File:** `Ambient.Saga.UI/Components/Overlay/MessageOverlay.cs`

```csharp
public class MessageOverlay
{
    private readonly List<OverlayMessage> _messages = new();

    public void AddMessage(string text, MessageType type = MessageType.Info, float duration = 3f);
    public void Render(); // Called each frame, handles fade-out
}

public record OverlayMessage(string Text, MessageType Type, DateTime CreatedAt, float Duration);

public enum MessageType { Info, Warning, Error, Combat, Quest, Loot }
```

**Rendering approach:**
- Stack from bottom-right or top-center
- Fade out over last 0.5s of duration
- Color-coded by type (yellow=warning, red=error, green=loot, etc.)

- [x] Create MessageOverlay class
- [x] Integrate with GameplayOverlay
- [x] Wire up existing message sources (avatar creation, character spawn, trade)
- [ ] Remove message area from HUD (optional - keeping for now as fallback)

### 1.3 HUD Redesign (2 hours)

Minimal survival-focused HUD.

**Layout:**
```
+--------------------------------------------------+
|                                                  |
|                   3D WORLD                       |
|                                                  |
|                                                  |
|                                      [Messages]  |  <- Floating, fades
|                                      [stack]     |
|                                      [here]      |
+--------------------------------------------------+
| [HP ████████░░] [ST ██████░░░░] [MP ████░░░░░░] |  <- Resource bars
| [🌡️ COLD]                              [J][I][C] |  <- Temp warning + panel hints
+--------------------------------------------------+
```

**What's ON the HUD:**
- Health/Stamina/Mana bars (horizontal, compact)
- Temperature status (icon + text only when abnormal: "COLD", "HOT")
- Panel hotkey hints (subtle, bottom-right)

**What's NOT on the HUD:**
- Detailed stats (go to Character panel)
- Messages (now overlay)
- Equipment (go to Inventory)
- Quest info (go to Journal)

- [x] Redesign DefaultHudRenderer
- [x] Add temperature warning indicator
- [x] Add panel hotkey hints
- [x] Remove message area
- [ ] Test with survival mechanics

---

## Day 2: Equipment & Inventory

### 2.1 Equipment Outside Battle (3 hours)

Allow equip/unequip from Character panel or Inventory, not just battle.

**New Command:** `EquipItemOutsideBattleCommand`

```csharp
public record EquipItemOutsideBattleCommand : IRequest<SagaCommandResult>
{
    public required Guid AvatarId { get; init; }
    public required string EquipmentRefName { get; init; }
    public required string SlotRefName { get; init; }
}
```

**Handler logic:**
1. Validate avatar owns the equipment
2. Validate slot compatibility
3. Unequip current item in slot (if any) → inventory
4. Equip new item
5. Write transaction
6. Invalidate cache

**UI Integration:**
- AvatarInfoModal: Add "Equip" button on equipment items
- Show equipment slot dropdown when equipping
- Preview stat changes before confirming

- [ ] Create EquipItemOutsideBattleCommand
- [ ] Create handler
- [ ] Add transaction type
- [ ] Update AvatarInfoModal with equip action
- [ ] Add slot selection UI
- [ ] Test equip/unequip flow

### 2.2 Inventory Panel Improvements (1 hour)

- [ ] Show equipped indicator on items
- [ ] Add "Unequip" action for equipped items
- [ ] Group by category (weapons, armor, consumables)

---

## Day 3: Journal System

### 3.1 Journal Modal Structure (2 hours)

**File:** `Ambient.Saga.UI/Components/Modals/JournalModal.cs`

```
+--------------------------------------------------+
|                    JOURNAL                    [X] |
+--------------------------------------------------+
| [Quests] [Bestiary] [Locations] [Lore] [History] |
+--------------------------------------------------+
|                                                   |
|  (Tab content area)                               |
|                                                   |
+--------------------------------------------------+
```

- [ ] Create JournalModal.cs with tab structure
- [ ] Create JournalModalAdapter.cs
- [ ] Register in ModalManager
- [ ] Add J key binding

### 3.2 Journal Tabs (4 hours)

Each tab: Player view by default, dev extensions when `GameConfiguration.ShowDeveloperInfo`

#### Quests Tab
| Player | Dev Extension |
|--------|---------------|
| Active quests + objectives | Quest RefName, Stage RefName |
| Completed quests + date | Transaction ID, ISO timestamp |
| Progress bars | Objective type metadata |

#### Bestiary Tab
| Player | Dev Extension |
|--------|---------------|
| Characters met | RefName, Archetype |
| Defeat count | Pixel coords, Spawn trigger |
| Faction relationship | Instance ID |

#### Locations Tab
| Player | Dev Extension |
|--------|---------------|
| Discovered places | RefName, GPS coords |
| Description | Pixel bounds |
| Discovery date | Saga associations |

#### Lore Tab
| Player | Dev Extension |
|--------|---------------|
| Story entries | Source DialogueTree + NodeId |
| Collected notes | Unlock transaction ID |

#### History Tab (Transaction Log)
| Player | Dev Extension |
|--------|---------------|
| Timeline of events | Full transaction log |
| "Defeated Forest Troll" | Type, Sequence, Payload |
| "Completed quest" | Filters, Search |
| Grouped by session | Raw JSON expandable |

- [ ] Implement Quests tab
- [ ] Implement Bestiary tab
- [ ] Implement Locations tab
- [ ] Implement Lore tab
- [ ] Implement History tab (player view)
- [ ] Implement History tab (dev view)

### 3.3 Conditional Rendering in Existing Modals (1 hour)

Update modals that currently show dev info unconditionally:

- [ ] CharactersModal - conditionalize pixel coords
- [ ] QuestDetailModal - conditionalize RefName, timestamps
- [ ] WorldSelectionScreen - conditionalize seed, paths

---

## Day 4: Polish & Testing

### 4.1 Integration Testing
- [ ] Full flow: explore → encounter → battle → loot → equip → journal
- [ ] HUD stays minimal throughout
- [ ] Messages appear as overlay
- [ ] Journal captures everything
- [ ] Dev mode shows extra info
- [ ] Release mode hides dev info

### 4.2 Visual Polish
- [ ] Consistent styling across all panels
- [ ] Dev info styling (gray, bracketed)
- [ ] Message overlay animations
- [ ] HUD bar styling

### 4.3 Documentation
- [ ] Update CLAUDE.md with new UI architecture
- [ ] Document message overlay API
- [ ] Document Journal data sources

---

## File Structure

```
Ambient.Saga.UI/
├── Configuration/
│   └── GameConfiguration.cs                    [NEW]
│
├── Components/
│   ├── Overlay/
│   │   └── MessageOverlay.cs                   [NEW]
│   │
│   ├── Rendering/
│   │   └── DefaultHudRenderer.cs               [MODIFY - minimal HUD]
│   │
│   └── Modals/
│       ├── JournalModal.cs                     [NEW]
│       ├── Adapters/
│       │   └── JournalModalAdapter.cs          [NEW]
│       │
│       ├── AvatarInfoModal.cs                  [MODIFY - equip action]
│       ├── CharactersModal.cs                  [MODIFY - conditional]
│       ├── QuestDetailModal.cs                 [MODIFY - conditional]
│       └── WorldSelectionScreen.cs             [MODIFY - conditional]
│
├── Application/
│   ├── Commands/
│   │   └── EquipItemOutsideBattleCommand.cs   [NEW]
│   └── Handlers/
│       └── EquipItemOutsideBattleHandler.cs   [NEW]
```

---

## Key Decisions

| Decision | Rationale |
|----------|-----------|
| HUD = survival only | 3D world is the focus, RPG depth lives in panels |
| Messages as overlay | Frees HUD space, more flexible positioning |
| J for Journal | Classic RPG convention (Morrowind, Skyrim) |
| History tab from transactions | Transaction log is rich, surface it for players |
| Dev mode as extension | One system, two views - not parallel implementations |
| Equip outside battle | Quality of life, reduces friction |

---

## Dependencies

```
GameConfiguration ──┬── Message Overlay (uses config for dev messages)
                    ├── Journal (uses config for dev extensions)
                    └── Existing Modals (conditional rendering)

Message Overlay ───── HUD Redesign (removes message area)

Journal ────────────── Transaction Log (data source for History tab)
```

Start with GameConfiguration, then Message Overlay + HUD in parallel, then Journal.
