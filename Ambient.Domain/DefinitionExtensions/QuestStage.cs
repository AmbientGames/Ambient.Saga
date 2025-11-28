namespace Ambient.Domain;

/// <summary>
/// Partial class extension for QuestStage to add helper properties.
/// Provides convenient access to Objectives or Branches without casting the Item property.
/// </summary>
public partial class QuestStage
{
    /// <summary>
    /// Helper property to access Objectives without casting Item.
    /// Returns null if Item is not QuestStageObjectives.
    /// </summary>
    [System.Xml.Serialization.XmlIgnoreAttribute()]
    public QuestStageObjectives Objectives
    {
        get { return Item as QuestStageObjectives; }
        set { Item = value; }
    }

    /// <summary>
    /// Helper property to access Branches without casting Item.
    /// Returns null if Item is not QuestStageBranches.
    /// </summary>
    [System.Xml.Serialization.XmlIgnoreAttribute()]
    public QuestStageBranches Branches
    {
        get { return Item as QuestStageBranches; }
        set { Item = value; }
    }
}
