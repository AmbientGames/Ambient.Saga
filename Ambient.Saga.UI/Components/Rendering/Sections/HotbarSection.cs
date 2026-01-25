using Ambient.Domain.Contracts;
using Ambient.Domain.Hotbar;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering.Sections;

/// <summary>
/// Bottom-center HUD section displaying the hotbar (slots 1-9).
/// Shows item names with number key badges. Selected slot is highlighted.
/// </summary>
public class HotbarSection : IHudSection
{
    // Slot styling
    private const float SlotWidth = 70f;
    private const float SlotHeight = 40f;
    private const float SlotSpacing = 4f;
    private const float SlotRounding = 4f;
    private const float KeyBadgeSize = 16f;

    // Colors
    private static readonly Vector4 SlotBgColor = new(0.15f, 0.15f, 0.2f, 0.9f);
    private static readonly Vector4 SlotBgSelectedColor = new(0.25f, 0.35f, 0.25f, 0.95f);
    private static readonly Vector4 SlotBgHoverColor = new(0.2f, 0.2f, 0.28f, 0.95f);
    private static readonly Vector4 SlotBorderColor = new(0.4f, 0.4f, 0.45f, 0.8f);
    private static readonly Vector4 SlotBorderSelectedColor = new(0.4f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 KeyBadgeBgColor = new(0.3f, 0.3f, 0.35f, 0.9f);
    private static readonly Vector4 KeyBadgeTextColor = new(0.9f, 0.9f, 0.6f, 1f);
    private static readonly Vector4 ItemTextColor = new(0.85f, 0.85f, 0.85f, 1f);
    private static readonly Vector4 EmptySlotTextColor = new(0.4f, 0.4f, 0.4f, 0.6f);

    // Item type colors for visual distinction
    private static readonly Dictionary<HotbarItemType, Vector4> ItemTypeColors = new()
    {
        { HotbarItemType.Empty, new Vector4(0.4f, 0.4f, 0.4f, 0.6f) },
        { HotbarItemType.Tool, new Vector4(0.7f, 0.7f, 0.9f, 1f) },
        { HotbarItemType.Block, new Vector4(0.6f, 0.5f, 0.4f, 1f) },
        { HotbarItemType.BuildingMaterial, new Vector4(0.8f, 0.6f, 0.4f, 1f) },
        { HotbarItemType.Consumable, new Vector4(0.4f, 0.9f, 0.5f, 1f) },
        { HotbarItemType.Spell, new Vector4(0.6f, 0.4f, 0.9f, 1f) },
        { HotbarItemType.Equipment, new Vector4(0.5f, 0.7f, 1f, 1f) }
    };

    public HudRegion Region => HudRegion.BottomCenter;
    public int Priority => 0;

    /// <summary>
    /// Event raised when a hotbar slot is activated (clicked or key pressed).
    /// Parameter is the slot index (0-8).
    /// </summary>
    public event Action<int>? SlotActivated;

    public void Render(HudContext context)
    {
        var avatar = context.ViewModel.PlayerAvatar;
        if (avatar == null) return;

        var hotbar = avatar.Hotbar;
        var activeSlot = avatar.ActiveHotbarSlot;

        var drawList = ImGui.GetWindowDrawList();
        var startPos = ImGui.GetCursorScreenPos();
        var style = ImGui.GetStyle();

        // Calculate total hotbar width and center it
        var totalWidth = (SlotWidth * 9) + (SlotSpacing * 8);
        var availableWidth = context.CenterRegionWidth;
        var offsetX = (availableWidth - totalWidth) / 2f;

        var currentX = startPos.X + offsetX;
        var slotY = startPos.Y + (context.HudHeight - SlotHeight - style.WindowPadding.Y * 2) / 2f;

        // Render each slot
        for (int i = 0; i < 9; i++)
        {
            var slot = hotbar[i];
            var isSelected = i == activeSlot;
            var slotPos = new Vector2(currentX, slotY);

            RenderSlot(context, drawList, slotPos, i, slot, isSelected);

            currentX += SlotWidth + SlotSpacing;
        }

        // Reserve space so ImGui layout works
        ImGui.Dummy(new Vector2(totalWidth, SlotHeight));
    }

    private void RenderSlot(HudContext context, ImDrawListPtr drawList, Vector2 pos, int index, HotbarSlot slot, bool isSelected)
    {
        var slotRect = new Vector2(pos.X + SlotWidth, pos.Y + SlotHeight);

        // Check if mouse is hovering this slot
        var mousePos = ImGui.GetMousePos();
        var isHovered = mousePos.X >= pos.X && mousePos.X <= slotRect.X &&
                        mousePos.Y >= pos.Y && mousePos.Y <= slotRect.Y;

        // Background
        var bgColor = isSelected ? SlotBgSelectedColor : (isHovered ? SlotBgHoverColor : SlotBgColor);
        drawList.AddRectFilled(pos, slotRect, ImGui.ColorConvertFloat4ToU32(bgColor), SlotRounding);

        // Border
        var borderColor = isSelected ? SlotBorderSelectedColor : SlotBorderColor;
        drawList.AddRect(pos, slotRect, ImGui.ColorConvertFloat4ToU32(borderColor), SlotRounding, ImDrawFlags.None, isSelected ? 2f : 1f);

        // Key number badge (top-left corner)
        var keyText = (index + 1).ToString();
        var keyBadgePos = new Vector2(pos.X + 2, pos.Y + 2);
        var keyBadgeRect = new Vector2(keyBadgePos.X + KeyBadgeSize, keyBadgePos.Y + KeyBadgeSize);
        drawList.AddRectFilled(keyBadgePos, keyBadgeRect, ImGui.ColorConvertFloat4ToU32(KeyBadgeBgColor), 3f);

        var keyTextSize = ImGui.CalcTextSize(keyText);
        var keyTextPos = new Vector2(
            keyBadgePos.X + (KeyBadgeSize - keyTextSize.X) / 2,
            keyBadgePos.Y + (KeyBadgeSize - keyTextSize.Y) / 2);
        drawList.AddText(keyTextPos, ImGui.ColorConvertFloat4ToU32(KeyBadgeTextColor), keyText);

        // Item content
        if (!slot.IsEmpty)
        {
            var displayName = GetItemDisplayName(context, slot);
            var textColor = ItemTypeColors.GetValueOrDefault(slot.ItemType, ItemTextColor);

            // Truncate text if too long
            var maxTextWidth = SlotWidth - 8;
            var truncatedName = TruncateText(displayName, maxTextWidth);

            var textSize = ImGui.CalcTextSize(truncatedName);
            var textPos = new Vector2(
                pos.X + (SlotWidth - textSize.X) / 2,
                pos.Y + KeyBadgeSize + 4 + (SlotHeight - KeyBadgeSize - 4 - textSize.Y) / 2);

            drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(textColor), truncatedName);
        }
        else
        {
            // Empty slot indicator
            var emptyText = "-";
            var textSize = ImGui.CalcTextSize(emptyText);
            var textPos = new Vector2(
                pos.X + (SlotWidth - textSize.X) / 2,
                pos.Y + (SlotHeight - textSize.Y) / 2);
            drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(EmptySlotTextColor), emptyText);
        }

