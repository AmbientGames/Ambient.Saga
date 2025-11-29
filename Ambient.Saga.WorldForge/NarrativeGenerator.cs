using Ambient.Saga.WorldForge.NarrativeServices;
using Ambient.Saga.WorldForge.Services;
using Ambient.Saga.WorldForge;

namespace Ambient.Saga.WorldForge;

/// <summary>
/// Orchestrates narrative structure generation from MST topology.
/// Delegates to specialized generators for each narrative component.
/// </summary>
public class NarrativeGenerator
{
    private readonly TokenChainGenerator _tokenChainGenerator;
    private readonly CharacterPlacementGenerator _characterPlacementGenerator;
    private readonly QuestChainGenerator _questChainGenerator;
    private readonly LocationMetadataGenerator _metadataGenerator;

    public NarrativeGenerator(RefNameGenerator refNameGenerator)
    {
        var geographyService = new GeographyService(refNameGenerator);

        _tokenChainGenerator = new TokenChainGenerator(refNameGenerator);
        _characterPlacementGenerator = new CharacterPlacementGenerator(refNameGenerator, geographyService);
        _questChainGenerator = new QuestChainGenerator(refNameGenerator);
        _metadataGenerator = new LocationMetadataGenerator(refNameGenerator);
    }

    /// <summary>
    /// Generate complete narrative structure from MST
    /// </summary>
    public NarrativeStructure GenerateNarrative(
        List<(SourceLocation from, SourceLocation to)> mstEdges,
        List<SourceLocation> allLocations,
        string worldRefName)
    {
        var analyzer = new MSTTopologyAnalyzer();
        var storyThreads = analyzer.AnalyzeThreads(mstEdges, allLocations);

        var narrative = new NarrativeStructure
        {
            WorldRefName = worldRefName,
            StoryThreads = storyThreads,
            TokenChains = _tokenChainGenerator.GenerateTokenChains(storyThreads),
            CharacterPlacements = _characterPlacementGenerator.GenerateCharacterPlacements(storyThreads, allLocations),
            QuestChain = _questChainGenerator.GenerateQuestChain(storyThreads)
        };

        // Generate AI metadata for each location
        narrative.LocationMetadata = _metadataGenerator.GenerateLocationMetadata(storyThreads, narrative);

        return narrative;
    }
}

/// <summary>
/// Complete narrative structure for a world
/// </summary>
public class NarrativeStructure
{
    public string WorldRefName { get; set; } = string.Empty;
    public List<StoryThread> StoryThreads { get; set; } = new();
    public List<TokenChainLink> TokenChains { get; set; } = new();
    public List<CharacterPlacement> CharacterPlacements { get; set; } = new();
    public List<QuestChainLink> QuestChain { get; set; } = new();
    public Dictionary<string, LocationAIMetadata> LocationMetadata { get; set; } = new();
}

/// <summary>
/// Character placement with dialogue hints
/// </summary>
public class CharacterPlacement
{
    public SourceLocation Location { get; set; } = null!;
    public string CharacterType { get; set; } = string.Empty; // Boss, Merchant, NPC, QuestGiver
    public string CharacterRefName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string InitialGreeting { get; set; } = string.Empty;
    public string Personality { get; set; } = string.Empty;
    public string NarrativeRole { get; set; } = string.Empty;
    public List<string> MentionsSagaArcRefs { get; set; } = new(); // Nearby locations to mention
    public List<string> CharacterTags { get; set; } = new(); // Tags for quest filtering (hostile, guard, friendly, etc.)
    public string DifficultyTier { get; set; } = string.Empty; // Easy, Normal, Hard, Epic
}

/// <summary>
/// Quest chain link
/// </summary>
public class QuestChainLink
{
    public string QuestRef { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SequenceNumber { get; set; }
    public List<string> PrerequisiteQuestRefs { get; set; } = new();
    public string AwardsTokenRef { get; set; } = string.Empty;
}

/// <summary>
/// AI metadata for a location
/// </summary>
public class LocationAIMetadata
{
    public string RefName { get; set; } = string.Empty;
    public List<string> StoryThreadRefs { get; set; } = new();
    public int NarrativeSequence { get; set; }
    public NarrativeRole NarrativeRole { get; set; }
    public string Tone { get; set; } = string.Empty;
    public string ThematicTags { get; set; } = string.Empty;
    public List<NarrativeConnection> NarrativeConnections { get; set; } = new();
}

/// <summary>
/// Narrative connection between elements
/// </summary>
public class NarrativeConnection
{
    public string TargetRef { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty; // caused-by, leads-to, foreshadows, etc.
    public string Description { get; set; } = string.Empty;
}
