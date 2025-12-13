namespace Ambient.Domain.Contracts;

public interface IWorldConfiguration
{
    string RefName { get; set; }
    double SpawnLatitude { get; set; }
    double SpawnLongitude { get; set; }
    IProceduralSettings ProceduralSettings { get; set; }
    IHeightMapSettings HeightMapSettings { get; set; }
    string CurrencyName { get; set; }
    DateTime StartDate { get; set; }
    int SecondsInHour { get; set; }
    string Template { get; set; }
    object Item { get; set; }
    string DisplayName { get; set; }
    string Description { get; set; }

    string ConsumableItemsRef { get; set; }
    string SpellsRef { get; set; }
    string EquipmentRef { get; set; }
    string QuestTokensRef { get; set; }
    string CharactersRef { get; set; }
    string CharacterArchetypesRef { get; set; }
    string CharacterAffinitiesRef { get; set; }
    string CombatStancesRef { get; set; }
    string LoadoutSlotsRef { get; set; }
    string ToolsRef { get; set; }
    string BuildingMaterialsRef { get; set; }
    string DialogueTreesRef { get; set; }
    string AvatarArchetypesRef { get; set; }
    string SagaFeaturesRef { get; set; }
    string AchievementsRef { get; set; }
    string QuestsRef { get; set; }
    string SagaTriggerPatternsRef { get; set; }
    string SagaArcsRef { get; set; }
    string FactionsRef { get; set; }
    string StatusEffectsRef { get; set; }
    string AttackTellsRef { get; set; }
}