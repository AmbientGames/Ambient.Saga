using Ambient.Domain;
using Ambient.Saga.WorldForge;

namespace Ambient.Saga.WorldForge;

/// <summary>
/// Represents a narrative thread derived from MST topology
/// </summary>
public class StoryThread
{
    public string RefName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public StoryThreadType Type { get; set; }
    public List<SourceLocation> Locations { get; set; } = new();
    public int BranchDepth { get; set; } // 0 = main, 1+ = nested branches
}

public enum StoryThreadType
{
    Main,           // Longest path through MST
    Branch,         // Side story branching from main
    Convergence,    // Multiple threads converge here
    Optional        // Disconnected optional content
}

/// <summary>
/// Narrative role in story arc structure
/// </summary>
public enum NarrativeRole
{
    Introduction,
    IncitingIncident,
    RisingAction,
    Climax,
    FallingAction,
    Resolution,
    SideQuest,
    Optional,
    Secret
}

/// <summary>
/// Token chain link - represents quest progression
/// </summary>
public class TokenChainLink
{
    public SourceLocation Location { get; set; } = null!;
    public string TokenAwarded { get; set; } = string.Empty;  // TOKEN_{refName}_COMPLETE
    public List<string> TokensRequired { get; set; } = new();  // Tokens from previous locations
    public int SequenceNumber { get; set; }
    public string StoryThreadRef { get; set; } = string.Empty;
}
