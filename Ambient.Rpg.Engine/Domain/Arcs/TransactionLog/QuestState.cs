namespace Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

/// <summary>
/// Runtime state of an accepted quest within an arc.
/// Tracks multi-stage, multi-objective quest progress computed from transaction log.
/// </summary>
public class QuestState
{
    /// <summary>
    /// Quest template reference
    /// </summary>
    public string QuestRef { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the quest (cached from template for convenience)
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Current stage RefName (e.g., "INVESTIGATION", "ACCUSATION")
    /// </summary>
    public string CurrentStage { get; set; } = string.Empty;

    /// <summary>
    /// Chosen branch RefName (for exclusive branch stages)
    /// Once set, other branches in that stage become unavailable
    /// </summary>
    public string? ChosenBranch { get; set; }

    /// <summary>
    /// Completed objectives per stage: Dictionary[StageRef, HashSet[ObjectiveRef]]
    /// Example: { "INVESTIGATION" => { "TALK_WITNESSES", "FIND_WEAPON" } }
    /// </summary>
    public Dictionary<string, HashSet<string>> CompletedObjectives { get; set; } = new();

    /// <summary>
    /// Current progress values per objective: Dictionary[ObjectiveRef, CurrentValue]
    /// Example: { "TALK_WITNESSES" => 2 } (talked to 2 of 3 witnesses)
    /// </summary>
    public Dictionary<string, int> ObjectiveProgress { get; set; } = new();

    /// <summary>
    /// Whether the quest succeeded (completed all stages successfully)
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// When the quest was accepted
    /// </summary>
    public DateTime AcceptedAt { get; set; }

    /// <summary>
    /// When the quest was completed
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Reference to the quest giver character
    /// </summary>
    public string QuestGiverRef { get; set; } = string.Empty;

    /// <summary>
    /// Arc where the quest was accepted
    /// </summary>
    public string ArcRef { get; set; } = string.Empty;

    /// <summary>
    /// Whether the quest is complete
    /// </summary>
    public bool IsComplete => IsSuccess;
}

/// <summary>
/// Progress tracking for a specific objective within a quest stage.
/// Used by UI to display objective status.
/// </summary>
public class ObjectiveProgress
{
    public string ObjectiveRef { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int CurrentValue { get; set; }
    public int TargetValue { get; set; }
    public bool IsComplete { get; set; }
    public bool IsOptional { get; set; }
    public bool IsHidden { get; set; }
}

/// <summary>
/// Snapshot of quest state for UI display.
/// Computed from QuestState + Quest template.
/// </summary>
public class QuestProgressSnapshot
{
    public string QuestRef { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string CurrentStageDisplayName { get; set; } = string.Empty;
    public List<ObjectiveProgress> Objectives { get; set; } = new();
    public bool IsComplete { get; set; }
    public bool IsSuccess { get; set; }
    public float OverallProgress { get; set; } // 0.0 - 1.0 across all stages
}
