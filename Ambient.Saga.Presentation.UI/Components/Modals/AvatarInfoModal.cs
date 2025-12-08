using Ambient.Domain;
using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.Presentation.UI.Components.Utilities;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Modals;

/// <summary>
/// Modal showing avatar stats, inventory, career statistics, party, and summons
/// </summary>
public class AvatarInfoModal
{
    private int _selectedTab = 0;

    public void Render(MainViewModel viewModel, ref bool isOpen)
    {
        if (!isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(650, 700), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Avatar Info", ref isOpen))
        {
            if (viewModel.PlayerAvatar != null)
            {
                var avatar = viewModel.PlayerAvatar;

                // Header
                ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), avatar.DisplayName ?? "Avatar");
                if (!string.IsNullOrEmpty(avatar.ArchetypeRef))
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"[{avatar.ArchetypeRef}]");
                }
                if (!string.IsNullOrEmpty(avatar.Description))
                {
                    ImGui.TextWrapped(avatar.Description);
                }

                ImGui.Separator();

                // Tab bar
                if (ImGui.BeginTabBar("AvatarInfoTabs"))
                {
                    if (ImGui.BeginTabItem("Stats"))
                    {
                        RenderStatsTab(avatar, viewModel);
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Inventory"))
                    {
                        RenderInventoryTab(avatar, viewModel);
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Career"))
                    {
                        RenderCareerTab(avatar);
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Party"))
                    {
                        RenderPartyTab(avatar, viewModel);
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Summons"))
                    {
                        RenderSummonsTab(avatar, viewModel);
                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No avatar created yet.");
                ImGui.Text("Click 'Select Archetype' to create your character.");
            }

            ImGui.End();
        }
    }

    private void RenderStatsTab(AvatarBase avatar, MainViewModel viewModel)
    {
        ImGui.BeginChild("StatsScroll", new Vector2(0, 0), ImGuiChildFlags.None);

        if (avatar.Stats != null)
        {
            var currencyName = viewModel.CurrentWorld?.WorldConfiguration?.CurrencyName ?? "Credits";

            // Vitals
            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Vitals:");
            RenderStatBar("Health", avatar.Stats.Health, new Vector4(1, 0.3f, 0.3f, 1));
            RenderStatBar("Stamina", avatar.Stats.Stamina, new Vector4(0.3f, 1, 0.3f, 1));
            RenderStatBar("Mana", avatar.Stats.Mana, new Vector4(0.3f, 0.5f, 1, 1));

            ImGui.Spacing();
            ImGui.Separator();

            // Combat Stats
            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Combat:");
            RenderStatBar("Strength", avatar.Stats.Strength, new Vector4(1, 0.5f, 0.2f, 1));
            RenderStatBar("Defense", avatar.Stats.Defense, new Vector4(0.6f, 0.6f, 0.6f, 1));
            RenderStatBar("Speed", avatar.Stats.Speed, new Vector4(1, 1, 0.3f, 1));
            RenderStatBar("Magic", avatar.Stats.Magic, new Vector4(0.7f, 0.3f, 1, 1));

            ImGui.Spacing();
            ImGui.Separator();

            // Survival Stats
            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Survival:");
            RenderStatBar("Temperature", avatar.Stats.Temperature, new Vector4(1, 0.6f, 0.2f, 1));
            RenderStatBar("Hunger", avatar.Stats.Hunger, new Vector4(0.8f, 0.6f, 0.3f, 1));
            RenderStatBar("Thirst", avatar.Stats.Thirst, new Vector4(0.3f, 0.7f, 1, 1));
            RenderStatBar("Insulation", avatar.Stats.Insulation, new Vector4(0.5f, 0.8f, 0.8f, 1));

            ImGui.Spacing();
            ImGui.Separator();

            // Progression
            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Progression:");
            ImGui.Text($"Level: {avatar.Stats.Level}");
            ImGui.Text($"Experience: {avatar.Stats.Experience:N0}");
            ImGui.Text($"{currencyName}: {avatar.Stats.Credits:N0}");

            // Current Affinity
            if (!string.IsNullOrEmpty(avatar.ActiveAffinityRef))
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), $"Active Affinity: {avatar.ActiveAffinityRef}");
            }
        }

        ImGui.EndChild();
    }

    private void RenderInventoryTab(AvatarBase avatar, MainViewModel viewModel)
    {
        ImGui.BeginChild("InventoryScroll", new Vector2(0, 0), ImGuiChildFlags.None);

        if (avatar.Capabilities == null)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No inventory data");
            ImGui.EndChild();
            return;
        }

        var caps = avatar.Capabilities;

