using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;

namespace Ambient.Saga.UI.Services;

/// <summary>
/// Analyzes dialogue trees to determine character interaction types.
/// Used for UI visualization (Saga ring colors, character icons, etc.)
/// </summary>
public static class DialogueTreeAnalyzer
{
    /// <summary>
    /// Analyzes a character's dialogue tree to determine interaction type.
    /// Priority: Boss > Merchant > Encounter
    /// </summary>
    /// <param name="character">Character to analyze</param>
    /// <param name="world">World containing dialogue trees</param>
    /// <returns>Dialogue interaction type</returns>
    public static DialogueInteractionType GetInteractionType(Character character, IWorld world)
    {
        if (character == null || string.IsNullOrEmpty(character.Interactable?.DialogueTreeRef))
            return DialogueInteractionType.Encounter;

        // Lookup dialogue tree from world's lookup dictionary
        if (!world.DialogueTreesLookup.TryGetValue(character.Interactable.DialogueTreeRef, out var dialogueTree) || dialogueTree == null)
            return DialogueInteractionType.Encounter;

        return AnalyzeDialogueTree(dialogueTree);
    }

    /// <summary>
    /// Analyzes a dialogue tree by searching all nodes for specific action types.
    /// Priority: Boss (StartBossBattle) > Merchant (OpenMerchantTrade) > Encounter (default)
    /// </summary>
    private static DialogueInteractionType AnalyzeDialogueTree(DialogueTree tree)
    {
        if (tree.Node == null || tree.Node.Length == 0)
            return DialogueInteractionType.Encounter;

        var hasBossBattle = false;
        var hasMerchantTrade = false;

        // Search all nodes for specific action types
        foreach (var node in tree.Node)
        {
            if (node.Action == null || node.Action.Length == 0)
                continue;

            foreach (var action in node.Action)
            {
                switch (action.Type)
                {
                    case DialogueActionType.StartBossBattle:
                        hasBossBattle = true;
                        break;

                    case DialogueActionType.OpenMerchantTrade:
                        hasMerchantTrade = true;
                        break;

                    case DialogueActionType.StartCombat:
                        // Regular combat counts as encounter, not boss
                        break;
                }

                // Early exit if we found the highest priority (Boss)
                if (hasBossBattle)
                    return DialogueInteractionType.Boss;
            }
        }

        // Return based on what we found
        if (hasMerchantTrade)
            return DialogueInteractionType.Merchant;

        return DialogueInteractionType.Encounter;
    }
}

/// <summary>
/// Categorizes dialogue interactions for UI purposes.
/// Priority order: Boss > Merchant > Encounter
/// </summary>
public enum DialogueInteractionType
{
    /// <summary>
    /// Regular encounter - generic NPC interaction, combat, or quest dialogue
    /// Color: Royal Blue
    /// </summary>
    Encounter,

    /// <summary>
    /// Merchant - opens trading interface
    /// Color: Goldenrod
    /// </summary>
    Merchant,

    /// <summary>
    /// Boss - triggers boss battle
    /// Color: Dark Red
    /// </summary>
    Boss
}
