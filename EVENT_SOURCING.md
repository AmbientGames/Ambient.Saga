# Event Sourcing in Ambient.Saga

Ambient.Saga is a drop-in RPG system. It can be integrated into any game — Archimedea, Schema, or a third-party title. This document describes the event sourcing architecture that underpins all game state.

---

## Core Principle

**The transaction log is the source of truth.** Every state change — a character spawning, a dialogue choice, a quest accepted, a boss defeated — is recorded as an immutable transaction. Current state is derived by replaying transactions, never stored directly.

```
Current State = Template (immutable, from XML) + Transactions (append-only log)
```

This gives us:
- **Replay to any point in time** for debugging and save/load
- **Deterministic state** — same transactions always produce the same result
- **Multiplayer sync** by exchanging transaction logs
- **Cheat detection** by validating transaction sequences server-side

---

## Saga Arcs

A **Saga** is a story. A **Saga Arc** is a fragment of that story, tied to a physical location in the world. One saga (e.g., BanditChief) spans multiple arcs (the town, the hideout, the boss lair).

Each arc is self-contained for its own local state:

| Component | Description |
|---|---|
| `SagaArc` | Immutable template from XML — location, triggers, metadata |
| `SagaInstance` | Per-avatar runtime container — the transaction log |
| `SagaTransaction` | Immutable event record — type, data, timestamp, sequence number |
| `SagaState` | Derived by replay — characters, triggers, dialogue visits |
| `SagaStateMachine` | Replays transactions to produce `SagaState` |

### What lives in the arc's transaction log

- Character spawns, damage, defeats
- Trigger activations and completions
- Dialogue sessions, node visits, choices
- Quest token awards (the record of what happened here)
- Item trades, loot, battle events

### What does NOT live in the arc's log

Cross-arc state — things the avatar has achieved across all arcs — lives in **avatar progress tables** (see below). An arc's log only records what happened within that arc.

Note: `SagaState.AwardedQuestTokens` is still populated during replay from the arc's own `QuestTokenAwarded` transactions. This is retained for debugging and audit (which tokens were earned in THIS arc) but is not used for gating decisions — all readers use the avatar progress table instead.

---

## Avatar Progress Tables

Stories span multiple arcs. A quest token earned in the hideout arc needs to be visible when dialogue runs in the town arc. This cross-arc state is stored in five avatar-level LiteDB collections, projected from the transaction log on commit.

| Table | Transaction Source | Used By |
|---|---|---|
| `AvatarQuestTokens` | `QuestTokenAwarded` | `HasQuestToken` / `LacksQuestToken` dialogue conditions, `TriggerAvailabilityChecker`, quest prerequisites |
| `AvatarQuestProgress` | `QuestAccepted`, `QuestCompleted`, `QuestAbandoned`, `QuestFailed`, `QuestStageAdvanced` | `QuestActive` / `QuestCompleted` / `QuestNotStarted` dialogue conditions |
| `AvatarBossDefeats` | `CharacterDefeated` | `BossDefeatedCount` dialogue condition |
| `AvatarFactionReputation` | `ReputationChanged` | `ReputationLevel` / `ReputationValue` dialogue conditions |
| `AvatarCharacterTraits` | `TraitAssigned`, `TraitRemoved` | `TraitComparison` dialogue condition |

### Projection

When `SagaInstanceRepository.AddAndCommitTransactionsAsync` commits transactions, it calls `AvatarProgressRepository.ProjectTransactions` inside the same LiteDB transaction. This is atomic — either both the arc log and the avatar table update, or neither does.

```
Transaction commits in Arc A
    |
    +---> Written to Arc A's transaction log (source of truth)
    |
    +---> Projected to avatar progress tables (derived, queryable)
```

All readers — trigger availability, dialogue conditions, quest prerequisites, UI — read from the avatar tables. No reader iterates across saga arcs.

### Server sync