        // Equipment
        if (caps.Equipment?.Length > 0)
        {
            if (ImGui.CollapsingHeader($"Equipment ({caps.Equipment.Length})", ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var equip in caps.Equipment)
                {
                    var item = viewModel.CurrentWorld?.TryGetEquipmentByRefName(equip.EquipmentRef);
                    var name = item?.DisplayName ?? equip.EquipmentRef;
                    var condColor = equip.Condition > 0.5 ? new Vector4(0.5f, 1, 0.5f, 1) : new Vector4(1, 0.5f, 0.5f, 1);

                    ImGui.BulletText(name);
                    ImGui.SameLine();
                    ImGui.TextColored(condColor, $"({equip.Condition:P0})");

                    if (item?.Effects != null && ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGuiHelpers.RenderCharacterEffects(item.Effects);
                        ImGui.EndTooltip();
                    }
                }
            }
        }

        // Consumables
        if (caps.Consumables?.Length > 0)
        {
            if (ImGui.CollapsingHeader($"Consumables ({caps.Consumables.Length})"))
            {
                foreach (var consumable in caps.Consumables)
                {
                    var item = viewModel.CurrentWorld?.TryGetConsumableByRefName(consumable.ConsumableRef);
                    var name = item?.DisplayName ?? consumable.ConsumableRef;
                    ImGui.BulletText($"{name} x{consumable.Quantity}");
                }
            }
        }

        // Spells
        if (caps.Spells?.Length > 0)
        {
            if (ImGui.CollapsingHeader($"Spells ({caps.Spells.Length})"))
            {
                foreach (var spell in caps.Spells)
                {
                    var item = viewModel.CurrentWorld?.TryGetSpellByRefName(spell.SpellRef);
                    var name = item?.DisplayName ?? spell.SpellRef;
                    var condColor = spell.Condition > 0.5 ? new Vector4(0.5f, 1, 0.5f, 1) : new Vector4(1, 0.5f, 0.5f, 1);

                    ImGui.BulletText(name);
                    ImGui.SameLine();
                    ImGui.TextColored(condColor, $"({spell.Condition:P0})");
                }
            }
        }

