# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Ambient.Saga is a C# RPG/narrative game engine using Clean Architecture + CQRS. It features dialogue systems, combat AI, quest tracking, and procedural world generation.

## Build Commands

```bash
# Build
dotnet build Ambient.Saga.sln
dotnet build -c Release

# Test (xUnit)
dotnet test
dotnet test --filter "FullyQualifiedName~GameplayTests"
dotnet test /p:CollectCoverage=true

# Build specific project
dotnet build Ambient.Saga.Engine/Ambient.Saga.Engine.csproj
```

## Architecture

```
Ambient.Domain/                    # Pure business logic, entities, value objects
Ambient.Application/               # Contracts, use cases, orchestration
Ambient.Infrastructure/            # EF Core, LiteDB, external integrations

Ambient.Saga.Engine/               # CQRS application (Commands, Queries, Handlers)
Ambient.Saga.UI/                   # ImGui game overlay
Ambient.Saga.Rendering.DirectX/    # DirectX 11 rendering
Ambient.Saga.Sandbox.DirectX/      # Development sandbox
```

### CQRS Pattern (MediatR)

Commands and queries live in `Ambient.Saga.Engine/Application/`:
- Commands modify state: `Commands/Saga/` → handled by `Handlers/Saga/`
- Queries read state: `Queries/Saga/` or `Queries/Loading/`
- All commands pass through validation/logging pipeline behaviors (`Behaviors/`)
- Transaction log provides event sourcing for saga state changes

### Key Domain Systems

Located in `Ambient.Saga.Engine/Domain/Rpg/`:
- `Dialogue/` - XML-based dialogue trees with conditions and actions
- `Battle/` - Turn-based combat system
- `Quests/` - Quest tracking with stages
- `Sagas/TransactionLog/` - Event sourcing for state changes
- `Trade/` - Merchant/trading system

### Definition System

World data is defined via XSD schemas:
- Schemas: `Content/Schemas/`
- Generated C# classes: `Ambient.Domain/Generated/`
- Partial class extensions: `Ambient.Domain/Partials/`
- World definitions (XML): `Content/Worlds/`
- Build targets auto-copy Content folder to output

## NuGet Configuration

Uses official NuGet source only (see `nuget.config`).

## Code Style

- File-scoped namespaces
- Nullable reference types enabled
- Implicit usings with `GlobalUsings.cs` files
- Architecture layer dependencies enforced by tests in `ArchitectureTests.cs`

## Key Documentation

- `ARCHITECTURE.md` - Comprehensive feature documentation and usage examples
- `OPEN_SOURCE_READINESS.md` - CI/CD, publishing, and project status

## Tech Stack

- .NET 8.0 (core) / .NET 10.0 (Windows UI)
- MediatR (CQRS), CommunityToolkit.Mvvm (MVVM)
- LiteDB (embedded DB), Entity Framework Core
- ImGui.NET + SharpDX (rendering)
- Steamworks.NET (Steam integration)
- xUnit + coverlet (testing)
