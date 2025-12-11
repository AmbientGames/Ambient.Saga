using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Infrastructure.GameLogic;
using Ambient.Saga.Engine.Infrastructure.Loading;

namespace Ambient.Saga.Engine.Tests;

public class GameplayTests : IAsyncLifetime
{
    private readonly IWorldFactory _worldFactory = new TestWorldFactory();
    private IWorld _world;

    private readonly string _dataDirectory;
    private readonly string _definitionDirectory;

    public GameplayTests()
    {
        // DefinitionXsd is copied to output directory by Ambient.Domain
        _definitionDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DefinitionXsd");

        // WorldDefinitions still lives in Sandbox source directory
        var sandboxDirectory = FindSandboxDirectory();
        _dataDirectory = Path.Combine(sandboxDirectory, "WorldDefinitions");
    }

    private static string FindSandboxDirectory()
    {
        var directory = AppDomain.CurrentDomain.BaseDirectory;
        while (directory != null)
        {
            var domainPath = Path.Combine(directory, "Ambient.Saga.Sandbox.WindowsUI");
            if (Directory.Exists(domainPath))
                return domainPath;
            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not find Sandbox directory");
    }

    public async Task InitializeAsync()
    {
        _world = await WorldAssetLoader.LoadWorldByConfigurationAsync(_worldFactory, _dataDirectory, _definitionDirectory, "Ise");

        //_world.AvailableWorldConfigurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);

        WorldBootstrapper.Initialize(_world);
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public void Consumables_ShouldNotBeNull()
    {
        Assert.NotNull(_world?.Gameplay.Consumables);
    }

    [Fact]
    public void Characters_ShouldNotBeNull()
    {
        Assert.NotNull(_world?.Gameplay.Characters);
    }

    [Fact]
    public void Tools_ShouldHaveValidRefNames()
    {
        Assert.NotNull(_world?.Gameplay.Tools);
        foreach (var tool in _world.Gameplay.Tools!)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.RefName), "Tool RefName should not be null or empty");
            var lookedUpTool = _world.TryGetToolByRefName(tool.RefName);
            Assert.NotNull(lookedUpTool);
            Assert.Equal(tool.RefName, lookedUpTool.RefName);
        }
    }

    [Fact]
    public void Materials_ShouldNotBeNull()
    {
        Assert.NotNull(_world?.Gameplay.BuildingMaterials);
    }

    [Fact]
    public void Materials_ShouldHaveValidRefNames()
    {
        Assert.NotNull(_world?.Gameplay.BuildingMaterials);
        foreach (var material in _world.Gameplay.BuildingMaterials!)
        {
            Assert.False(string.IsNullOrWhiteSpace(material.RefName), "BuildingMaterial RefName should not be null or empty");
            var lookedUpMaterial = _world.TryGetBuildingMaterialByRefName(material.RefName);
            Assert.NotNull(lookedUpMaterial);
            Assert.Equal(material.RefName, lookedUpMaterial.RefName);
        }
    }

    [Fact]
    public void DialogueTrees_ShouldNotBeNull()
    {
        Assert.NotNull(_world?.Gameplay.DialogueTrees);
    }

    //[Fact]
    //public void Structures_ShouldNotBeNull()
    //{
    //    Assert.NotNull(_world?.Gameplay.Structures);
    //}

    [Fact]
    public void Sagas_ShouldNotBeNull()
    {
        Assert.NotNull(_world?.Gameplay.SagaArcs);
    }

    //[Fact]
    //public void Landmarks_ShouldNotBeNull()
    //{
    //    Assert.NotNull(_world?.Gameplay.Landmarks);
    //}

    [Fact]
    public void Achievements_ShouldNotBeNull()
    {
        Assert.NotNull(_world?.Gameplay.Achievements);
    }

    [Fact]
    public void CharacterAffinities_ShouldNotBeNull()
    {
        Assert.NotNull(_world?.Gameplay.CharacterAffinities);
    }

    [Fact]
    public void CharacterAffinities_ShouldHaveValidRefNames()
    {
        Assert.NotNull(_world?.Gameplay.CharacterAffinities);
        foreach (var affinity in _world.Gameplay.CharacterAffinities!)
        {
            Assert.False(string.IsNullOrWhiteSpace(affinity.RefName), "CharacterAffinity RefName should not be null or empty");
            var lookedUpAffinity = _world.TryGetCharacterAffinityByRefName(affinity.RefName);
            Assert.NotNull(lookedUpAffinity);
            Assert.Equal(affinity.RefName, lookedUpAffinity.RefName);
        }
    }

    [Fact]
    public void CombatStances_ShouldNotBeNull()
    {
        Assert.NotNull(_world?.Gameplay.CombatStances);
    }

    [Fact]
    public void CombatStances_ShouldHaveValidRefNames()
    {
        Assert.NotNull(_world?.Gameplay.CombatStances);
        foreach (var combatStance in _world.Gameplay.CombatStances!)
        {
            Assert.False(string.IsNullOrWhiteSpace(combatStance.RefName), "CombatStance RefName should not be null or empty");
            var lookedUpCombatStance = _world.TryGetCombatStanceByRefName(combatStance.RefName);
            Assert.NotNull(lookedUpCombatStance);
            Assert.Equal(combatStance.RefName, lookedUpCombatStance.RefName);
        }
    }

    [Fact]
    public void CombatStances_ShouldHaveValidEffects()
    {
        Assert.NotNull(_world?.Gameplay.CombatStances);
        foreach (var combatStance in _world.Gameplay.CombatStances!)
        {
            if (combatStance.Effects == null)
                continue;

            Assert.InRange(combatStance.Effects.Strength, 0.1f, 3.0f);
            Assert.InRange(combatStance.Effects.Defense, 0.1f, 3.0f);
            Assert.InRange(combatStance.Effects.Speed, 0.1f, 3.0f);
            Assert.InRange(combatStance.Effects.Magic, 0.1f, 3.0f);
        }
    }

    // Referential Integrity Tests

    [Fact]
    public void Characters_AllDialogueTreeReferences_ShouldExist()
    {
        Assert.NotNull(_world?.Gameplay.Characters);
        foreach (var character in _world.Gameplay.Characters!)
        {
            // DialogueTreeRef is in Interactable
            if (!string.IsNullOrEmpty(character.Interactable?.DialogueTreeRef))
            {
                var exists = _world.DialogueTreesLookup.TryGetValue(character.Interactable.DialogueTreeRef, out var dialogue);
                Assert.True(exists, $"DialogueTree '{character.Interactable.DialogueTreeRef}' not found for character '{character.RefName}'");
                Assert.NotNull(dialogue);
            }
        }
    }

    [Fact]
    public void Characters_AllInventoryReferences_ShouldExist()
    {
        Assert.NotNull(_world?.Gameplay.Characters);
        foreach (var character in _world.Gameplay.Characters!)
        {
            // Validate Equipment references
            if (character.Capabilities?.Equipment != null)
            {
                foreach (var entry in character.Capabilities.Equipment)
                {
                    var exists = _world.EquipmentLookup.TryGetValue(entry.EquipmentRef, out var equipment);
                    Assert.True(exists, $"Equipment '{entry.EquipmentRef}' not found for character '{character.RefName}'");
                    Assert.NotNull(equipment);
                }
            }

            // Validate Consumable references
            if (character.Capabilities?.Consumables != null)
            {
                foreach (var entry in character.Capabilities.Consumables)
                {
                    var consumable = _world.TryGetConsumableByRefName(entry.ConsumableRef);
                    Assert.NotNull(consumable);
                }
            }

            // Validate Tool references
            if (character.Capabilities?.Tools != null)
            {
                foreach (var entry in character.Capabilities.Tools)
                {
                    var tool = _world.TryGetToolByRefName(entry.ToolRef);
                    Assert.NotNull(tool);
                }
            }

            //// Validate Block references
            //if (character.Capabilities?.Blocks != null)
            //{
            //    foreach (var entry in character.Capabilities.Blocks)
            //    {
            //        var block = _world.TryGetBlockByRefName(entry.BlockRef);
            //        Assert.NotNull(block);
            //    }
            //}

            // Validate Material references
            if (character.Capabilities?.BuildingMaterials != null)
            {
                foreach (var entry in character.Capabilities.BuildingMaterials)
                {
                    var material = _world.TryGetBuildingMaterialByRefName(entry.BuildingMaterialRef);
                    Assert.NotNull(material);
                }
            }
        }
    }

    [Fact]
    public void AvatarArchetypes_AllInventoryReferences_ShouldExist()
    {
        Assert.NotNull(_world?.Gameplay.AvatarArchetypes);
        foreach (var archetype in _world.Gameplay.AvatarArchetypes!)
        {
            ValidateCapabilitiesReferences(archetype.SpawnCapabilities, $"AvatarArchetype '{archetype.RefName}' SpawnCapabilities");
            ValidateCapabilitiesReferences(archetype.RespawnCapabilities, $"AvatarArchetype '{archetype.RefName}' RespawnCapabilities");
        }
    }

    //[Fact]
    //public void Sagas_AllContentReferences_ShouldExist()
    //{
    //    Assert.NotNull(_world?.Gameplay.SagaArcs);
    //    foreach (var saga in _world.Gameplay.SagaArcs!)
    //    {
    //        // Saga content is stored in Item property with ItemElementName indicating the type
    //        if (!string.IsNullOrEmpty(saga.Item))
    //        {
    //            switch (saga.ItemElementName)
    //            {
    //                case ItemChoiceType2.StructureRef:
    //                    var structureExists = _world.StructuresLookup.TryGetValue(saga.Item, out var structure);
    //                    Assert.True(structureExists, $"Structure '{saga.Item}' not found for Saga '{saga.RefName}'");
    //                    Assert.NotNull(structure);
    //                    break;
                        
    //               case ItemChoiceType2.LandmarkRef:
    //                    var landmarkExists = _world.LandmarksLookup.TryGetValue(saga.Item, out var landmark);
    //                    Assert.True(landmarkExists, $"Landmark '{saga.Item}' not found for Saga '{saga.RefName}'");
    //                    Assert.NotNull(landmark);
    //                    break;
    //            }
    //        }
    //    }
    //}

    //[Fact]
    //public void Tools_AllSubstanceReferences_ShouldExist()
    //{
    //    Assert.NotNull(_world?.Gameplay.Tools);
    //    foreach (var tool in _world.Gameplay.Tools!)
    //    {
    //        if (tool.EffectiveSubstances != null)
    //        {
    //            foreach (var substance in tool.EffectiveSubstances)
    //            {
    //                var materialRef = _world.TryGetSubstanceByRefName(substance.SubstanceRef);
    //                Assert.NotNull(materialRef);
    //            }
    //        }
    //    }
    //}

    [Fact]
    public void CharacterAffinities_AllMatchupReferences_ShouldExist()
    {
        Assert.NotNull(_world?.Gameplay.CharacterAffinities);
        foreach (var affinity in _world.Gameplay.CharacterAffinities!)
        {
            if (affinity.Matchup != null)
            {
                foreach (var matchup in affinity.Matchup)
                {
                    var matchupExists = _world.CharacterAffinitiesLookup.TryGetValue(matchup.TargetAffinityRef, out var referencedAffinity);
                    Assert.True(matchupExists, $"CharacterAffinity '{affinity.RefName}' references non-existent affinity '{matchup.TargetAffinityRef}' in matchup");
                    Assert.NotNull(referencedAffinity);
                }
            }
        }
    }

    [Fact]
    public void Characters_AllAffinityReferences_ShouldExist()
    {
        Assert.NotNull(_world?.Gameplay.Characters);
        foreach (var character in _world.Gameplay.Characters!)
        {
            if (!string.IsNullOrEmpty(character.AffinityRef))
            {
                var affinityExists = _world.CharacterAffinitiesLookup.TryGetValue(character.AffinityRef, out var affinity);
                Assert.True(affinityExists, $"Character '{character.RefName}' references non-existent affinity '{character.AffinityRef}'");
                Assert.NotNull(affinity);
            }
        }
    }

    [Fact]
    public void AvatarArchetypes_AllAffinityReferences_ShouldExist()
    {
        Assert.NotNull(_world?.Gameplay.AvatarArchetypes);
        foreach (var archetype in _world.Gameplay.AvatarArchetypes!)
        {
            if (!string.IsNullOrEmpty(archetype.AffinityRef))
            {
                var affinityExists = _world.CharacterAffinitiesLookup.TryGetValue(archetype.AffinityRef, out var affinity);
                Assert.True(affinityExists, $"AvatarArchetype '{archetype.RefName}' references non-existent affinity '{archetype.AffinityRef}'");
                Assert.NotNull(affinity);
            }
        }
    }

    // Helper methods

    private void ValidateCapabilitiesReferences(ItemCollection? capabilities, string context)
    {
        if (capabilities == null) return;

        // Validate Equipment references
        if (capabilities.Equipment != null)
        {
            foreach (var entry in capabilities.Equipment)
            {
                var exists = _world!.EquipmentLookup.TryGetValue(entry.EquipmentRef, out var equipment);
                Assert.True(exists, $"Equipment '{entry.EquipmentRef}' not found in {context}");
                Assert.NotNull(equipment);
            }
        }

        // Validate Consumable references
        if (capabilities.Consumables != null)
        {
            foreach (var entry in capabilities.Consumables)
            {
                var consumable = _world!.TryGetConsumableByRefName(entry.ConsumableRef);
                Assert.NotNull(consumable);
            }
        }

        // Validate Tool references
        if (capabilities.Tools != null)
        {
            foreach (var entry in capabilities.Tools)
            {
                var tool = _world!.TryGetToolByRefName(entry.ToolRef);
                Assert.NotNull(tool);
            }
        }

        //// Validate Block references
        //if (capabilities.Blocks != null)
        //{
        //    foreach (var entry in capabilities.Blocks)
        //    {
        //        var block = _world!.TryGetBlockByRefName(entry.BlockRef);
        //        Assert.NotNull(block);
        //    }
        //}

        // Validate Material references
        if (capabilities.BuildingMaterials != null)
        {
            foreach (var entry in capabilities.BuildingMaterials)
            {
                var material = _world!.TryGetBuildingMaterialByRefName(entry.BuildingMaterialRef);
                Assert.NotNull(material);
            }
        }
    }
}