        // Handle click
        if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            SlotActivated?.Invoke(index);
        }
    }

    private string GetItemDisplayName(HudContext context, HotbarSlot slot)
    {
        if (slot.IsEmpty || string.IsNullOrEmpty(slot.RefName))
            return string.Empty;

        var world = context.ViewModel.CurrentWorld;
        if (world == null)
            return slot.RefName;

        return slot.ItemType switch
        {
            // Saga-owned types
            HotbarItemType.Consumable => world.Gameplay?.Consumables?.FirstOrDefault(c => c.RefName == slot.RefName)?.DisplayName ?? slot.RefName,
            HotbarItemType.Spell => world.Gameplay?.Spells?.FirstOrDefault(s => s.RefName == slot.RefName)?.DisplayName ?? slot.RefName,
            HotbarItemType.Equipment => world.Gameplay?.Equipment?.FirstOrDefault(e => e.RefName == slot.RefName)?.DisplayName ?? slot.RefName,
            // External provider types - lookup via IGameplayItemProvider
            HotbarItemType.Tool or HotbarItemType.Block or HotbarItemType.BuildingMaterial =>
                LookupExternalItemDisplayName(world, slot.RefName),
            _ => slot.RefName
        };
    }

    private static string LookupExternalItemDisplayName(IWorld world, string refName)
    {
        foreach (var provider in world.GameplayItemProviders)
        {
            var item = provider.GetByRefName(refName);
            if (item != null)
                return item.DisplayName;
        }
        return refName;
    }

    private static string TruncateText(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var textSize = ImGui.CalcTextSize(text);
        if (textSize.X <= maxWidth) return text;

        // Binary search for the right length
        var ellipsis = "..";
        var ellipsisWidth = ImGui.CalcTextSize(ellipsis).X;
        var targetWidth = maxWidth - ellipsisWidth;

        for (int len = text.Length - 1; len > 0; len--)
        {
            var truncated = text[..len];
            if (ImGui.CalcTextSize(truncated).X <= targetWidth)
            {
                return truncated + ellipsis;
            }
        }

        return ellipsis;
    }
}
