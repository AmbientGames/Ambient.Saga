using Ambient.Domain;
using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Command to spawn a dev character for testing.
/// Creates the necessary saga transactions so the character can be interacted with.
/// </summary>
public record SpawnDevCharacterCommand : IRequest<SpawnDevCharacterResult>
{
    public required Guid AvatarId { get; init; }
    public required string CharacterRef { get; init; }
    public required string SagaArcRef { get; init; }
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
    public string? SagaRef { get; init; }

    public static SpawnDevCharacterResult Success(Guid characterInstanceId, string sagaRef)
        => new() { Successful = true, CharacterInstanceId = characterInstanceId, SagaRef = sagaRef };

    public static SpawnDevCharacterResult Failure(string error)
        => new() { Successful = false, ErrorMessage = error };
}
