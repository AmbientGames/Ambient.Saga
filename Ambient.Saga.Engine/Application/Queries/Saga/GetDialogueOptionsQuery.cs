using MediatR;

namespace Ambient.Saga.Engine.Application.Queries.Saga;

/// <summary>
/// Query to get available dialogue options for a character.
/// Returns dialogue nodes that can be visited (considering quest tokens, visited status).
/// </summary>
public record GetDialogueOptionsQuery : IRequest<List<DialogueOptionDto>>
{
    /// <summary>
    /// Avatar viewing dialogue options
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga containing the character
    /// </summary>
    public required string SagaRef { get; init; }

    /// <summary>
    /// Character being talked to
    /// </summary>
    public required string CharacterRef { get; init; }

    /// <summary>
    /// Dialogue tree to query
    /// </summary>
    public required string DialogueTreeRef { get; init; }

    /// <summary>
    /// Current dialogue node (for branching logic)
    /// </summary>
    public string? CurrentNodeId { get; init; }
}

/// <summary>
/// DTO representing a dialogue option
/// </summary>
public record DialogueOptionDto
{
    public required string NodeId { get; init; }
    public required string DisplayText { get; init; }
    public bool HasBeenVisited { get; init; }
    public bool IsAvailable { get; init; }
    public string? BlockedReason { get; init; }
    public List<string> RequiredQuestTokens { get; init; } = new();
}
