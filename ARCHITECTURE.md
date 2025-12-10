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

- **Turn-based combat** with AI opponents, status effects, and elemental affinities
- **Branching dialogue** with conditions, actions, and quest integration
- **Quest system** with multi-stage objectives, prerequisites, and rewards
- **Event sourcing** for deterministic state replay and time-travel debugging
- **Achievement tracking** with Steam integration
- **Faction reputation** with spillover effects between allied/enemy factions
- **Trading system** for merchant interactions
- **Party management** for companion NPCs

The engine uses **Clean Architecture** with **CQRS** (Command Query Responsibility Segregation) via MediatR, making it easy to integrate into any game client.

---

## Key Features

### Event Sourcing & Transaction Log

All game state changes are recorded as immutable transactions, enabling:

```csharp
// Replay state to any point in time
var stateMachine = new SagaStateMachine(sagaArc, triggers, world);
var currentState = stateMachine.ReplayToNow(sagaInstance);
var pastState = stateMachine.ReplayToTimestamp(sagaInstance, someDateTime);
var specificState = stateMachine.ReplayToSequence(sagaInstance, sequenceNumber);
```

**Benefits:**
- Debug by examining transaction history
- Implement save/load by persisting transaction log
- Support multiplayer by synchronizing transactions
- Detect cheating by validating transaction sequences

### Turn-Based Combat System

Rich combat with multiple mechanics:

```csharp
// Create combatants from character data
var player = new Combatant(avatarStats, playerEquipment, playerSpells);
var enemy = new Combatant(enemyStats, enemyEquipment, enemySpells);

// Initialize battle with AI
var battle = new BattleEngine(player, enemy, new CombatAI(), world);
battle.Start();

// Execute player decisions
var result = battle.ExecuteDecision(new BattleDecision
{
    ActionType = BattleActionType.Attack,
    TargetSpellRef = "Fireball"
});
```

**Combat Features:**
- Weapon attacks with equipment requirements (swords need swords equipped)
- Spell casting with mana costs and staff requirements
- Elemental affinities (Fire > Wild > Water > Fire cycle)
- Status effects (poison, regeneration, buffs/debuffs)
- Companion party members with AI control
- Flee mechanics with success chance calculation
- Critical hits based on Speed stat
- Defensive stance for damage reduction

### Dialogue System

XML-based dialogue trees with rich condition and action support:

```xml
<DialogueTree RefName="merchant_greeting" StartNodeId="hello">
    <Node NodeId="hello">
        <Text>Welcome, traveler! Care to see my wares?</Text>
        <Choice Text="Show me what you have" NextNodeId="shop" />
        <Choice Text="I'm looking for information" NextNodeId="rumors">
            <Condition Type="HasQuestToken" RefName="SEEKING_INFO" />
        </Choice>
        <Choice Text="Goodbye" NextNodeId="farewell" />
    </Node>

    <Node NodeId="shop">
        <Text>Here's my finest merchandise!</Text>
        <Action Type="OpenMerchantUI" CharacterRef="merchant_bob" />
    </Node>
</DialogueTree>
```

**Dialogue Conditions:**
- `HasQuestToken` / `LacksQuestToken` - Check inventory tokens
- `HasCompletedQuest` / `HasNotCompletedQuest` - Quest state checks
- `HasActiveQuest` - Currently tracking a quest
- `ReputationAtLeast` / `ReputationBelow` - Faction standing
- `HasEquipment` / `HasConsumable` - Inventory checks
- `HasTrait` - Character trait validation
- `HasAffinity` - Elemental affinity check
- `HasPartyMember` - Party composition

**Dialogue Actions:**
- `AcceptQuest` / `CompleteQuest` / `AbandonQuest` - Quest management
- `GiveQuestToken` / `TakeQuestToken` - Token inventory
- `GiveEquipment` / `GiveConsumable` - Item rewards
- `GiveCredits` / `TakeCredits` - Currency
- `GiveReputation` - Faction standing changes
- `StartCombat` - Initiate battle
- `GrantAffinity` - Give elemental affinity
- `JoinParty` / `LeaveParty` - Party management
- `AssignTrait` - Character development

