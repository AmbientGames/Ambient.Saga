using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;
using Ambient.Saga.UI.Components.Utilities;
using Ambient.Saga.UI.Components.Modals;

namespace Ambient.Saga.UI.Components.Panels;

/// <summary>
/// Character panel showing the current state of the avatar.
/// Full-screen panel with three columns:
/// - Column 1: Position, Vitals (with progress bars), Archetype
/// - Column 2: Equipped Items, Affinities, Party Members
/// - Column 3: Faction Reputation, Lifetime Statistics
/// Accessible via C key.
/// Inventory content has moved to InventoryPanel (I key).
/// </summary>
public class CharacterPanel
{
    public void Render(SagaMainViewModel viewModel, ModalManager modalManager)
    {
        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "CHARACTER");
        ImGui.Separator();

        // Calculate column widths for 3-column layout
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var columnWidth = (availableWidth - spacing * 2) / 3;

        // Column 1: Position, Stats, Archetype
        ImGui.BeginChild("CharacterCol1", new Vector2(columnWidth, ImGuiSizes.Fill), ImGuiChildFlags.None);
        RenderColumn1_StatsAndArchetype(viewModel);
        ImGui.EndChild();

        ImGui.SameLine();

        // Column 2: Equipment, Affinities, Party
        ImGui.BeginChild("CharacterCol2", new Vector2(columnWidth, ImGuiSizes.Fill), ImGuiChildFlags.None);
        RenderColumn2_EquipmentAndCompanions(viewModel);
        ImGui.EndChild();

        ImGui.SameLine();

