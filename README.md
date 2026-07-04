# Ambient.Saga

A C# RPG/narrative game engine built with Clean Architecture and CQRS patterns. Game
state is event-sourced: every change is an immutable transaction, and current state is
derived by replay.

## Features

- **Dialogue System** - XML-based dialogue trees with conditions, branching, and actions; the front door for quest offers, turn-ins, trade, and battles
- **Combat System** - Event-sourced turn-based battles with AI, telegraphed attacks (tells/reactions), companions, and proximity assault by hostile NPCs
- **Quest System** - Multi-stage quests with automatic stage advancement and character-driven turn-in
- **Achievement System** - Persistent achievement tracking via Steam integration

## Requirements

- .NET 8.0 SDK (core libraries)
- .NET 10.0 SDK (Windows sandbox)
- Visual Studio 2022 17.8+

## Building

```bash
dotnet build Ambient.Saga.sln
```

## Testing

```bash
dotnet test
```

## Try It

`Ambient.Saga.Sandbox.DirectX` is a runnable Windows sandbox (map-based, ImGui +
DirectX 11) with sample worlds (Ise, Kagoshima). Click the map to move; landing near a
character starts the interaction.

```bash
dotnet run --project Ambient.Saga.Sandbox.DirectX/Ambient.Saga.Sandbox.DirectX.csproj
```

## Project Structure

```
Ambient.Domain/              # Entities, value objects, game logic
Ambient.Application/         # Contracts and orchestration
Ambient.Infrastructure/      # EF Core, LiteDB, integrations

Ambient.Saga.Engine/         # CQRS application with MediatR
Ambient.Saga.UI/             # ImGui game overlay
Ambient.Saga.Rendering.DirectX/  # DirectX 11 rendering
Ambient.Saga.Sandbox.DirectX/    # Development sandbox
```

## Documentation

- [ARCHITECTURE.md](ARCHITECTURE.md) - engine features, game systems, content authoring, CQRS reference
- [EVENT_SOURCING.md](EVENT_SOURCING.md) - the transaction log: replay, projections, sync, authoring rules

## License

MIT License - see [LICENSE](LICENSE) for details.

*Verified against code 2026-07-04.*