### Quest System

Multi-stage quests with branching paths:

```xml
<Quest RefName="rescue_prisoner" DisplayName="The Prisoner's Plight">
    <Prerequisites>
        <Prerequisite Type="QuestCompleted" RefName="meet_the_guard" />
        <Prerequisite Type="ReputationAtLeast" FactionRef="CITY_GUARDS" Level="Friendly" />
    </Prerequisites>

    <Stages StartStage="investigate">
        <Stage RefName="investigate" DisplayName="Investigate the Prison">
            <Objectives>
                <Objective Type="DialogueCompleted" DialogueRef="guard_interrogation" />
            </Objectives>
            <Branches>
                <Branch RefName="stealth_path" NextStage="sneak_in" />
                <Branch RefName="combat_path" NextStage="fight_in" />
            </Branches>
        </Stage>

        <Stage RefName="sneak_in" DisplayName="Sneak Past Guards">
            <Objectives>
                <Objective Type="LocationReached" LocationRef="prison_cell" />
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

**Objective Types:**
- `CharacterDefeated` - Kill/defeat an NPC
- `DialogueCompleted` - Finish a conversation
- `ItemCollected` - Gather specific items
- `LocationReached` - Visit a place
- `QuestTokenObtained` - Acquire a token

### Faction Reputation

Reputation with spillover effects between factions:

```xml
<Faction RefName="CITY_GUARDS" DisplayName="City Guard" Category="Military">
    <Relationships>
        <Relationship FactionRef="MERCHANTS_GUILD" RelationshipType="Allied" SpilloverPercent="0.25" />
        <Relationship FactionRef="BANDITS" RelationshipType="Enemy" SpilloverPercent="0.5" />
    </Relationships>

    <ReputationRewards>
        <Reward RequiredLevel="Friendly">
            <Equipment EquipmentRef="guard_badge" DiscountPercent="0.1" />
        </Reward>
        <Reward RequiredLevel="Honored">
            <Equipment EquipmentRef="guard_armor" DiscountPercent="0.2" />
        </Reward>
    </ReputationRewards>
</Faction>
```

**Reputation Levels:** Hated → Hostile → Unfriendly → Neutral → Friendly → Honored → Revered → Exalted

### Achievement System

Track player accomplishments with Steam integration:

```xml
<Achievement RefName="dragon_slayer" DisplayName="Dragon Slayer">
    <Criteria Type="CharactersDefeated" CharacterTag="Dragon" RequiredCount="1" />
</Achievement>

<Achievement RefName="master_trader" DisplayName="Master Trader">
    <Criteria Type="TradesCompleted" RequiredCount="100" />
</Achievement>
```

**Achievement Criteria Types:**
- `CharactersDefeated` - Combat victories
- `QuestsCompleted` - Finished quests
- `TradesCompleted` - Trading activity
- `DialoguesCompleted` - Conversations finished
- `ItemsCollected` - Inventory milestones
- `ReputationReached` - Faction standing

---

## Getting Started

### Requirements

- .NET 8.0 SDK (core libraries)
- .NET 10.0 SDK (Windows UI projects - optional)
- Visual Studio 2022 17.8+ (recommended)

### Build & Test

```bash
# Build the solution
dotnet build Ambient.Saga.sln

# Run all tests
dotnet test

# Build for release
dotnet build -c Release
```

### Basic Integration

```csharp
// 1. Set up dependency injection
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(StartBattleCommand).Assembly));
services.AddSingleton<ISagaInstanceRepository, SagaInstanceRepository>();
services.AddSingleton<IGameAvatarRepository, GameAvatarRepository>();

// 2. Load a world
var world = await mediator.Send(new LoadWorldQuery { WorldConfigRef = "MyWorld" });

// 3. Update player position (triggers proximity events)
await mediator.Send(new UpdateAvatarPositionCommand
{
    AvatarId = playerId,
    NewLatitude = 35.6762,
    NewLongitude = 139.6503
});

// 4. Get available interactions
var interactions = await mediator.Send(new GetAvailableInteractionsQuery { AvatarId = playerId });

