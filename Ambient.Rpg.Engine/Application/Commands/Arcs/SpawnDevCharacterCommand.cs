using Ambient.Domain;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

/// <summary>
/// Command to spawn a dev character for testing.
/// Creates the necessary arc transactions so the character can be interacted with.
/// </summary>
public record SpawnDevCharacterCommand : IRequest<SpawnDevCharacterResult>
{
    public required Guid AvatarId { get; init; }
    public required string CharacterRef { get; init; }
    public required string ArcRef { get; init; }
    public required AvatarBase Avatar { get; init; }
}

/// <summary>
/// Result of spawning a dev character.
/// </summary>
public record SpawnDevCharacterResult
{
    public bool Successful { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid CharacterInstanceId { get; init; }
    public string? ArcRef { get; init; }

    public static SpawnDevCharacterResult Success(Guid characterInstanceId, string arcRef)
        => new() { Successful = true, CharacterInstanceId = characterInstanceId, ArcRef = arcRef };

    public static SpawnDevCharacterResult Failure(string error)
        => new() { Successful = false, ErrorMessage = error };
}
