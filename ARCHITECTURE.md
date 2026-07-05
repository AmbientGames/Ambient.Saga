# Ambient.Saga Architecture

A comprehensive RPG/narrative game engine built with modern .NET patterns. This document explains the key features and how to use them.

## Table of Contents

- [Overview](#overview)
- [Key Features](#key-features)
- [Getting Started](#getting-started)
- [Game Systems](#game-systems)
- [Creating Content](#creating-content)
- [CQRS Command Reference](#cqrs-command-reference)
- [Project Structure](#project-structure)

---

## Overview

Ambient.Saga is a **narrative RPG engine** that provides:

- **Turn-based combat** with AI opponents, status effects, elemental affinities, telegraphed attacks (tells/reactions), and companions
- **Branching dialogue** with conditions, actions, and quest integration — the front door for quest offers, turn-ins, trade, and battles
- **Quest system** with multi-stage objectives, prerequisites, automatic stage advancement, and character-driven turn-in
- **Event sourcing** for deterministic state replay, save/load, and multiplayer sync
- **Proximity-driven world** — trigger rings spawn characters; hostile characters can initiate battle on approach
- **Achievement tracking** with Steam integration
- **Faction reputation** with spillover effects between allied/enemy factions
- **Trading system** for merchant interactions
- **Party management** for companion NPCs

The engine uses **Clean Architecture** with **CQRS** (Command Query Responsibility Segregation) via MediatR, making it easy to integrate into any game client. Games consume the engine externally; `Ambient.Saga.Sandbox.DirectX` in this repo is the reference host.

---

## Key Features

### Event Sourcing & Transaction Log

All game state changes are recorded as immutable transactions. Saga arcs are self-contained — each arc's state is derived by replaying its own transaction log. Cross-arc state (quest tokens, quest progress, boss defeats, faction reputation, character traits) is projected to avatar-level progress tables on commit.

See **[EVENT_SOURCING.md](EVENT_SOURCING.md)** for the full architecture: the self-containment model, log-health rules (invariant culture, stateless transaction types, reversal), battle snapshots, avatar progress tables, quest token lifecycle, trigger gating, dialogue conditions, and content authoring rules.

### Turn-Based Combat System

Combat is **event-sourced across CQRS command boundaries**. There is no long-lived battle object: every command reconstructs the battle from the transaction log, executes exactly one slice, and persists new transactions.

```csharp
// Start a battle (also reached via dialogue StartCombat actions and proximity assault)
var start = await mediator.Send(new StartBattleCommand { ... });

// Execute one avatar turn (companion turns + the enemy response follow)
var result = await mediator.Send(new ExecuteBattleTurnCommand { ... });

// Resolve a telegraphed enemy attack (Dodge/Block/Parry/Brace)
var reaction = await mediator.Send(new SubmitReactionCommand { ... });

// Read-only reconstruction for the UI
var state = await mediator.Send(new GetBattleStateQuery { ... });
```

**Combat Features:**
- `BattleStarted` records a full initial snapshot (both combatants, companion roster, random seed); every `BattleTurnExecuted` carries **absolute post-turn snapshots for both sides** — reconstruction folds snapshots, it never re-simulates
- **Per-turn derived RNG**: each command derives its seed from the battle seed and turn index, so rolls differ per turn while replays stay deterministic
- **Tells & reactions**: an enemy attack may telegraph instead of landing; the tell is persisted (it survives save/reload) and the player reacts via `SubmitReactionCommand`. The outcome is computed **server-side** — client-supplied damage values are ignored. An abandoned tell auto-resolves as an un-reacted hit
- Weapon attacks with equipment requirements, spell casting with mana costs, elemental affinities, status effects both directions (DoTs tick, durations expire), critical hits, defensive stance
- Companion party members with AI control, persisted and reconstructed across commands
- **Flee** is a distinct outcome — neither victory nor defeat; no defeat triggers fire, and a re-engaged enemy keeps the damage it took
- Equipment durability accumulates across a battle (equipment is disposable by design — there is no repair mechanic)
- Battle dialogue triggers (boss taunts on health thresholds / turn numbers / outcome; `OnDefeat` does not fire on flee)

The domain core (`BattleEngine`, `Combatant`, `CombatAI` in `Ambient.Saga.Engine/Domain/Rpg/Battle/`) executes individual decisions; the handlers own reconstruction and persistence.

### Dialogue System

XML-based dialogue trees with rich condition and action support:

```xml
<DialogueTree RefName="merchant_greeting" StartNodeId="hello">
    <Node NodeId="hello">
        <Text>Welcome, traveler! Care to see my wares?</Text>
        <Choice Text="Show me what you have" NextNodeId="shop" />
        <Choice Text="I'm looking for information" NextNodeId="rumors" />
        <Choice Text="Goodbye" NextNodeId="farewell" />
    </Node>

    <Node NodeId="rumors">
        <Condition Type="HasQuestToken" RefName="SEEKING_INFO" />
        <Text>Word is the bandits have a new hideout...</Text>
        <Choice Text="Interesting." NextNodeId="farewell" />
    </Node>

    <Node NodeId="shop">
        <Text>Here's my finest merchandise!</Text>
        <Action Type="OpenMerchantTrade" CharacterRef="merchant_bob" />
    </Node>
</DialogueTree>
```

Conditions on a node are AND-combined and gate entry to it; a node's `NextNodeId` is the fallback route when its conditions fail.

**Dialogue Conditions** (selection):
- `HasQuestToken` / `LacksQuestToken` - Proof-of-progress token checks (cross-arc)
- `QuestActive` / `QuestCompleted` / `QuestNotStarted` - Quest state checks (cross-arc)
- `BossDefeatedCount` - Defeat-count check (cross-arc)
- `ReputationLevel` / `ReputationValue` - Faction standing (cross-arc)
- `TraitComparison` - Character trait comparison (cross-arc)
- `HasEquipment` / `LacksEquipment`, `HasConsumable` / `LacksConsumable`, `HasMaterial`, `HasBlock`, `HasTool`, `HasSpell` - Inventory checks
- `HasAchievement` - Achievement check
- `Credits` / `Health` - Numeric avatar checks
- `IsInParty` / `PartySize` / `PartySlotAvailable` - Party composition

**Dialogue Actions** (selection):
- `AcceptQuest` / `CompleteQuest` / `AbandonQuest` - Quest management (this is how quests are offered and can be turned in)
- `GiveQuestToken` - Token award (event-sourced via `QuestTokenAwarded` transaction, projected to avatar progress table)
- `GiveEquipment` / `GiveConsumable` / `GiveMaterial` / `GiveTool` / `GiveSpell` (+ `Take*` variants) - Item rewards
- `TransferCurrency` - Currency (also records a `CurrencyChanged` transaction for quest objectives)
- `ChangeReputation` - Faction standing changes (with spillover)
- `StartCombat` / `StartBossBattle` - Initiate battle
- `OpenMerchantTrade` - Open trading
- `GrantAffinity` / `ChangeAffinity` - Elemental affinity
- `JoinParty` / `LeaveParty` - Party management
- `AssignTrait` / `RemoveTrait` - Character development (can pacify or provoke — see proximity assault)
- `UnlockAchievement`, `SpawnCharacters`, `SetCharacterState`, `ChangeStance`, `CastSpell`, `ApplyStatusEffect`, `HealSelf`, `SummonAlly`, `EndBattle`

Item/token/currency actions only award on the **first committed visit** to their node (per character) — revisiting a conversation never double-grants.

### Quest System

The rule that governs everything: **things happen through characters.** Quests are offered by a character in dialogue (an `AcceptQuest` action on a choice), progressed by playing, and turned in by interacting with the quest giver. There is no signpost or field pickup.

```xml
<Quest RefName="rescue_prisoner" DisplayName="The Prisoner's Plight">
    <Prerequisites>
        <Prerequisite QuestRef="meet_the_guard"
                      FactionRef="CITY_GUARDS" RequiredReputationLevel="Friendly" />
    </Prerequisites>

    <Stages StartStage="investigate">
        <Stage RefName="investigate" DisplayName="Investigate the Prison">
            <Objectives>
                <Objective RefName="interrogate" Type="DialogueCompleted" Threshold="1"
                           DisplayName="Question the guard" DialogueRef="guard_interrogation" />
            </Objectives>
            <Branches>
                <Branch RefName="stealth_path" NextStage="sneak_in" />
                <Branch RefName="combat_path" NextStage="fight_in" />
            </Branches>
        </Stage>

        <Stage RefName="sneak_in" DisplayName="Sneak Past Guards">
            <Objectives>
                <Objective RefName="reach_cell" Type="LocationReached" Threshold="1"
                           DisplayName="Reach the prison cell" LocationRef="prison_cell" />
            </Objectives>
        </Stage>
    </Stages>

    <Rewards>
        <Reward Condition="OnSuccess">
            <Experience Amount="100" />
            <Reputation FactionRef="REBELS" Amount="500" />
        </Reward>
    </Rewards>
</Quest>
```

**Lifecycle:**
1. **Offer & accept** — a dialogue choice carries `AcceptQuest`; the NPC becomes the quest's giver. Prerequisites (`QuestRef`, `MinimumLevel`, `RequiredItemRef` — a `TOKEN_*` value means a quest token — `RequiredAchievementRef`, `FactionRef` + `RequiredReputationLevel`) are enforced against both arc-local state and the cross-arc projection (quests completed in other arcs count).
2. **Progress** — there is no separate progress store: `QuestProgressEvaluator` counts matching transactions, **scoped to the current acceptance** (events from before you accepted never count).
3. **Automatic stage advancement** — `QuestStageProgressionBehavior` (a MediatR pipeline behavior every host registers) runs after each successful saga command and advances the stage when its objectives complete. Branch stages wait for an explicit `ChooseQuestBranchCommand` (a player decision).
4. **Turn-in via the giver** — the final stage is held even when complete; it advances (and the quest completes, distributing rewards) when the player interacts with the quest giver again. Authored `CompleteQuest` dialogue actions also work anywhere (turn in at a different NPC). Quests with no recorded giver complete immediately on the final stage.
5. **Abandon / re-accept** — re-accepting starts a fresh scope; old progress and branch choices do not carry over.

**Objective Types**: `TriggerActivated` / `LocationReached` (trigger refs), `CharacterDefeated`, `CharactersDefeatedByTag` / `CharactersDefeatedByType`, `ItemCollected`, `ItemDelivered`, `ItemTraded`, `DialogueCompleted`, `DialogueChoiceSelected`, `DialogueNodeVisited`, `QuestTokenCollected`, `CurrencyCollected`, `SagaDiscovered`. Every type has a real gameplay producer (producer-less types `ItemCrafted` and `Custom` were removed 2026-07-04). Each has a `Threshold`; `Optional="true"` objectives don't block stage completion.

**Rewards** (OnSuccess / OnBranch / OnObjective): Currency, Experience, Equipment, Consumable are applied to the avatar; Achievement unlocks on the avatar's ledger; Reputation is emitted as `ReputationChanged` transactions (with faction spillover) committed atomically with the causing quest transaction.

### Proximity & Interaction

Nothing proximity-related happens on its own — the host sends `UpdateAvatarPositionCommand` (the position pump) roughly once per second, and all ring evaluation happens inside that command.

- **Trigger rings** (on `SagaArc`): `DiscoverRadius` reveals the arc, `EnterRadius` fires the trigger — spawning its characters and awarding `GivesQuestTokenRef` tokens — and `ExitRadius` records departure. Triggers can be gated with `RequiresQuestTokenRef`. Entering a ring spawns characters; that is all it does.
- **Character ApproachRadius** (on the character's `<Interactable>` section): `GetAvailableInteractionsQuery` lists in-range interactable characters; `GetInitiatedInteractionQuery` (the arbiter) picks the single highest-priority in-range character that wants to engage.
- **Walk-up dialogue**: if the arbiter's winner can talk, the view model (`SagaMainViewModel`) starts the dialogue session and raises `DialogueRequested` for the host to show. The sandbox subscribes to this — clicking the map teleports the avatar, and landing inside a character's ApproachRadius is what starts the interaction.
- **Proximity assault**: a spawned, alive character whose **effective traits** (template + replayed `TraitAssigned`/`TraitRemoved`) include `Hostile` and no truce trait (`Disengaged`/`Spared`) initiates battle when approached. The engine computes `IsAssault`; the view model raises `AssaultRequested`; the host opens the battle via `ModalManager.TryOpenAssault` — the same path as clicking Attack. Assault goes **straight into battle** (menace speech is the `battle_opening` battle-dialogue trigger; talk-first villains are authored as normal dialogue with a `StartCombat` action). A successful flee assigns `Disengaged`, which suppresses further assaults from that instance; fresh spawns of the same template start hostile again.
- The arbiter check is subscription-gated: if a host subscribes neither `DialogueRequested` nor `AssaultRequested`, no engine-initiated interactions occur.

### Faction Reputation

Reputation with spillover effects between factions:

```xml
<Faction RefName="CITY_GUARDS" DisplayName="City Guard" Category="Military" StartingReputation="0">
    <Relationships>
        <Relationship FactionRef="MERCHANTS_GUILD" RelationshipType="Allied" SpilloverPercent="0.25" />
        <Relationship FactionRef="BANDITS" RelationshipType="Enemy" SpilloverPercent="0.5" />
    </Relationships>

    <ReputationRewards>
        <Reward RequiredLevel="Friendly">
            <Equipment EquipmentRef="guard_badge" Quantity="1" DiscountPercent="0.1" />
        </Reward>
        <Reward RequiredLevel="Honored">
            <Equipment EquipmentRef="guard_armor" Quantity="1" DiscountPercent="0.2" />
        </Reward>
    </ReputationRewards>
</Faction>
```

**Reputation Levels:** Hated → Hostile → Unfriendly → Neutral → Friendly → Honored → Revered → Exalted

Reputation changes flow through `ReputationChanged` transactions (dialogue `ChangeReputation` actions and quest rewards, both with spillover) and are projected to the avatar's cross-arc reputation table.

### Achievement System

Track player accomplishments with Steam integration:

```xml
<Achievement RefName="dragon_slayer" DisplayName="Dragon Slayer">
    <Criteria Type="CharactersDefeatedByTag" CharacterTag="Dragon" Threshold="1" />
</Achievement>

<Achievement RefName="master_trader" DisplayName="Master Trader">
    <Criteria Type="ItemsTraded" Threshold="100" />
</Achievement>
```

**Achievement Criteria Types** (selection): `CharactersDefeated` (+`ByRef`/`ByTag`/`ByType`), `QuestsCompleted` (+`ByRef`), `QuestTokensEarned`, `ItemsTraded`, `DialogueTreesCompleted`, `DialogueNodesVisited`, `UniqueCharactersMet`, `SagaArcsDiscovered`, `SagaArcsCompleted`, `SagaTriggersActivated`, `ReputationReached`, `FactionsAtReputationLevel`, `TraitsAssigned`, `StatusEffectsApplied`, `CriticalHitsDealt`, `DistanceTraveled`, `PlayTimeHours`.

Evaluation runs as a MediatR pipeline behavior (`AchievementEvaluationBehavior`) after each saga command; unlocks live on the avatar's ledger and replay to Steam on load.

---

## Getting Started

### Requirements

- .NET 8.0 SDK (core libraries)
- .NET 10.0 SDK (Windows sandbox - optional)
- Visual Studio 2022 17.8+ (recommended)

### Build & Test

```bash
# Build the solution
dotnet build Ambient.Saga.sln

# Run all tests
dotnet test

# Build for release
dotnet build -c Release

# Run the sandbox (Windows)
dotnet run --project Ambient.Saga.Sandbox.DirectX/Ambient.Saga.Sandbox.DirectX.csproj
```

### Basic Integration

```csharp
// 1. Set up dependency injection (see the sandbox's ServiceProviderSetup for the full picture)
services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<UpdateAvatarPositionCommand>();

    // Pipeline behaviors (run in order)
    cfg.AddOpenBehavior(typeof(SagaLoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(SagaValidationBehavior<,>));
    cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
    cfg.AddOpenBehavior(typeof(QuestStageProgressionBehavior<,>)); // automatic quest stage advancement
});

// 2. Load a world
var world = await mediator.Send(new LoadWorldQuery
{
    DataDirectory = worldsDirectory,
    ConfigurationRefName = "Kagoshima"
});

// 3. Update player position (the position pump — drives discovery, spawns, walk-up interaction)
await mediator.Send(new UpdateAvatarPositionCommand
{
    AvatarId = playerId,
    Latitude = 35.6762,
    Longitude = 139.6503
});

// 4. Get available interactions
var interactions = await mediator.Send(new GetAvailableInteractionsQuery { ... });

// 5. Start dialogue with an NPC
await mediator.Send(new StartDialogueCommand { ... });
```

The higher-level integration path is `Ambient.Saga.UI`'s `SagaMainViewModel` + `ModalManager`: the view model runs the position pump and the interaction arbiter and raises `DialogueRequested` / `AssaultRequested`; the host subscribes and opens the matching modal (see `Ambient.Saga.Sandbox.DirectX/MainWindow.cs`).

---

## Game Systems

### Saga Arcs

Sagas are geographic story containers that spawn characters via concentric trigger rings (all triggers of one arc share the arc's coordinates — distinct places are distinct arcs):

```xml
<SagaArc RefName="forest_adventure" DisplayName="Forest Adventure"
         Latitude="35.67" Longitude="139.65" Category="Wilderness" InitialState="Visible">
    <SagaTrigger RefName="forest_patrol" EnterRadius="120.0">
        <Spawn>
            <CharacterRef>wandering_merchant</CharacterRef>
        </Spawn>
    </SagaTrigger>
    <SagaTrigger RefName="forest_boss" EnterRadius="25.0">
        <RequiresQuestTokenRef>TOKEN_PATROL_CLEARED</RequiresQuestTokenRef>
        <GivesQuestTokenRef>TOKEN_BOSS_REACHED</GivesQuestTokenRef>
        <Spawn Count="1">
            <CharacterRef>forest_boss</CharacterRef>
        </Spawn>
    </SagaTrigger>
</SagaArc>
```

### Characters

Character templates carry stats, equipment, abilities, traits, and an optional `<Interactable>` section (characters without one are valid content — scenery or battle-only spawns — and are never interactable):

```xml
<Character RefName="bandit_scout" DisplayName="Bandit Scout">
    <Stats Health="0.60" Stamina="0.8" Mana="0.1"
           Strength="0.08" Defense="0.06" Speed="0.18" Magic="0.02" Credits="25" />
    <Capabilities>
        <Equipment>
            <Entry EquipmentRef="Dagger" Condition="0.7" />
            <Entry EquipmentRef="LeatherVest" Condition="0.6" />
        </Equipment>
    </Capabilities>
    <Interactable>
        <DialogueTreeRef>bandit_taunt</DialogueTreeRef>
    </Interactable>
    <Traits>
        <Trait Name="Hostile" Value="1" />     <!-- initiates proximity assault -->
        <Trait Name="FleeThreshold" Value="25" />
    </Traits>
    <Tags>
        <Tag>BanditScout</Tag>                 <!-- counted by ByTag objectives/criteria -->
    </Tags>
</Character>
```

`<GivesQuestTokenOnDefeat>` on a character awards quest tokens when it is defeated — the combat-driven token path.

### Status Effects

Attribute-based definitions applied during combat (by spells, weapons, or dialogue):

```xml
<StatusEffect RefName="Poison" DisplayName="Poison" Type="DamageOverTime"
              Category="Debuff" DamagePerTurn="5" />
<StatusEffect RefName="Bleed" DisplayName="Bleed" Type="DamageOverTime"
              Category="Debuff" DurationTurns="2" DamagePerTurn="8" MaxStacks="3" />
<StatusEffect RefName="StrengthBoost" DisplayName="Battle Fury" Type="StatBoost"
              Category="Buff" StrengthModifier="0.3" />
<StatusEffect RefName="Stun" DisplayName="Stun" Type="Stun"
              Category="Debuff" DurationTurns="1" />
```

Types include `DamageOverTime`, `StatBoost`, `Weaken`, `Stun`, `Slow`, `Root`, `Silence`, `Vulnerable`, `Blind`. Effects with no `DurationTurns` are permanent until cleansed; DoTs tick for both sides.

---

## Creating Content

### Definition System

Game content is defined via XSD schemas in `Ambient.Domain/Content/xsd/`. The schemas generate C# classes automatically.

**To regenerate definitions after schema changes:**

```powershell
cd Ambient.Domain\Scripts
.\BuildDefinitions.ps1
```

Never edit `Ambient.Domain/Generated/` by hand; extensions go in `Ambient.Domain/Partials/`.

### World Structure

Sample worlds ship with the sandbox under `Ambient.Saga.Sandbox.DirectX/Content/worlds/`:

```
Content/worlds/
├── Ise/
│   └── assets/ambient_games/xml/
│       ├── WorldConfiguration.xml
│       └── Gameplay/
│           ├── Achievements/Achievements.xml
│           ├── Acquirables/            # Equipment, Consumable, Spells, Tools, QuestTokens...
│           ├── Actors/                 # Characters, Dialogue, StatusEffects, CharacterAffinities...
│           ├── Combat/                 # AttackTells, LoadoutSlots
│           ├── Factions/Factions.xml
│           ├── Quests/Quests.xml
│           └── Sagas.xml
└── Kagoshima/
    └── ...
```

Build targets copy the Content folder to the output directory.

### Entity Naming Convention

All entities follow this pattern:
- `RefName` - Unique identifier for code references (e.g., "iron_sword")
- `DisplayName` - Human-readable name for UI (e.g., "Iron Sword")
- `Description` - Optional flavor text

---

## CQRS Command Reference

### Combat Commands

| Command | Description |
|---------|-------------|
| `StartBattleCommand` | Initialize combat (click Attack, dialogue `StartCombat`, or proximity assault); never restarts an active battle |
| `ExecuteBattleTurnCommand` | Execute the avatar's combat decision (companions + enemy response follow) |
| `SubmitReactionCommand` | Resolve a pending telegraphed attack (server computes the outcome) |
| `ApplyBattleDialogueEffectsCommand` | Apply effects from mid-battle dialogue |
| `DamageCharacterCommand` | Apply damage to a character |
| `DefeatCharacterCommand` | Mark character as defeated (awards `GivesQuestTokenOnDefeat` tokens) |

### Dialogue Commands

| Command | Description |
|---------|-------------|
| `StartDialogueCommand` | Begin conversation with NPC |
| `AdvanceDialogueCommand` | Move to next dialogue node |
| `SelectDialogueChoiceCommand` | Choose a dialogue option |
| `VisitDialogueNodeCommand` | Navigate to specific node |
| `CloseDialogueCommand` | Seal the session |

### Quest Commands

| Command | Description |
|---------|-------------|
| `AcceptQuestCommand` | Accept a quest (dispatched by dialogue `AcceptQuest` actions) |
| `AbandonQuestCommand` | Remove quest from tracking |
| `CompleteQuestCommand` | Finish quest and grant rewards |
| `AdvanceQuestStageCommand` | Move to next quest stage (normally sent by `QuestStageProgressionBehavior`) |
| `ChooseQuestBranchCommand` | Select branching path (explicit player decision) |
| `ProgressQuestObjectiveCommand` | Update objective progress |

### World Interaction Commands

| Command | Description |
|---------|-------------|
| `UpdateAvatarPositionCommand` | Move player (the position pump — triggers proximity events) |
| `TeleportAvatarCommand` | Teleport the avatar |
| `TradeItemCommand` | Buy/sell with merchants |
| `UseConsumableCommand` | Consume an item outside battle |
| `EquipItemOutsideBattleCommand` | Change equipment outside battle |
| `SharpenToolCommand` | Tool upkeep |
| `CompleteSagaCommand` | Mark a saga arc completed |
| `SpawnDevCharacterCommand` | Dev-spawn a character for testing |

### Character Commands

| Command | Description |
|---------|-------------|
| `AssignTraitCommand` | Give character a trait (e.g., pacify with `Disengaged`, provoke with `Hostile`) |

### Key Queries

| Query | Description |
|---------|-------------|
| `LoadWorldQuery` / `LoadAvailableWorldConfigurationsQuery` | World loading |
| `GetSagaStateQuery` | Replayed state of an arc |
| `GetBattleStateQuery` | Read-only battle reconstruction |
| `GetDialogueStateQuery` / `GetDialogueOptionsQuery` | Dialogue state |
| `GetAvailableInteractionsQuery` | In-range interactable characters |
| `GetInitiatedInteractionQuery` | The interaction arbiter (walk-up dialogue / proximity assault) |
| `GetQuestProgressQuery` / `GetSagaForQuestQuery` | Quest progress |
| `GetAchievementProgressQuery` | Achievement progress |
| `GetSpawnedCharactersQuery` / `GetCharacterByIdQuery` / `GetTriggersInRangeQuery` / `CanActivateTriggerQuery` | World queries |

---

## Project Structure

```
Ambient.Saga/
├── Ambient.Domain/              # Pure domain logic, no dependencies
│   ├── Content/xsd/             # XSD schema definitions
│   ├── Generated/               # Auto-generated from XSD schemas (never edit)
│   ├── Partials/                # Partial classes extending generated types
│   └── Scripts/                 # PowerShell generation scripts
│
├── Ambient.Application/         # Contracts and interfaces
│   └── Contracts/               # Repository interfaces
│
├── Ambient.Infrastructure/      # External integrations
│   └── GameLogic/               # World loading, validation
│
├── Ambient.Saga.Engine/         # Game engine (main library)
│   ├── Application/
│   │   ├── Commands/            # CQRS commands
│   │   ├── Queries/             # CQRS queries (Saga/ and Loading/)
│   │   ├── Handlers/            # Command/query handlers
│   │   ├── Behaviors/           # Pipeline behaviors (logging, validation, achievements, quest stage progression)
│   │   ├── ReadModels/          # Read-side projections
│   │   └── Results/             # Response DTOs
│   ├── Domain/
│   │   ├── AvatarProgress/      # Cross-arc progress documents
│   │   └── Rpg/
│   │       ├── Battle/          # Combat system
│   │       ├── Dialogue/        # Conversation engine
│   │       ├── Quests/          # Quest evaluation and rewards
│   │       ├── Sagas/           # Arcs, triggers, event sourcing (TransactionLog/)
│   │       ├── Trade/           # Merchant system
│   │       ├── Reputation/      # Faction standing
│   │       ├── Progression/     # Experience/levels
│   │       └── Party/           # Companion management
│   └── Infrastructure/
│       └── Persistence/         # LiteDB repositories (saga instances, avatar progress)
│
├── Ambient.Saga.UI/             # ImGui overlay (SagaMainViewModel, ModalManager, panels/modals)
├── Ambient.Saga.Rendering.DirectX/  # DirectX 11 rendering
└── Ambient.Saga.Sandbox.DirectX/    # Development sandbox (reference host; sample worlds in Content/worlds/)
```

---

## Tech Stack

| Component | Technology |
|-----------|------------|
| Framework | .NET 8.0 / .NET 10.0 (Windows sandbox) |
| CQRS | MediatR 12.4.1 |
| Database | LiteDB 5.0.21 (embedded NoSQL) |
| ORM | Entity Framework Core 8.0.11 |
| UI | ImGui.NET 1.91.6.1 + SharpDX 4.2.0 |
| Steam | Steamworks.NET 2024.8.0 |
| MVVM | CommunityToolkit.Mvvm 8.4.0 |
| Testing | xUnit + coverlet (1,038 tests) |
| Images | SixLabors.ImageSharp, SkiaSharp |
| CI/CD | GitHub Actions |
| Package | NuGet (Ambient.Saga) |

---

## CI/CD & Publishing

### Continuous Integration

All pull requests and pushes to `master` trigger the CI pipeline (`.github/workflows/ci.yml`):

1. Build solution (Release configuration)
2. Run all tests
3. Upload test results as artifacts

### NuGet Publishing

Create a GitHub Release with a tag (e.g., `v1.2.0`) to automatically publish to NuGet.org (`.github/workflows/release.yml`):

```bash
# Install the package
dotnet add package Ambient.Saga
```

The `Ambient.Saga` package includes:
- `Ambient.Domain.dll` - Core entities and business logic
- `Ambient.Application.dll` - Use cases and contracts
- `Ambient.Infrastructure.dll` - Persistence and integrations
- `Ambient.Saga.Engine.dll` - Game engine with CQRS handlers
- `Ambient.Saga.UI.dll` - ImGui overlay (OS-agnostic)
- `Ambient.Saga.Rendering.DirectX.dll` - DirectX 11 rendering (Windows-only)

---

## Known Gaps

See **[EVENT_SOURCING.md](EVENT_SOURCING.md)** — the "Known Gaps" section covers avatar-side state mutations, server-side progress tables, UI token display, and faction rewards.

---

## License

MIT License - See [LICENSE](LICENSE) for details.

*Verified against code 2026-07-04.*