// 5. Start dialogue with an NPC
await mediator.Send(new StartDialogueCommand
{
    AvatarId = playerId,
    CharacterRef = "merchant_bob"
});
```

---

## Game Systems

### Saga Arcs

Sagas are geographic story containers that spawn characters and enable quests:

```xml
<SagaArc RefName="forest_adventure" LatitudeZ="35.67" LongitudeX="139.65">
    <Triggers>
        <Trigger RefName="forest_merchant" Type="Discovery">
            <Character CharacterRef="wandering_merchant" />
        </Trigger>
        <Trigger RefName="forest_quest" Type="QuestGiver">
            <Character CharacterRef="distressed_villager" />
            <Quest QuestRef="save_the_village" />
        </Trigger>
    </Triggers>

    <SagaFeatureRef>forest_camp</SagaFeatureRef>
</SagaArc>
```

### Character Archetypes

Define character templates with stats, equipment, and abilities:

```xml
<CharacterArchetype RefName="warrior_template">
    <Stats Health="1.0" Stamina="0.8" Mana="0.3"
           Strength="0.7" Defense="0.6" Speed="0.4" Magic="0.2" />
    <Capabilities>
        <Equipment>
            <Entry EquipmentRef="iron_sword" />
            <Entry EquipmentRef="leather_armor" />
        </Equipment>
        <Spells>
            <Entry SpellRef="battle_cry" />
        </Spells>
    </Capabilities>
    <Traits>
        <Trait Name="Brave" Value="1" />
    </Traits>
</CharacterArchetype>
```

### Status Effects

Apply buffs and debuffs during combat:

```xml
<StatusEffect RefName="poison" DisplayName="Poisoned" Category="Debuff">
    <Type>DamageOverTime</Type>
    <ApplicationMethod>EndOfTurn</ApplicationMethod>
    <Duration>3</Duration>
    <Magnitude>0.05</Magnitude> <!-- 5% max health per turn -->
</StatusEffect>

<StatusEffect RefName="berserk" DisplayName="Berserk" Category="Buff">
    <Type>StatModifier</Type>
    <ApplicationMethod>StartOfTurn</ApplicationMethod>
    <Duration>2</Duration>
    <AffectedStat>Strength</AffectedStat>
    <Magnitude>0.25</Magnitude> <!-- +25% Strength -->
