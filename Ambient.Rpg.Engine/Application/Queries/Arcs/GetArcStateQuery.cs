using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Queries.Arcs;

/// <summary>
/// Query to get the current state of an arc (derived from transaction log).
/// Returns full ArcState including triggers, characters, discoveries, etc.
/// </summary>
public record GetArcStateQuery : IRequest<ArcState?>
{
    /// <summary>
    /// Avatar requesting the state (for avatar-specific data like discoveries)
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc to query
    /// </summary>
    public required string ArcRef { get; init; }
}
