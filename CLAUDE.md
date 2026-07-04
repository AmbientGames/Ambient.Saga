# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Ambient.Saga is a C# RPG/narrative game engine using Clean Architecture + CQRS. It features event-sourced game state, dialogue systems, combat AI, and quest tracking. Games consume this engine externally; the DirectX sandbox in this repo is the reference host.

## Build Commands

```bash
# Build (engine libraries target net8.0; the sandbox targets net10.0-windows)
dotnet build Ambient.Saga.sln
dotnet build -c Release

# Test (xUnit; all test projects target net8.0)
dotnet test
dotnet test --filter "FullyQualifiedName~GameplayTests"
dotnet test /p:CollectCoverage=true

# Build specific project
dotnet build Ambient.Saga.Engine/Ambient.Saga.Engine.csproj

# Run the sandbox
dotnet run --project Ambient.Saga.Sandbox.DirectX/Ambient.Saga.Sandbox.DirectX.csproj
```

## Architecture

```
Ambient.Domain/                    # Pure business logic, entities, value objects
Ambient.Application/               # Contracts, use cases, orchestration
Ambient.Infrastructure/            # EF Core, LiteDB, external integrations

Ambient.Saga.Engine/               # CQRS application (Commands, Queries, Handlers)
Ambient.Saga.UI/                   # ImGui game overlay
Ambient.Saga.Rendering.DirectX/    # DirectX 11 rendering
Ambient.Saga.Sandbox.DirectX/      # Development sandbox (net10.0-windows)
```

### CQRS Pattern (MediatR)

Commands and queries live in `Ambient.Saga.Engine/Application/`:
- Commands modify state: `Commands/Saga/` → handled by `Handlers/Saga/`
- Queries read state: `Queries/Saga/` or `Queries/Loading/`
- All commands pass through pipeline behaviors (`Behaviors/`): logging, validation, achievement evaluation, and `QuestStageProgressionBehavior` (automatic quest stage advancement — hosts must register it)
- Transaction log provides event sourcing for saga state changes (see `EVENT_SOURCING.md`)

### Key Domain Systems

Located in `Ambient.Saga.Engine/Domain/Rpg/`:
- `Dialogue/` - XML-based dialogue trees with conditions and actions; quests are offered and turned in through characters (there is no signpost/offer modal)
- `Battle/` - Turn-based combat; state is reconstructed from the transaction log every command (absolute per-turn snapshots, per-turn derived RNG); tell reactions are resolved server-side — never trust client-supplied damage
- `Quests/` - Quest tracking with stages; objectives count from the transaction log
- `Sagas/TransactionLog/` - Event sourcing for state changes; serialize numbers with `CultureInfo.InvariantCulture` only
- `Trade/` - Merchant/trading system (the only item-acquisition path; corpse looting was removed — `LootAwarded` is a retired transaction type)

### Definition System

World data is defined via XSD schemas:
- Schemas: `Ambient.Domain/Content/xsd/`
- Generated C# classes: `Ambient.Domain/Generated/` (never edit; regenerate via `Ambient.Domain/Scripts/BuildDefinitions.ps1`)
- Partial class extensions: `Ambient.Domain/Partials/`
- Sample world definitions (XML): `Ambient.Saga.Sandbox.DirectX/Content/worlds/` (Ise, Kagoshima)
- Build targets auto-copy Content folders to output

## NuGet Configuration

Uses official NuGet source only (see `nuget.config`).

## Code Style

- File-scoped namespaces
- Nullable reference types enabled
- Implicit usings with `GlobalUsings.cs` files
- Architecture layer dependencies enforced by tests in `ArchitectureTests.cs`

## Key Documentation

- `ARCHITECTURE.md` - Comprehensive feature documentation and usage examples
- `EVENT_SOURCING.md` - Transaction log architecture and content authoring rules

## Tech Stack

- .NET 8.0 (core) / .NET 10.0 (Windows sandbox)
- MediatR (CQRS), CommunityToolkit.Mvvm (MVVM)
- LiteDB (embedded DB), Entity Framework Core
- ImGui.NET + SharpDX (rendering)
- Steamworks.NET (Steam integration)
- xUnit + coverlet (testing)

*Verified against code 2026-07-04.*
