using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.Entities;

namespace Ambient.SagaEngine.Domain.Rpg.Sagas;

/// <summary>
/// Provides the data context needed for Saga interaction operations.
/// This allows ViewModels to be decoupled from MainViewModel while having access to CQRS command data.
/// MainViewModel maintains this context and updates it as data changes.
/// </summary>
public class SagaInteractionContext
{
    public World? World { get; set; }
    public AvatarEntity? AvatarEntity { get; set; }
    public Guid AvatarId { get; set; }
    public Character? ActiveCharacter { get; set; }
    public string? CurrentSagaRef { get; set; }
    public Guid? CurrentCharacterInstanceId { get; set; }

    public string CurrencyName => World?.WorldConfiguration?.CurrencyName ?? "Coin";
    public string PluralCurrencyName => CurrencyName + "s";
}