</StatusEffect>
```

---

## Creating Content

### Definition System

Game content is defined via XSD schemas in `Ambient.Domain/DefinitionXsd/`. The schemas generate C# classes automatically.

**To regenerate definitions after schema changes:**

```powershell
cd Ambient.Domain\DefinitionXsd
.\BuildDefinitions.ps1
```

### World Structure

```
WorldDefinitions/
├── MyWorld/
│   ├── WorldConfiguration.xml    # World settings
│   ├── Gameplay/
│   │   ├── Characters.xml        # NPCs
│   │   ├── Quests.xml           # Quest definitions
│   │   ├── DialogueTrees.xml    # Conversations
│   │   ├── Factions.xml         # Faction relationships
│   │   ├── Equipment.xml        # Weapons, armor
│   │   ├── Consumables.xml      # Potions, food
│   │   ├── Spells.xml           # Magic abilities
│   │   ├── SagaArcs.xml         # Story containers
│   │   └── Achievements.xml     # Player achievements
│   └── HeightMaps/              # Terrain data (optional)
```

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
| `StartBattleCommand` | Initialize combat with an enemy |
| `ExecuteBattleTurnCommand` | Execute player's combat decision |
| `DamageCharacterCommand` | Apply damage to a character |
| `DefeatCharacterCommand` | Mark character as defeated |
| `LootCharacterCommand` | Collect loot from defeated enemy |

### Dialogue Commands

| Command | Description |
|---------|-------------|
| `StartDialogueCommand` | Begin conversation with NPC |
| `AdvanceDialogueCommand` | Move to next dialogue node |
| `SelectDialogueChoiceCommand` | Choose a dialogue option |
| `VisitDialogueNodeCommand` | Navigate to specific node |

### Quest Commands

| Command | Description |
|---------|-------------|
| `AcceptQuestCommand` | Add quest to active list |
| `AbandonQuestCommand` | Remove quest from tracking |
| `CompleteQuestCommand` | Finish quest and grant rewards |
| `AdvanceQuestStageCommand` | Move to next quest stage |
| `ChooseQuestBranchCommand` | Select branching path |
| `ProgressQuestObjectiveCommand` | Update objective progress |

### World Interaction Commands

| Command | Description |
|---------|-------------|
| `UpdateAvatarPositionCommand` | Move player (triggers proximity events) |
| `ActivateTriggerCommand` | Manually activate a saga trigger |
| `InteractWithFeatureCommand` | Interact with world feature |
| `TradeItemCommand` | Buy/sell with merchants |

### Character Commands

| Command | Description |
|---------|-------------|
| `AssignTraitCommand` | Give character a trait |

---

## Project Structure

```
Ambient.Saga/
├── Ambient.Domain/              # Pure domain logic, no dependencies
│   ├── DefinitionGenerated/     # Auto-generated from XSD
│   ├── DefinitionExtensions/    # Extension methods for definitions
│   ├── DefinitionXsd/           # XML Schema definitions
│   └── GameLogic/               # Core algorithms
│
├── Ambient.Application/         # Contracts and interfaces
│   └── Contracts/               # Repository interfaces
│
├── Ambient.Infrastructure/      # External integrations
│   └── GameLogic/               # World loading, validation
│
├── Ambient.Saga.Engine/         # Game engine (main library)
│   ├── Application/
│   │   ├── Commands/            # CQRS commands (25 commands)
│   │   ├── Queries/             # CQRS queries (17 queries)
│   │   ├── Handlers/            # Command/query handlers
│   │   ├── Behaviors/           # Pipeline behaviors
│   │   └── Results/             # Response DTOs
│   ├── Domain/
│   │   └── Rpg/
│   │       ├── Battle/          # Combat system
│   │       ├── Dialogue/        # Conversation engine
│   │       ├── Quests/          # Quest tracking
│   │       ├── Sagas/           # Story/event sourcing
│   │       ├── Trade/           # Merchant system
│   │       ├── Reputation/      # Faction standing
│   │       └── Party/           # Companion management
│   └── Infrastructure/
│       └── Persistence/         # LiteDB repositories
│
├── Ambient.Saga.Presentation.UI/ # ImGui overlay (Windows)
└── Ambient.Saga.Sandbox.WindowsUI/ # Development sandbox
```

---

## Tech Stack

| Component | Technology |
|-----------|------------|
| Framework | .NET 8.0 / .NET 10.0 (Windows UI) |
| CQRS | MediatR 12.4.1 |
| Database | LiteDB 5.0.21 (embedded NoSQL) |
| ORM | Entity Framework Core 8.0.11 |
| UI | ImGui.NET 1.91.6.1 + SharpDX 4.2.0 |
| Steam | Steamworks.NET 2024.8.0 |
| MVVM | CommunityToolkit.Mvvm 8.4.0 |
| Testing | xUnit + coverlet (1,024 tests) |
| Images | SixLabors.ImageSharp, SkiaSharp |
| CI/CD | GitHub Actions |
| Package | NuGet (Ambient.Saga) |

---

## CI/CD & Publishing

### Continuous Integration

All pull requests and pushes to `master` trigger the CI pipeline:

1. Build solution (Release configuration)
2. Run all 1,024 tests
3. Upload test results as artifacts

### NuGet Publishing

Create a GitHub Release with a tag (e.g., `v1.2.0`) to automatically publish to NuGet.org:

```bash
# Install the package
dotnet add package Ambient.Saga
```

The `Ambient.Saga` package includes:
- `Ambient.Domain.dll` - Core entities and business logic
- `Ambient.Application.dll` - Use cases and contracts
- `Ambient.Infrastructure.dll` - Persistence and integrations
- `Ambient.Saga.Engine.dll` - Game engine with CQRS handlers
- `DefinitionXsd/` - XML schema files for world definitions

---

## License

MIT License - See [LICENSE](LICENSE) for details.
