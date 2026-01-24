# Ambient.Saga

A C# RPG/narrative game engine built with Clean Architecture and CQRS patterns.

## Features

- **Dialogue System** - XML-based dialogue trees with conditions, branching, and actions
- **Combat System** - Turn-based battles with AI
- **Quest System** - Multi-stage quest tracking
- **Achievement System** - Persistent achievement tracking via Steam integration

## Requirements

- .NET 8.0 SDK (core libraries)
- .NET 10.0 SDK (Windows UI projects)
- Visual Studio 2022 17.8+

## Building

```bash
dotnet build Ambient.Saga.sln
```

## Testing

```bash
dotnet test
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

## License

MIT License - see [LICENSE](LICENSE) for details.
