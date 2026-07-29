using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;

namespace Ambient.Rpg.Engine.Domain.Arcs;

/// <summary>
/// Provides the data context needed for Arc interaction operations.
/// This allows ViewModels to be decoupled from MainViewModel while having access to CQRS command data.
/// MainViewModel maintains this context and updates it as data changes.
/// </summary>
public class ArcInteractionContext
{
    public IWorld World { get; set; }
    public AvatarEntity? AvatarEntity { get; set; }
    public Guid AvatarId { get; set; }
    public Character? ActiveCharacter { get; set; }
    public string? CurrentArcRef { get; set; }
    public Guid? CurrentCharacterInstanceId { get; set; }

    public string CurrencyName => World?.WorldConfiguration?.CurrencyName ?? "Coin";
    public string PluralCurrencyName => CurrencyName + "s";
}