        // Column 3: Factions, Statistics
        ImGui.BeginChild("CharacterCol3", new Vector2(columnWidth, ImGuiSizes.Fill), ImGuiChildFlags.None);
        RenderColumn3_FactionsAndStats(viewModel);
        ImGui.EndChild();
    }

    private void RenderColumn1_StatsAndArchetype(SagaMainViewModel viewModel)
    {
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

        // Home Location (spawn point)
        if (viewModel.Avatar != null)
        {
            var home = viewModel.Avatar.HomeLocation;
            if (home.X != 0 || home.Y != 0)
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1), "Home:");
                ImGui.Text($"  Lat: {home.Y:F6}, Lon: {home.X:F6}");
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Vitals/Stats
        if (viewModel.Avatar != null && viewModel.Avatar.Stats != null)
        {
            var vitals = viewModel.Avatar.Stats;
            var currencyName = viewModel.CurrentWorld?.WorldConfiguration?.CurrencyName ?? "Credit";
            var pluralCurrency = vitals.Credits == 1 ? currencyName : currencyName + "s";

            // Progression (Level & Experience)
            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Progression:");
            RenderStatLine("Level:", $"{vitals.Level}");
            RenderStatLine("Experience:", $"{vitals.Experience:N0}");
            RenderStatLine($"{pluralCurrency}:", $"{vitals.Credits:N0}");

            ImGui.Spacing();
            ImGui.Separator();

            // Vitals with progress bars
            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Vitals:");
            RenderStatBar("Health", vitals.Health, new Vector4(1, 0.3f, 0.3f, 1));
            RenderStatBar("Stamina", vitals.Stamina, new Vector4(0.3f, 1, 0.3f, 1));
            RenderStatBar("Mana", vitals.Mana, new Vector4(0.3f, 0.5f, 1, 1));

            ImGui.Spacing();
            ImGui.Separator();

            // Combat Stats
            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Combat:");
            RenderStatBar("Strength", vitals.Strength / 100.0, new Vector4(1, 0.5f, 0.2f, 1));
            RenderStatBar("Defense", vitals.Defense / 100.0, new Vector4(0.6f, 0.6f, 0.6f, 1));
            RenderStatBar("Speed", vitals.Speed / 100.0, new Vector4(1, 1, 0.3f, 1));
            RenderStatBar("Magic", vitals.Magic / 100.0, new Vector4(0.7f, 0.3f, 1, 1));

            ImGui.Spacing();
            ImGui.Separator();

            // State
            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "State:");
            RenderTemperatureStat(vitals.Temperature);
            RenderStatBar("Endurance", vitals.Endurance / 100.0, new Vector4(0.5f, 0.8f, 0.8f, 1));

            // Invulnerability status
            if (viewModel.Avatar.IsInvulnerable)
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), "[INVULNERABLE]");
            }

            // Archetype info with bias
            if (!string.IsNullOrEmpty(viewModel.Avatar.ArchetypeRef))
            {
                var archetype = viewModel.CurrentWorld?.Gameplay?.AvatarArchetypes?
                    .FirstOrDefault(a => a.RefName == viewModel.Avatar.ArchetypeRef);

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

                    // Weight & Carry Capacity
                    var worldConfig = viewModel.CurrentWorld?.WorldConfiguration;
                    if (worldConfig != null)
                    {
                        var weightUnit = worldConfig.WeightUnitName ?? "kg";
                        var maxCarry = CarryWeightCalculator.GetMaxCarryWeight(archetype);
                        var currentCarry = CarryWeightCalculator.CalculateTotalWeight(
                            viewModel.Avatar.Capabilities, worldConfig);
                        var fraction = maxCarry > 0 ? currentCarry / maxCarry : 0f;

                        ImGui.Spacing();
                        RenderStatLine("Weight:", $"{archetype.Weight:N0} {weightUnit}");

                        var carryColor = fraction > 0.9f
                            ? new Vector4(1, 0.3f, 0.3f, 1)
                            : fraction > 0.7f
                                ? new Vector4(1, 0.8f, 0.3f, 1)
                                : new Vector4(0.5f, 0.8f, 1, 1);
                        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, carryColor);
                        ImGui.Text("Carry:");
                        ImGui.SameLine(100 * UIConstants.DpiScale);
                        ImGui.ProgressBar(fraction, new Vector2(ImGuiSizes.Fill, ImGui.GetFrameHeight()),
                            $"{currentCarry:N1} / {maxCarry:N1} {weightUnit}");
                        ImGui.PopStyleColor();
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
    }

    private void RenderColumn2_EquipmentAndCompanions(SagaMainViewModel viewModel)
    {
        // Current Selection (Tool & Material)
        RenderCurrentSelection(viewModel);

        ImGui.Spacing();
        ImGui.Separator();

        // Equipped Items (read-only view of current loadout)
        RenderEquippedItems(viewModel);

        ImGui.Spacing();
        ImGui.Separator();

        // Collected Affinities (captured from characters)
        if (viewModel.Avatar?.Affinities != null && viewModel.Avatar.Affinities.Length > 0)
        {
            ImGui.TextColored(new Vector4(0.8f, 0.5f, 1, 1), "Collected Affinities:");
            ImGui.Spacing();

            // Active affinity indicator
            var activeAffinity = viewModel.Avatar.ActiveAffinityRef;
            if (!string.IsNullOrEmpty(activeAffinity))
            {
                var activeAffinityDef = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == activeAffinity);
                var activeName = activeAffinityDef?.DisplayName ?? activeAffinity;
                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Active: {activeName}");
                ImGui.Spacing();
            }

            foreach (var affinity in viewModel.Avatar.Affinities)
            {
                var affinityDef = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == affinity.AffinityRef);
                var name = affinityDef?.DisplayName ?? affinity.AffinityRef;
                var isActive = affinity.AffinityRef == activeAffinity;

                var treeNodeOpen = ImGui.TreeNode($"{(isActive ? "* " : "")}{name}##aff_{affinity.AffinityRef}");

                if (treeNodeOpen)
                {
                    ImGui.Indent();

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
                            foreach (var matchup in affinityDef.Matchup)
                            {
                                var targetAffinityDef = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == matchup.TargetAffinityRef);
                                var targetName = targetAffinityDef?.DisplayName ?? matchup.TargetAffinityRef;
                                var color = matchup.Multiplier > 1.0
                                    ? new Vector4(0.2f, 1, 0.2f, 1)  // Green for strong
                                    : new Vector4(1, 0.5f, 0.2f, 1); // Orange for weak
                                ImGui.TextColored(color, $"  vs {targetName}: {matchup.Multiplier}x");
                            }
                        }
                    }

                    ImGui.Unindent();
                    ImGui.TreePop();
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
        }

        // Party/Companions
        if (viewModel.Avatar?.Party?.Member != null && viewModel.Avatar.Party.Member.Length > 0)
        {
            ImGui.TextColored(new Vector4(1, 0.8f, 0.5f, 1), "Party Members:");
            ImGui.Spacing();

            foreach (var member in viewModel.Avatar.Party.Member)
            {
                var memberChar = viewModel.CurrentWorld?.Gameplay?.Characters?.FirstOrDefault(c => c.RefName == member.CharacterRef);
                var memberName = memberChar?.DisplayName ?? member.CharacterRef;

                var treeNodeOpen = ImGui.TreeNode($"{memberName}##party_{member.CharacterRef}");

                if (treeNodeOpen)
                {
                    ImGui.Indent();

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

                    // Show member stats if available
                    if (memberChar?.Stats != null)
                    {
                        ImGui.Text($"HP: {memberChar.Stats.Health:P0} | STR: {memberChar.Stats.Strength:F0} | DEF: {memberChar.Stats.Defense:F0}");
                    }

                    ImGui.Unindent();
                    ImGui.TreePop();
                }
            }

            // Party faction info
            if (!string.IsNullOrEmpty(viewModel.Avatar.Party.SlotFactionRef))
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), $"Party Faction: {viewModel.Avatar.Party.SlotFactionRef}");
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0.8f, 0.5f, 1), "Party Members:");
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No party members");
            ImGui.TextWrapped("Party members can join through dialogue interactions.");
        }
    }

    private void RenderColumn3_FactionsAndStats(SagaMainViewModel viewModel)
    {
        // Faction Reputation
        RenderFactionSection(viewModel);

        // Lifetime Statistics
        if (viewModel.Avatar != null)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1, 1), "Lifetime Statistics:");
            ImGui.Spacing();

            var avatar = viewModel.Avatar;

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

                // Quest stats
                var completedQuests = avatar.Quests?.Count(q => q.IsCompleted) ?? 0;
                var totalQuests = avatar.Quests?.Length ?? 0;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Quests:");
                ImGui.TableNextColumn();
                ImGui.Text($"{completedQuests} / {totalQuests} completed");

                // Achievement stats
                var unlockedAchievements = avatar.Achievements?.Length ?? 0;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Achievements:");
                ImGui.TableNextColumn();
                ImGui.Text($"{unlockedAchievements} unlocked");

                ImGui.EndTable();
            }
        }
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

    private void RenderStatBar(string label, double value, Vector4 color)
    {
        var scale = UIConstants.DpiScale;
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{label}:");
        ImGui.SameLine(100 * scale);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color);
        ImGui.ProgressBar((float)value, new Vector2(ImGuiSizes.Fill, ImGui.GetFrameHeight()), $"{value * 100:F0}%");
        ImGui.PopStyleColor();
    }

    // Temperature thresholds (body temp in Celsius - 37 is normal)
    private const float NormalTemperature = 37f;
    private const float ColdThreshold = 35f;   // Hypothermia warning
    private const float HotThreshold = 39f;    // Hyperthermia warning
    private const float CriticalColdThreshold = 32f;  // Severe hypothermia
    private const float CriticalHotThreshold = 42f;   // Severe hyperthermia

    private void RenderTemperatureStat(float temperature)
    {
        var scale = UIConstants.DpiScale;
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Temperature:");
        ImGui.SameLine(100 * scale);

        // Determine status and color based on deviation from normal
        string statusText;
        Vector4 barColor;
        Vector4 textColor;

        if (temperature < CriticalColdThreshold)
        {
            statusText = "CRITICAL";
            barColor = new Vector4(0.2f, 0.4f, 1f, 1f);      // Deep blue
            textColor = new Vector4(0.4f, 0.6f, 1f, 1f);
        }
        else if (temperature < ColdThreshold)
        {
            statusText = "Cold";
            barColor = new Vector4(0.4f, 0.7f, 1f, 1f);      // Light blue
            textColor = new Vector4(0.5f, 0.8f, 1f, 1f);
        }
        else if (temperature > CriticalHotThreshold)
        {
            statusText = "CRITICAL";
            barColor = new Vector4(1f, 0.2f, 0.2f, 1f);      // Deep red
            textColor = new Vector4(1f, 0.4f, 0.4f, 1f);
        }
        else if (temperature > HotThreshold)
        {
            statusText = "Hot";
            barColor = new Vector4(1f, 0.5f, 0.2f, 1f);      // Orange
            textColor = new Vector4(1f, 0.6f, 0.3f, 1f);
        }
        else
        {
            statusText = "Normal";
            barColor = new Vector4(0.3f, 0.8f, 0.3f, 1f);    // Green
            textColor = new Vector4(0.5f, 0.9f, 0.5f, 1f);
        }

        // Calculate progress bar value: deviation from normal
        // Bar shows distance from thresholds: 0 = at threshold, 1 = at normal
        // Invert so that normal = full bar, threshold = empty bar
        float progress;
        if (temperature < NormalTemperature)
        {
            // Cold side: CriticalCold (0%) -> Normal (100%)
            progress = Math.Clamp((temperature - CriticalColdThreshold) / (NormalTemperature - CriticalColdThreshold), 0f, 1f);
        }
        else
        {
            // Hot side: Normal (100%) -> CriticalHot (0%)
            progress = Math.Clamp(1f - (temperature - NormalTemperature) / (CriticalHotThreshold - NormalTemperature), 0f, 1f);
        }

        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, barColor);
        ImGui.ProgressBar(progress, new Vector2(ImGuiSizes.Fill, ImGui.GetFrameHeight()), $"{temperature:F1}°C ({statusText})");
        ImGui.PopStyleColor();
    }

    private void RenderEquippedItems(SagaMainViewModel viewModel)
    {
        if (viewModel.Avatar?.CombatProfile == null || viewModel.CurrentWorld == null)
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

    private void RenderCurrentSelection(SagaMainViewModel viewModel)
    {
        if (viewModel.Avatar == null || viewModel.CurrentWorld == null)
            return;

        var hasSelection = false;
        var scale = UIConstants.DpiScale;

        // Current Tool (only show if lookup succeeds)
        var currentToolRef = viewModel.Avatar.CurrentToolRef;
        if (!string.IsNullOrEmpty(currentToolRef))
        {
            var tool = viewModel.CurrentWorld.TryGetToolByRefName(currentToolRef);
            if (tool != null)
            {
                if (!hasSelection)
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.9f, 0.5f, 1), "Current Selection:");
                    ImGui.Spacing();
                    hasSelection = true;
                }

                ImGui.Text("Tool:");
                ImGui.SameLine(120 * scale);
                ImGui.TextColored(new Vector4(0.5f, 1, 0.8f, 1), tool.DisplayName ?? tool.RefName);
            }
        }

        // Only show header if nothing selected
        if (!hasSelection)
        {
            ImGui.TextColored(new Vector4(0.8f, 0.9f, 0.5f, 1), "Current Selection:");
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No tool or material selected");
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
        // Get avatar's reputation with this faction
        var currentRep = faction.StartingReputation; // TODO: Get actual avatar reputation
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
