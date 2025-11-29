using Ambient.Domain;
using Ambient.Saga.WorldForge;
using Ambient.Saga.WorldForge.Services;

namespace Ambient.Saga.WorldForge.NarrativeServices;

/// <summary>
/// Generates character placements based on location type and narrative position.
/// Includes bosses, merchants, NPCs, quest givers, and hostile characters.
/// </summary>
public class CharacterPlacementGenerator
{
    private readonly RefNameGenerator _refNameGenerator;
    private readonly GeographyService _geographyService;

    public CharacterPlacementGenerator(RefNameGenerator refNameGenerator, GeographyService geographyService)
    {
        _refNameGenerator = refNameGenerator;
        _geographyService = geographyService;
    }

    public List<CharacterPlacement> GenerateCharacterPlacements(List<StoryThread> threads, List<SourceLocation> allLocations)
    {
        var placements = new List<CharacterPlacement>();
        var mainThread = threads.FirstOrDefault(t => t.Type == StoryThreadType.Main);

        foreach (var location in allLocations)
        {
            var refName = _refNameGenerator.GetRefName(location);
            CharacterPlacement? placement = null;

            switch (location.Type)
            {
                case SourceLocationType.Structure:
                    // Structures get bosses
                    var bossProgress = (double)placements.Count(p => p.CharacterType == "Boss") / Math.Max(1, allLocations.Count(l => l.Type == SourceLocationType.Structure));
                    placement = new CharacterPlacement
                    {
                        Location = location,
                        CharacterType = "Boss",
                        CharacterRefName = $"BOSS_{refName}",
                        DisplayName = $"Guardian of {location.DisplayName}",
                        InitialGreeting = $"[AI: Boss protecting {location.DisplayName}. Theme: Challenge, guardian, test of strength]",
                        Personality = "Intimidating, protective, tests worthiness",
                        NarrativeRole = "antagonist",
                        CharacterTags = new List<string> { "boss", "hostile", "elite", "guardian" },
                        DifficultyTier = bossProgress < 0.3 ? "Normal" : bossProgress < 0.7 ? "Hard" : "Epic"
                    };
                    break;

                case SourceLocationType.Landmark:
                    // Every 3rd landmark gets a merchant, others get info NPCs
                    var landmarkIndex = placements.Count(p => p.Location.Type == SourceLocationType.Landmark);
                    if (landmarkIndex % 3 == 0)
                    {
                        placement = new CharacterPlacement
                        {
                            Location = location,
                            CharacterType = "Merchant",
                            CharacterRefName = $"MERCHANT_{refName}",
                            DisplayName = $"Trader at {location.DisplayName}",
                            InitialGreeting = $"[AI: Merchant at {location.DisplayName}. Sells region-appropriate goods, shares rumors]",
                            Personality = "Friendly, business-minded, knowledgeable about area",
                            NarrativeRole = "merchant",
                            CharacterTags = new List<string> { "merchant", "friendly", "trader", "npc" },
                            DifficultyTier = "Easy"
                        };
                    }
                    else
                    {
                        placement = new CharacterPlacement
                        {
                            Location = location,
                            CharacterType = "NPC",
                            CharacterRefName = $"NPC_{refName}",
                            DisplayName = $"Traveler at {location.DisplayName}",
                            InitialGreeting = $"[AI: Traveler resting at {location.DisplayName}. Provides lore and directions]",
                            Personality = "Helpful, weary, observant",
                            NarrativeRole = "informant",
                            CharacterTags = new List<string> { "npc", "friendly", "traveler", "informant" },
                            DifficultyTier = "Easy"
                        };
                    }
                    break;

                case SourceLocationType.QuestSignpost:
                    // Quest givers
                    placement = new CharacterPlacement
                    {
                        Location = location,
                        CharacterType = "QuestGiver",
                        CharacterRefName = $"QUESTGIVER_{refName}",
                        DisplayName = $"Quest Giver at {location.DisplayName}",
                        InitialGreeting = $"[AI: Quest giver at {location.DisplayName}. Needs help with local problem]",
                        Personality = "Earnest, in need, grateful",
                        NarrativeRole = "questgiver",
                        CharacterTags = new List<string> { "questgiver", "friendly", "npc" },
                        DifficultyTier = "Easy"
                    };
                    break;
            }

            if (placement != null)
            {
                // Add narrative connections (mentions nearby locations)
                placement.MentionsSagaArcRefs = _geographyService.FindNearbyLocations(location, allLocations, maxCount: 3);
                placements.Add(placement);
            }
        }

        // Add hostile creatures/guards along the path for combat quests
        placements.AddRange(GenerateHostileCharacters(allLocations, placements.Count));

        return placements;
    }

    private List<CharacterPlacement> GenerateHostileCharacters(List<SourceLocation> allLocations, int startIndex)
    {
        var hostiles = new List<CharacterPlacement>();
        var random = new Random(42); // Deterministic

        // Add guards near structures (25% of structures)
        var structureLocations = allLocations.Where(l => l.Type == SourceLocationType.Structure).ToList();
        foreach (var structure in structureLocations.Take(Math.Max(1, structureLocations.Count / 4)))
        {
            var refName = _refNameGenerator.GetRefName(structure);
            hostiles.Add(new CharacterPlacement
            {
                Location = structure,
                CharacterType = "Guard",
                CharacterRefName = $"GUARD_{refName}_{hostiles.Count}",
                DisplayName = $"Guard at {structure.DisplayName}",
                InitialGreeting = "[AI: Defensive guard protecting the structure. May attack if provoked]",
                Personality = "Vigilant, loyal, aggressive when threatened",
                NarrativeRole = "defender",
                CharacterTags = new List<string> { "guard", "hostile", "defender", "humanoid" },
                DifficultyTier = "Normal",
                MentionsSagaArcRefs = new List<string>()
            });
        }

        // Add hostile creatures at 20% of landmarks
        var landmarks = allLocations.Where(l => l.Type == SourceLocationType.Landmark).ToList();
        foreach (var landmark in landmarks.Where((l, idx) => idx % 5 == 0))
        {
            var refName = _refNameGenerator.GetRefName(landmark);
            var creatureTypes = new[] { "Bandit", "Wild Beast", "Undead", "Elemental" };
            var creatureType = creatureTypes[random.Next(creatureTypes.Length)];

            hostiles.Add(new CharacterPlacement
            {
                Location = landmark,
                CharacterType = "HostileCreature",
                CharacterRefName = $"HOSTILE_{refName}_{hostiles.Count}",
                DisplayName = $"{creatureType} near {landmark.DisplayName}",
                InitialGreeting = $"[AI: Hostile {creatureType.ToLower()} encountered near {landmark.DisplayName}. Attacks on sight]",
                Personality = "Aggressive, territorial, dangerous",
                NarrativeRole = "enemy",
                CharacterTags = new List<string> { "hostile", creatureType.ToLower().Replace(" ", ""), "creature", "enemy" },
                DifficultyTier = "Normal",
                MentionsSagaArcRefs = new List<string>()
            });
        }

        return hostiles;
    }
}