        // Tools
        if (caps.Tools?.Length > 0)
        {
            if (ImGui.CollapsingHeader($"Tools ({caps.Tools.Length})"))
            {
                foreach (var tool in caps.Tools)
                {
                    var item = viewModel.CurrentWorld?.TryGetToolByRefName(tool.ToolRef);
                    var name = item?.DisplayName ?? tool.ToolRef;
                    var condColor = tool.Condition > 0.5 ? new Vector4(0.5f, 1, 0.5f, 1) : new Vector4(1, 0.5f, 0.5f, 1);

                    ImGui.BulletText(name);
                    ImGui.SameLine();
                    ImGui.TextColored(condColor, $"({tool.Condition:P0})");

                    // Show current tool indicator
                    if (avatar.CurrentToolRef == tool.ToolRef)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), "[ACTIVE]");
                    }
                }
            }
        }

        // Building Materials
        if (caps.BuildingMaterials?.Length > 0)
        {
            if (ImGui.CollapsingHeader($"Building Materials ({caps.BuildingMaterials.Length})"))
            {
                foreach (var mat in caps.BuildingMaterials)
                {
                    var item = viewModel.CurrentWorld?.TryGetBuildingMaterialByRefName(mat.BuildingMaterialRef);
                    var name = item?.DisplayName ?? mat.BuildingMaterialRef;
                    ImGui.BulletText($"{name} x{mat.Quantity}");

                    // Show current material indicator
                    if (avatar.CurrentBuildingMaterialRef == mat.BuildingMaterialRef)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), "[ACTIVE]");
                    }
                }
            }
        }

        // Blocks
        if (caps.Blocks?.Length > 0)
        {
            if (ImGui.CollapsingHeader($"Blocks ({caps.Blocks.Length})"))
            {
                foreach (var block in caps.Blocks)
                {
                    ImGui.BulletText($"{block.BlockRef} x{block.Quantity}");
                }
            }
        }

        // Quest Tokens
        if (caps.QuestTokens?.Length > 0)
        {
            if (ImGui.CollapsingHeader($"Quest Tokens ({caps.QuestTokens.Length})", ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var token in caps.QuestTokens)
                {
                    var item = viewModel.CurrentWorld?.TryGetQuestTokenByRefName(token.QuestTokenRef);
                    var name = item?.DisplayName ?? token.QuestTokenRef;
                    ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), $"  {name}");
                }
            }
        }

        ImGui.EndChild();
    }

    private void RenderCareerTab(AvatarBase avatar)
    {
        ImGui.BeginChild("CareerScroll", new Vector2(0, 0), ImGuiChildFlags.None);

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Career Statistics:");
        ImGui.Spacing();

        // Time played
        ImGui.Text($"Play Time: {avatar.PlayTimeHours:F1} hours");

        ImGui.Spacing();
        ImGui.Separator();

        // Building stats
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Building:");
        ImGui.Text($"Blocks Placed: {avatar.BlocksPlaced:N0}");
        ImGui.Text($"Blocks Destroyed: {avatar.BlocksDestroyed:N0}");

        ImGui.Spacing();
        ImGui.Separator();

        // Exploration stats
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Exploration:");
        ImGui.Text($"Distance Traveled: {avatar.DistanceTraveled:N0} meters");

        ImGui.Spacing();
        ImGui.Separator();

        // Quest stats
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Quests:");
        var completedQuests = avatar.Quests?.Count(q => q.IsCompleted) ?? 0;
        var totalQuests = avatar.Quests?.Length ?? 0;
        ImGui.Text($"Quests Completed: {completedQuests} / {totalQuests}");

        // Achievement stats
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Achievements:");
        var unlockedAchievements = avatar.Achievements?.Length ?? 0;
        ImGui.Text($"Achievements Unlocked: {unlockedAchievements}");

        ImGui.EndChild();
    }

    private void RenderPartyTab(AvatarBase avatar, MainViewModel viewModel)
    {
        ImGui.BeginChild("PartyScroll", new Vector2(0, 0), ImGuiChildFlags.None);

        if (avatar.Party?.Member == null || avatar.Party.Member.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No party members");
            ImGui.Spacing();
            ImGui.TextWrapped("Party members can join through dialogue interactions.");
            ImGui.EndChild();
            return;
        }

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Party Members ({avatar.Party.Member.Length}):");
        ImGui.Spacing();

        foreach (var member in avatar.Party.Member)
        {
            var character = viewModel.CurrentWorld?.TryGetCharacterByRefName(member.CharacterRef);
            var name = character?.DisplayName ?? member.CharacterRef;

            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.2f, 0.15f, 0.4f));
            ImGui.BeginChild($"party_{member.CharacterRef}", new Vector2(0, 80), ImGuiChildFlags.Borders);

            ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), name);
            ImGui.Text($"Slot: {member.SlotRef}");
            if (!string.IsNullOrEmpty(member.JoinedDate))
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), $"Joined: {member.JoinedDate}");
            }

            if (character?.Stats != null)
            {
                ImGui.Text($"HP: {character.Stats.Health:P0} | STR: {character.Stats.Strength:F0} | DEF: {character.Stats.Defense:F0}");
            }

            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        // Party faction info
        if (!string.IsNullOrEmpty(avatar.Party.SlotFactionRef))
        {
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), $"Party Faction: {avatar.Party.SlotFactionRef}");
            ImGui.TextWrapped("Party slots unlock with faction reputation.");
        }

        ImGui.EndChild();
    }

    private void RenderSummonsTab(AvatarBase avatar, MainViewModel viewModel)
    {
        ImGui.BeginChild("SummonsScroll", new Vector2(0, 0), ImGuiChildFlags.None);

        if (avatar.Affinities == null || avatar.Affinities.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No captured summons");
            ImGui.Spacing();
            ImGui.TextWrapped("Summons can be captured during combat encounters.");
            ImGui.EndChild();
            return;
        }

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Summons ({avatar.Summons.Length}):");
        ImGui.Spacing();

        foreach (var summon in avatar.Affinities)
        {
            var character = viewModel.CurrentWorld?.TryGetCharacterByRefName(summon.CapturedFromCharacterRef);
            var affinity = viewModel.CurrentWorld?.TryGetCharacterAffinityByRefName(summon.AffinityRef);

            var name = character?.DisplayName ?? summon.CapturedFromCharacterRef ?? "Unknown";
            var affinityName = affinity?.DisplayName ?? summon.AffinityRef ?? "None";

            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.2f, 0.15f, 0.2f, 0.4f));
            ImGui.BeginChild($"summon_{summon.CapturedFromCharacterRef}", new Vector2(0, 70), ImGuiChildFlags.Borders);

            ImGui.TextColored(new Vector4(0.8f, 0.5f, 1, 1), name);
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), $"Affinity: {affinityName}");
            if (!string.IsNullOrEmpty(summon.CapturedDate))
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), $"Captured: {summon.CapturedDate}");
            }

            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        ImGui.EndChild();
    }
    private void RenderStatBar(string label, double value, Vector4 color)
    {
        ImGui.Text($"{label}:");
        ImGui.SameLine(110);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color);
        ImGui.ProgressBar((float)value, new Vector2(-1, 18), $"{value * 100:F0}%");
        ImGui.PopStyleColor();
    }
}
