using Ambient.Domain;
using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Modals;

/// <summary>
/// Modal showing faction reputation standings and rewards
/// </summary>
public class FactionReputationModal
{
    private string _filterText = "";

    public void Render(MainViewModel viewModel, ref bool isOpen)
    {
        if (!isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(700, 600), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Faction Reputation", ref isOpen))
        {
            if (viewModel.CurrentWorld?.Gameplay?.Factions == null ||
                viewModel.CurrentWorld.Gameplay.Factions.Length == 0)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No factions defined");
                ImGui.Text("This world does not have a faction system.");
                ImGui.End();
                return;
            }

            // Header
            ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), "FACTION STANDINGS");
            ImGui.Separator();

            // Filter
            ImGui.SetNextItemWidth(300);
            ImGui.InputTextWithHint("##Filter", "Filter factions...", ref _filterText, 100);
            ImGui.Spacing();

            // Faction list
            ImGui.BeginChild("FactionList", new Vector2(0, 0), ImGuiChildFlags.Borders);

            foreach (var faction in viewModel.CurrentWorld.Gameplay.Factions)
            {
                if (!string.IsNullOrEmpty(_filterText) &&
                    !faction.DisplayName.Contains(_filterText, StringComparison.OrdinalIgnoreCase) &&
                    !faction.RefName.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                    continue;

                RenderFactionCard(faction, viewModel);
            }

            ImGui.EndChild();
            ImGui.End();
        }
    }

    private void RenderFactionCard(Faction faction, MainViewModel viewModel)
    {
        // Get player's reputation with this faction (would come from avatar state)
        // For now, using starting reputation as placeholder
        var currentRep = faction.StartingReputation;
        var repLevel = GetReputationLevel(currentRep);
        var (levelColor, levelName) = GetReputationDisplay(repLevel);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.12f, 0.12f, 0.15f, 0.6f));
        ImGui.BeginChild($"faction_{faction.RefName}", new Vector2(0, 180), ImGuiChildFlags.Borders);

        // Faction name and category
        ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), faction.DisplayName);
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), $"[{faction.Category}]");

        // Description
        if (!string.IsNullOrEmpty(faction.Description))
        {
            ImGui.TextWrapped(faction.Description);
        }

        ImGui.Spacing();

        // Reputation bar
        ImGui.Text("Reputation:");
        ImGui.SameLine();
        ImGui.TextColored(levelColor, levelName);
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), $"({currentRep:N0})");

        // Progress bar showing position within reputation range
        var (minRep, maxRep) = GetReputationRange(repLevel);
        var progress = maxRep > minRep ? (float)(currentRep - minRep) / (maxRep - minRep) : 0.5f;
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, levelColor);
        ImGui.ProgressBar(progress, new Vector2(-1, 20), $"{levelName}");
        ImGui.PopStyleColor();

        // Faction relationships
        if (faction.Relationships?.Length > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Relationships:");
            ImGui.SameLine();

            foreach (var rel in faction.Relationships.Take(3))
            {
                var relColor = rel.RelationshipType == FactionRelationshipRelationshipType.Allied
                    ? new Vector4(0.5f, 1, 0.5f, 1)
                    : new Vector4(1, 0.5f, 0.5f, 1);
                ImGui.TextColored(relColor, $"{rel.FactionRef} ({rel.RelationshipType})");
                ImGui.SameLine();
            }
            ImGui.NewLine();
        }

        // Available rewards at current level
        if (faction.ReputationRewards?.Length > 0)
        {
            var availableRewards = faction.ReputationRewards
                .Where(r => IsRewardUnlocked(r, repLevel))
                .ToList();

            var lockedRewards = faction.ReputationRewards
                .Where(r => !IsRewardUnlocked(r, repLevel))
                .ToList();

            if (availableRewards.Count > 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Unlocked Rewards: {availableRewards.Count}");
            }
            if (lockedRewards.Count > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), $"(+{lockedRewards.Count} locked)");
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private ReputationLevel GetReputationLevel(int reputation)
    {
        return reputation switch
        {
            < -21000 => ReputationLevel.Hated,
            < -6000 => ReputationLevel.Hostile,
            < -3000 => ReputationLevel.Unfriendly,
            < 3000 => ReputationLevel.Neutral,
            < 9000 => ReputationLevel.Friendly,
            < 21000 => ReputationLevel.Honored,
            < 42000 => ReputationLevel.Revered,
            _ => ReputationLevel.Exalted
        };
    }

    private (int min, int max) GetReputationRange(ReputationLevel level)
    {
        return level switch
        {
            ReputationLevel.Hated => (-42000, -21000),
            ReputationLevel.Hostile => (-21000, -6000),
            ReputationLevel.Unfriendly => (-6000, -3000),
            ReputationLevel.Neutral => (-3000, 3000),
            ReputationLevel.Friendly => (3000, 9000),
            ReputationLevel.Honored => (9000, 21000),
            ReputationLevel.Revered => (21000, 42000),
            ReputationLevel.Exalted => (42000, 84000),
            _ => (0, 1)
        };
    }

    private (Vector4 color, string name) GetReputationDisplay(ReputationLevel level)
    {
        return level switch
        {
            ReputationLevel.Hated => (new Vector4(0.8f, 0, 0, 1), "Hated"),
            ReputationLevel.Hostile => (new Vector4(1, 0.3f, 0.3f, 1), "Hostile"),
            ReputationLevel.Unfriendly => (new Vector4(1, 0.5f, 0.3f, 1), "Unfriendly"),
            ReputationLevel.Neutral => (new Vector4(1, 1, 0.5f, 1), "Neutral"),
            ReputationLevel.Friendly => (new Vector4(0.5f, 1, 0.5f, 1), "Friendly"),
            ReputationLevel.Honored => (new Vector4(0.3f, 0.8f, 0.3f, 1), "Honored"),
            ReputationLevel.Revered => (new Vector4(0.3f, 0.6f, 1, 1), "Revered"),
            ReputationLevel.Exalted => (new Vector4(0.8f, 0.5f, 1, 1), "Exalted"),
            _ => (new Vector4(0.5f, 0.5f, 0.5f, 1), "Unknown")
        };
    }

    private bool IsRewardUnlocked(ReputationReward reward, ReputationLevel currentLevel)
    {
        // Check if the reward's required level is at or below current level
        var levelOrder = new[]
        {
            ReputationLevel.Hated, ReputationLevel.Hostile, ReputationLevel.Unfriendly,
            ReputationLevel.Neutral, ReputationLevel.Friendly, ReputationLevel.Honored,
            ReputationLevel.Revered, ReputationLevel.Exalted
        };

        var currentIndex = Array.IndexOf(levelOrder, currentLevel);
        var requiredIndex = Array.IndexOf(levelOrder, reward.RequiredLevel);

        return currentIndex >= requiredIndex;
    }
}