Cross-device sync is a host responsibility (e.g., Archimedea's `SagaSyncService.PullAsync`) — pulling bytes, decryption, watermarks. The projection invariant stays inside Saga: `SagaInstanceRepository.ImportTransactionsAsync` inserts the newly-arrived transactions and projects them to the avatar progress tables in the **same LiteDB transaction**, so the log and the `Avatar*` tables cannot drift on imports any more than they can on local commits.

Duplicate transactions in a pull batch are skipped by `TransactionId` before projection, which keeps the non-idempotent projections (`AvatarBossDefeats.DefeatedCount`, `AvatarFactionReputation.ReputationValue`) from double-counting when a sync batch overlaps with what the client already has.

---

## Quest Tokens

Quest tokens are **permanent proof-of-progress flags**. They record that something happened — a boss was defeated, an NPC was spoken to, a trial was completed. Once earned, a token is never consumed or removed.

### How tokens are awarded

There are exactly two paths:

1. **Dialogue action** — `<Action Type="GiveQuestToken" RefName="TOKEN_REF" />` in a dialogue node. This is the primary path for narrative-driven awards (accepting a quest, completing a conversation, making a choice).

2. **Character defeat** — `<GivesQuestTokenOnDefeat>TOKEN_REF</GivesQuestTokenOnDefeat>` on a character definition. When `DefeatCharacterHandler` processes a defeat, it looks up the character template and creates `QuestTokenAwarded` transactions for each declared token. This is the path for combat-driven awards.

Both paths write `QuestTokenAwarded` transactions to the arc's log, which are then projected to the `AvatarQuestTokens` table.

### How tokens are checked

- **Dialogue conditions** — `<Condition Type="HasQuestToken" RefName="TOKEN_REF" />` and `LacksQuestToken`. These read from the avatar progress table via `DirectDialogueStateProvider.HasQuestToken`.
- **Trigger gates** — `<RequiresQuestTokenRef>TOKEN_REF</RequiresQuestTokenRef>` on saga triggers. `TriggerAvailabilityChecker` reads from the avatar progress table.
- **Quest prerequisites** — `QuestRewardDistributor.CheckPrerequisites` checks `RequiredItemRef` against the avatar's awarded tokens.

### Session buffer

During a dialogue session, tokens are awarded via `GiveQuestToken` but the transaction is not committed until the handler flushes. A session buffer (`_sessionTokens` in `DirectDialogueStateProvider`) makes tokens immediately visible to later nodes in the same dialogue, before commit.

The session buffer currently exists only for quest tokens. The other four cross-arc state types (quest progress, boss defeats, reputation, traits) do not have session buffers — their dialogue actions commit before the next condition check in practice. If same-session visibility becomes an issue for those types, the pattern can be extended.

### What tokens are NOT

- Not inventory items — they cannot be traded, dropped, or consumed
- Not loot — they are not found on defeated characters' inventories
- Not quest rewards — they are not granted as quest completion bonuses
- A single token can unlock multiple things across multiple arcs

---

## Trigger Availability

Saga triggers define proximity-based encounters. A trigger can gate on quest tokens:

```xml
<SagaTrigger RefName="BOSS_FIGHT" EnterRadius="25.0">
    <RequiresQuestTokenRef>TOKEN_OUTER_COMPLETE</RequiresQuestTokenRef>
    <GivesQuestTokenRef>TOKEN_BOSS_DEFEATED</GivesQuestTokenRef>
    <Spawn Count="1">
        <CharacterRef>BOSS_CHARACTER</CharacterRef>
    </Spawn>
</SagaTrigger>
```

`TriggerAvailabilityChecker.CanActivate` reads from the avatar progress table. If the avatar has all required tokens, the trigger can activate. When the trigger activates (avatar enters the radius), its `GivesQuestTokenRef` tokens are awarded immediately — the avatar receives proof-of-arrival before combat begins.

---

## Dialogue Conditions and Cross-Arc State

Dialogue is character-scoped, not arc-scoped. The same character (and dialogue tree) can appear in multiple arcs of the same story. Dialogue conditions that check cross-arc state read from avatar progress tables:

| Condition Type | Reads From |
|---|---|
| `HasQuestToken` / `LacksQuestToken` | `AvatarQuestTokens` |
| `QuestActive` / `QuestCompleted` / `QuestNotStarted` | `AvatarQuestProgress` |
| `BossDefeatedCount` | `AvatarBossDefeats` |
| `ReputationLevel` / `ReputationValue` | `AvatarFactionReputation` |
| `TraitComparison` | `AvatarCharacterTraits` |

Conditions that check avatar-local state (inventory, credits, health, equipment, party) read directly from the avatar entity. These are global to the avatar and do not require cross-arc lookup.

---

## Content Authoring Rules

### Do

- Award tokens via `GiveQuestToken` in dialogue or `GivesQuestTokenOnDefeat` on characters
- Gate triggers with `RequiresQuestTokenRef` / `GivesQuestTokenRef`
- Gate dialogue with `HasQuestToken` / `LacksQuestToken` conditions
- Use `QuestActive` / `QuestCompleted` conditions to prevent repeat interactions
- Name tokens with the story prefix: `BanditChief_ChiefsBadge`, `ElementalSage_TrialComplete`

### Do not

- Do not try to remove or consume tokens — they are permanent
- Do not put tokens in character loot — use `GivesQuestTokenOnDefeat` instead
- Do not put tokens in quest rewards — use dialogue `GiveQuestToken` instead
- Do not assume a token exists only in one arc — tokens are avatar-global via the progress table

---

## Architecture: Key Files

| File | Role |
|---|---|
| `Domain/Rpg/Sagas/TransactionLog/SagaTransaction.cs` | Transaction record |
| `Domain/Rpg/Sagas/TransactionLog/SagaStateMachine.cs` | Replay engine |
| `Domain/Rpg/Sagas/TransactionLog/SagaState.cs` | Derived state |
| `Domain/Rpg/Sagas/TransactionLog/SagaInstance.cs` | Per-avatar arc container |
| `Domain/AvatarProgress/` | Five document models for avatar tables |
| `Contracts/Persistence/IAvatarProgressRepository.cs` | Read + write interface |
| `Infrastructure/Persistence/AvatarProgressRepository.cs` | LiteDB implementation + projection |
| `Infrastructure/Persistence/SagaInstanceRepository.cs` | Transaction persistence + projection trigger |
| `Domain/Rpg/Sagas/TriggerAvailabilityChecker.cs` | Trigger gate evaluation |
| `Domain/Rpg/Dialogue/DirectDialogueStateProvider.cs` | Dialogue condition provider |
| `Application/Handlers/Saga/DefeatCharacterHandler.cs` | Character defeat + token award |

---

## Known Gaps

### Avatar-side state mutations without transactions

Several dialogue actions still mutate `avatar.Capabilities` directly without writing a saga transaction. They rely on `shouldAwardRewards` (first-visit idempotence) to avoid double-granting on replay, but the reward itself is only in the avatar's live state:

- `GiveConsumable` / `TakeConsumable`
- `GiveMaterial` / `TakeMaterial`
- `GiveBlock` / `TakeBlock`
- `GiveEquipment` / `TakeEquipment`
- `GiveTool` / `TakeTool`
- `GiveSpell` / `TakeSpell`
- `TransferCurrency`
- `UnlockAchievement`

These are intentionally not event-sourced yet. They represent avatar inventory changes (physical items the avatar carries), not progress flags. If drift becomes a problem, each can be migrated to a transaction-backed path following the pattern established for quest tokens.

### Server-side avatar progress tables

The server validates transactions by replaying the state machine. It does not currently maintain its own avatar progress tables. If server-side features need cross-arc queries (e.g., leaderboards, cross-player validation), a `ServerAvatarProgressRepository` should be created mirroring the client-side implementation.

### UI: Quest token display

`InventoryPanel.RenderQuestTokens` is stubbed. It previously read tokens from `avatar.Capabilities.QuestTokens` (removed). It needs to be rewired to read from the avatar progress table via a view model or read model.

### Faction reputation rewards

The faction system supports reputation levels and rewards (equipment, consumables) at each level. Quest tokens were removed from faction rewards. If faction-granted tokens are needed in the future, the reward distribution code should write `QuestTokenAwarded` transactions, not mutate avatar state.
