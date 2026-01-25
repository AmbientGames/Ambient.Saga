using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;
using Ambient.Saga.UI.Components.Utilities;
using Ambient.Saga.UI.Components.Modals;

namespace Ambient.Saga.UI.Components.Panels;

/// <summary>
/// Character panel showing the current state of the avatar.
/// Includes position, stats, equipped items, archetype, affinities, party, and lifetime statistics.
/// Accessible via C key.
/// Inventory content has moved to InventoryPanel (I key).
/// </summary>
public class CharacterPanel
{
    public void Render(SagaMainViewModel viewModel, ModalManager modalManager)
    {
        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "CHARACTER");
        ImGui.Separator();

        // Scrollable character info content
        ImGui.BeginChild("CharacterInfoScroll", new Vector2(ImGuiSizes.Fill, ImGuiSizes.Fill), ImGuiChildFlags.None);

        // Position
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "Position:");
        if (viewModel.HasAvatarPosition)
        {
            ImGui.Text($"Lat: {viewModel.AvatarLatitude:F6}");
            ImGui.Text($"Long: {viewModel.AvatarLongitude:F6}");
            ImGui.Text($"Elevation: {viewModel.AvatarElevation}m");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No position set");
            ImGui.TextWrapped("Click on map to move");
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Vitals/Stats
        if (viewModel.PlayerAvatar != null && viewModel.PlayerAvatar.Stats != null)
        {
            var vitals = viewModel.PlayerAvatar.Stats;
            var currencyName = viewModel.CurrentWorld?.WorldConfiguration?.CurrencyName ?? "Credit";
            var pluralCurrency = vitals.Credits == 1 ? currencyName : currencyName + "s";

            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Stats:");

            // Resources
            RenderStatLine("Health:", vitals.Health.ToString());
            RenderStatLine("Stamina:", vitals.Stamina.ToString());
            RenderStatLine("Mana:", vitals.Mana.ToString());
            // State
            RenderStatLine("Temperature:", $"{vitals.Temperature:F1}C");
            // Attributes
            RenderStatLine("Strength:", vitals.Strength.ToString());
            RenderStatLine("Defense:", vitals.Defense.ToString());
            RenderStatLine("Magic:", vitals.Magic.ToString());
            RenderStatLine("Speed:", vitals.Speed.ToString());
            RenderStatLine("Endurance:", vitals.Endurance.ToString());
            // Progression
            RenderStatLine($"{pluralCurrency}:", $"{vitals.Credits:N0}");

            // Archetype info with bias
            if (!string.IsNullOrEmpty(viewModel.PlayerAvatar.ArchetypeRef))
            {
                var archetype = viewModel.CurrentWorld?.Gameplay?.AvatarArchetypes?
                    .FirstOrDefault(a => a.RefName == viewModel.PlayerAvatar.ArchetypeRef);

                if (archetype != null)
                {
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();

                    // Archetype name and affinity
                    ImGui.TextColored(new Vector4(0.8f, 0.5f, 1, 1), "Archetype:");
                    ImGui.SameLine();
                    ImGui.Text(archetype.DisplayName ?? archetype.RefName);

                    if (!string.IsNullOrEmpty(archetype.AffinityRef))
                    {
                        var affinity = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?
                            .FirstOrDefault(a => a.RefName == archetype.AffinityRef);
                        var affinityName = affinity?.DisplayName ?? archetype.AffinityRef;
                        ImGui.TextColored(new Vector4(0.6f, 0.8f, 1, 1), "Affinity:");
                        ImGui.SameLine();
                        ImGui.Text(affinityName);
                    }

                    // Archetype bias (permanent stat modifiers)
                    if (archetype.ArchetypeBias != null)
                    {
                        var bias = archetype.ArchetypeBias;
                        var hasBias = bias.Strength != 0 || bias.Defense != 0 || bias.Speed != 0 || bias.Magic != 0 ||
                                      bias.Health != 1 || bias.Stamina != 1 || bias.Mana != 1 || bias.Endurance != 0;

                        if (hasBias)
                        {
                            ImGui.Spacing();
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.9f, 1), "Archetype Bonuses:");

                            if (bias.Strength != 0) RenderBiasLine("Strength", bias.Strength);
                            if (bias.Defense != 0) RenderBiasLine("Defense", bias.Defense);
                            if (bias.Speed != 0) RenderBiasLine("Speed", bias.Speed);
                            if (bias.Magic != 0) RenderBiasLine("Magic", bias.Magic);
                            if (bias.Endurance != 0) RenderBiasLine("Endurance", bias.Endurance);
                            if (bias.Health != 1) RenderBiasLine("Health", bias.Health - 1);
                            if (bias.Stamina != 1) RenderBiasLine("Stamina", bias.Stamina - 1);
                            if (bias.Mana != 1) RenderBiasLine("Mana", bias.Mana - 1);
                        }
                    }
                }
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No avatar created");
            ImGui.TextWrapped("Enter a world to select archetype");
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Equipped Items (read-only view of current loadout)
        RenderEquippedItems(viewModel);

        ImGui.Spacing();
        ImGui.Separator();

        // Collected Affinities (captured from characters)
        if (viewModel.PlayerAvatar?.Affinities != null && viewModel.PlayerAvatar.Affinities.Length > 0)
        {
            ImGui.TextColored(new Vector4(0.8f, 0.5f, 1, 1), "Collected Affinities:");
            ImGui.Spacing();

            // Active affinity indicator
            var activeAffinity = viewModel.PlayerAvatar.ActiveAffinityRef;
            if (!string.IsNullOrEmpty(activeAffinity))
            {
                var activeAffinityDef = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == activeAffinity);
                var activeName = activeAffinityDef?.DisplayName ?? activeAffinity;
                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Active: {activeName}");
                ImGui.Spacing();
            }

            foreach (var affinity in viewModel.PlayerAvatar.Affinities)
            {
                var affinityDef = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == affinity.AffinityRef);
                var name = affinityDef?.DisplayName ?? affinity.AffinityRef;
                var isActive = affinity.AffinityRef == activeAffinity;

                ImGui.Indent();

                var treeNodeOpen = ImGui.TreeNode($"{(isActive ? "* " : "")}{name}##aff_{affinity.AffinityRef}");

                if (treeNodeOpen)
                {
                    // Source character
                    if (!string.IsNullOrEmpty(affinity.CapturedFromCharacterRef))
                    {
                        var sourceChar = viewModel.CurrentWorld?.Gameplay?.Characters?.FirstOrDefault(c => c.RefName == affinity.CapturedFromCharacterRef);
                        var sourceName = sourceChar?.DisplayName ?? affinity.CapturedFromCharacterRef;
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"From: {sourceName}");
                    }

                    // Acquired date
                    if (!string.IsNullOrEmpty(affinity.AcquiredDate))
                    {
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"Acquired: {affinity.AcquiredDate}");
                    }

                    // Affinity description and matchups
                    if (affinityDef != null)
                    {
                        if (!string.IsNullOrEmpty(affinityDef.Description))
                        {
                            ImGui.TextWrapped(affinityDef.Description);
                        }

                        if (affinityDef.Matchup != null && affinityDef.Matchup.Length > 0)
                        {
                            ImGui.Spacing();
                            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Matchups:");
                            ImGui.Indent(10 * UIConstants.DpiScale);
                            foreach (var matchup in affinityDef.Matchup)
                            {
                                var targetAffinityDef = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == matchup.TargetAffinityRef);
                                var targetName = targetAffinityDef?.DisplayName ?? matchup.TargetAffinityRef;
                                var color = matchup.Multiplier > 1.0
                                    ? new Vector4(0.2f, 1, 0.2f, 1)  // Green for strong
                                    : new Vector4(1, 0.5f, 0.2f, 1); // Orange for weak
                                ImGui.TextColored(color, $"vs {targetName}: {matchup.Multiplier}x");
                            }
                            ImGui.Unindent(10 * UIConstants.DpiScale);
                        }
                    }

                    ImGui.TreePop();
                }

                ImGui.Unindent();
            }

            ImGui.Spacing();
            ImGui.Separator();
        }

        // Party/Companions
        if (viewModel.PlayerAvatar?.Party?.Member != null && viewModel.PlayerAvatar.Party.Member.Length > 0)
        {
            ImGui.TextColored(new Vector4(1, 0.8f, 0.5f, 1), "Party Members:");
            ImGui.Spacing();

            foreach (var member in viewModel.PlayerAvatar.Party.Member)
            {
                var memberChar = viewModel.CurrentWorld?.Gameplay?.Characters?.FirstOrDefault(c => c.RefName == member.CharacterRef);
                var memberName = memberChar?.DisplayName ?? member.CharacterRef;

                ImGui.Indent();

                var treeNodeOpen = ImGui.TreeNode($"{memberName}##party_{member.CharacterRef}");

                if (treeNodeOpen)
                {
                    if (memberChar != null && !string.IsNullOrEmpty(memberChar.Description))
                    {
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), memberChar.Description);
                    }

                    // Show member's affinity if available
                    if (memberChar?.AffinityRef != null)
                    {
                        var memberAffinity = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == memberChar.AffinityRef);
                        var affinityName = memberAffinity?.DisplayName ?? memberChar.AffinityRef;
                        ImGui.TextColored(new Vector4(0.8f, 0.5f, 1, 1), $"Affinity: {affinityName}");
                    }

                    ImGui.TreePop();
                }

                ImGui.Unindent();
            }

            ImGui.Spacing();
            ImGui.Separator();
        }

        // Faction Reputation
        RenderFactionSection(viewModel);

        // Lifetime Statistics
        if (viewModel.PlayerAvatar != null)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1, 1), "Lifetime Statistics:");
            ImGui.Spacing();

            var avatar = viewModel.PlayerAvatar;

            if (ImGui.BeginTable("LifetimeStatsTable", 2, ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

                // Play time
                var playTimeHours = avatar.PlayTimeHours;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Play Time:");
                ImGui.TableNextColumn();
                ImGui.Text(playTimeHours >= 1 ? $"{playTimeHours:F1} hours" : $"{playTimeHours * 60:F0} minutes");

                // Distance traveled
                var distance = avatar.DistanceTraveled;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Distance:");
                ImGui.TableNextColumn();
                ImGui.Text(distance >= 1000 ? $"{distance / 1000:F2} km" : $"{distance:F0} m");

                // Blocks
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Blocks Placed:");
                ImGui.TableNextColumn();
                ImGui.Text($"{avatar.BlocksPlaced:N0}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Blocks Destroyed:");
                ImGui.TableNextColumn();
                ImGui.Text($"{avatar.BlocksDestroyed:N0}");

                ImGui.EndTable();
            }
        }

        ImGui.EndChild();
    }

    private void RenderStatLine(string label, string value)
    {
        var scale = UIConstants.DpiScale;
        ImGui.Text(label);
        ImGui.SameLine(120 * scale);
        ImGui.Text(value);
    }

    private void RenderBiasLine(string statName, float modifier)
    {
        var color = modifier > 0
            ? new Vector4(0.2f, 1, 0.2f, 1)   // Green for positive
            : new Vector4(1, 0.4f, 0.4f, 1);  // Red for negative
        var sign = modifier > 0 ? "+" : "";
        ImGui.TextColored(color, $"  {statName}: {sign}{modifier:P0}");
    }

    private void RenderEquippedItems(SagaMainViewModel viewModel)
    {
        if (viewModel.PlayerAvatar?.CombatProfile == null || viewModel.CurrentWorld == null)
            return;

        // Get loadout slots from world definition
        var loadoutSlots = viewModel.CurrentWorld.Gameplay?.LoadoutSlots;
        if (loadoutSlots == null || loadoutSlots.Length == 0)
            return;

        ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1), "Equipped Items:");
        ImGui.Spacing();

        var scale = UIConstants.DpiScale;
        var hasEquippedItems = false;

        foreach (var slot in loadoutSlots)
        {
            var slotName = slot.RefName;
            var displayName = slot.DisplayName ?? slotName;
            var equippedRef = viewModel.GetEquippedItemInSlot(slotName);

            if (!string.IsNullOrEmpty(equippedRef))
            {
                hasEquippedItems = true;

                // Get equipment details
                var equipment = viewModel.CurrentWorld.GetEquipmentByRefName(equippedRef);
                var itemName = equipment?.DisplayName ?? equippedRef;

                ImGui.Text($"{displayName}:");
                ImGui.SameLine(120 * scale);
                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), itemName);
            }
        }

        if (!hasEquippedItems)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "No items equipped");
            ImGui.TextWrapped("Use Inventory (I) to equip items");
        }
    }

    private void RenderFactionSection(SagaMainViewModel viewModel)
    {
        var factions = viewModel.CurrentWorld?.Gameplay?.Factions;
        if (factions == null || factions.Length == 0)
            return;

        ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), "Faction Reputation:");
        ImGui.Spacing();

        // Render each faction as a collapsible entry
        foreach (var faction in factions)
        {
            RenderFactionEntry(faction, viewModel);
        }

        ImGui.Spacing();
        ImGui.Separator();
    }

    private void RenderFactionEntry(Ambient.Domain.Faction faction, SagaMainViewModel viewModel)
    {
        // Get player's reputation with this faction
        var currentRep = faction.StartingReputation; // TODO: Get actual player reputation from avatar
        var repLevel = GetReputationLevel(currentRep);
        var (levelColor, levelName) = GetReputationDisplay(repLevel);

        // Collapsible header with faction name and current standing
        var headerText = $"{faction.DisplayName} - {levelName}";

        ImGui.PushStyleColor(ImGuiCol.Text, levelColor);
        var isOpen = ImGui.TreeNode($"{headerText}##{faction.RefName}");
        ImGui.PopStyleColor();

        if (isOpen)
        {
            ImGui.Indent();

            // Category
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), $"[{faction.Category}]");

            // Description
            if (!string.IsNullOrEmpty(faction.Description))
            {
                ImGui.TextWrapped(faction.Description);
            }

            // Reputation progress bar
            var (minRep, maxRep) = GetReputationRange(repLevel);
            var progress = maxRep > minRep ? (float)(currentRep - minRep) / (maxRep - minRep) : 0.5f;
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, levelColor);
            ImGui.ProgressBar(progress, new Vector2(ImGuiSizes.Fill, ImGui.GetFrameHeight() * 0.8f), $"{currentRep:N0}");
            ImGui.PopStyleColor();

            // Relationships
            if (faction.Relationships?.Length > 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Relationships:");
                foreach (var rel in faction.Relationships.Take(3))
                {
                    var relColor = rel.RelationshipType == Ambient.Domain.FactionRelationshipRelationshipType.Allied
                        ? new Vector4(0.5f, 1, 0.5f, 1)
                        : new Vector4(1, 0.5f, 0.5f, 1);
                    var relFaction = viewModel.CurrentWorld?.Gameplay?.Factions?.FirstOrDefault(f => f.RefName == rel.FactionRef);
                    var relFactionName = relFaction?.DisplayName ?? rel.FactionRef;
                    ImGui.TextColored(relColor, $"  {relFactionName} ({rel.RelationshipType})");
                }
            }

            // Rewards summary
            if (faction.ReputationRewards?.Length > 0)
            {
                var availableRewards = faction.ReputationRewards.Count(r => IsRewardUnlocked(r, repLevel));
                var lockedRewards = faction.ReputationRewards.Length - availableRewards;

                if (availableRewards > 0)
                {
                    ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Unlocked: {availableRewards}");
                    ImGui.SameLine();
                }
                if (lockedRewards > 0)
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), $"Locked: {lockedRewards}");
                }
            }

            ImGui.Unindent();
            ImGui.TreePop();
        }
    }

    private static Ambient.Domain.ReputationLevel GetReputationLevel(int reputation)
    {
        return reputation switch
        {
            < -21000 => Ambient.Domain.ReputationLevel.Hated,
            < -6000 => Ambient.Domain.ReputationLevel.Hostile,
            < -3000 => Ambient.Domain.ReputationLevel.Unfriendly,
            < 3000 => Ambient.Domain.ReputationLevel.Neutral,
            < 9000 => Ambient.Domain.ReputationLevel.Friendly,
            < 21000 => Ambient.Domain.ReputationLevel.Honored,
            < 42000 => Ambient.Domain.ReputationLevel.Revered,
            _ => Ambient.Domain.ReputationLevel.Exalted
        };
    }

    private static (int min, int max) GetReputationRange(Ambient.Domain.ReputationLevel level)
    {
        return level switch
        {
            Ambient.Domain.ReputationLevel.Hated => (-42000, -21000),
            Ambient.Domain.ReputationLevel.Hostile => (-21000, -6000),
            Ambient.Domain.ReputationLevel.Unfriendly => (-6000, -3000),
            Ambient.Domain.ReputationLevel.Neutral => (-3000, 3000),
            Ambient.Domain.ReputationLevel.Friendly => (3000, 9000),
            Ambient.Domain.ReputationLevel.Honored => (9000, 21000),
            Ambient.Domain.ReputationLevel.Revered => (21000, 42000),
            Ambient.Domain.ReputationLevel.Exalted => (42000, 84000),
            _ => (0, 1)
        };
    }

    private static (Vector4 color, string name) GetReputationDisplay(Ambient.Domain.ReputationLevel level)
    {
        return level switch
        {
            Ambient.Domain.ReputationLevel.Hated => (new Vector4(0.8f, 0, 0, 1), "Hated"),
            Ambient.Domain.ReputationLevel.Hostile => (new Vector4(1, 0.3f, 0.3f, 1), "Hostile"),
            Ambient.Domain.ReputationLevel.Unfriendly => (new Vector4(1, 0.5f, 0.3f, 1), "Unfriendly"),
            Ambient.Domain.ReputationLevel.Neutral => (new Vector4(1, 1, 0.5f, 1), "Neutral"),
            Ambient.Domain.ReputationLevel.Friendly => (new Vector4(0.5f, 1, 0.5f, 1), "Friendly"),
            Ambient.Domain.ReputationLevel.Honored => (new Vector4(0.3f, 0.8f, 0.3f, 1), "Honored"),
            Ambient.Domain.ReputationLevel.Revered => (new Vector4(0.3f, 0.6f, 1, 1), "Revered"),
            Ambient.Domain.ReputationLevel.Exalted => (new Vector4(0.8f, 0.5f, 1, 1), "Exalted"),
            _ => (new Vector4(0.5f, 0.5f, 0.5f, 1), "Unknown")
        };
    }

    private static bool IsRewardUnlocked(Ambient.Domain.ReputationReward reward, Ambient.Domain.ReputationLevel currentLevel)
    {
        var levelOrder = new[]
        {
            Ambient.Domain.ReputationLevel.Hated,
            Ambient.Domain.ReputationLevel.Hostile,
            Ambient.Domain.ReputationLevel.Unfriendly,
            Ambient.Domain.ReputationLevel.Neutral,
            Ambient.Domain.ReputationLevel.Friendly,
            Ambient.Domain.ReputationLevel.Honored,
            Ambient.Domain.ReputationLevel.Revered,
            Ambient.Domain.ReputationLevel.Exalted
        };

        var currentIndex = Array.IndexOf(levelOrder, currentLevel);
        var requiredIndex = Array.IndexOf(levelOrder, reward.RequiredLevel);

        return currentIndex >= requiredIndex;
    }
}
