using Ambient.Domain.Hotbar;
using Ambient.Saga.UI;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering.Sections;

/// <summary>
/// Bottom-center HUD section displaying the hotbar (slots 1-9).
/// Shows item names with number key badges. Selected slot is highlighted.
/// </summary>
public class HotbarSection : IHudSection
{
    // Base slot styling at 96 DPI (1.0 scale) - public so layout can use fixed sizes
    public const float SlotWidthBase = 70f;
    public const float SlotHeightBase = 40f;
    public const float SlotSpacingBase = 4f;
    public const int SlotCount = 9;

    // DPI-scaled dimensions for rendering
    public static float SlotWidth => SlotWidthBase * UIConstants.DpiScale;
    public static float SlotHeight => SlotHeightBase * UIConstants.DpiScale;
    public static float SlotSpacing => SlotSpacingBase * UIConstants.DpiScale;

    // Calculated total width (DPI-scaled)
    public static float TotalWidth => (SlotWidth * SlotCount) + (SlotSpacing * (SlotCount - 1));

    private static float SlotRounding => 4f * UIConstants.DpiScale;
    private static float KeyBadgeSize => 16f * UIConstants.DpiScale;

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
        { HotbarItemType.Consumable, new Vector4(0.4f, 0.9f, 0.5f, 1f) },
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
        var avatar = context.ViewModel.Avatar;
        if (avatar == null) return;

        var hotbar = avatar.Hotbar;
        var activeSlot = avatar.ActiveHotbarSlot;

        var drawList = ImGui.GetWindowDrawList();
        var startPos = ImGui.GetCursorScreenPos();

        // Calculate total hotbar width and center it
        var totalWidth = (SlotWidth * 9) + (SlotSpacing * 8);
        var availableWidth = context.CenterRegionWidth;
        var offsetX = (availableWidth - totalWidth) / 2f;

        var currentX = startPos.X + offsetX;
        // Position slots at the cursor position (content area matches slot height)
        var slotY = startPos.Y;

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
        var borderThickness = (isSelected ? 2f : 1f) * UIConstants.DpiScale;
        drawList.AddRect(pos, slotRect, ImGui.ColorConvertFloat4ToU32(borderColor), SlotRounding, ImDrawFlags.None, borderThickness);

        // Key number badge (top-left corner)
        var keyText = (index + 1).ToString();
        var badgeOffset = 2f * UIConstants.DpiScale;
        var keyBadgePos = new Vector2(pos.X + badgeOffset, pos.Y + badgeOffset);
        var keyBadgeRect = new Vector2(keyBadgePos.X + KeyBadgeSize, keyBadgePos.Y + KeyBadgeSize);
        var keyBadgeRounding = 3f * UIConstants.DpiScale;
        drawList.AddRectFilled(keyBadgePos, keyBadgeRect, ImGui.ColorConvertFloat4ToU32(KeyBadgeBgColor), keyBadgeRounding);

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
            var textPadding = 8f * UIConstants.DpiScale;
            var maxTextWidth = SlotWidth - textPadding;
            var truncatedName = TruncateText(displayName, maxTextWidth);

            var textSize = ImGui.CalcTextSize(truncatedName);
            var textVerticalPadding = 4f * UIConstants.DpiScale;
            var textPos = new Vector2(
                pos.X + (SlotWidth - textSize.X) / 2,
                pos.Y + KeyBadgeSize + textVerticalPadding + (SlotHeight - KeyBadgeSize - textVerticalPadding - textSize.Y) / 2);

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
            HotbarItemType.Tool => world.TryGetToolByRefName(slot.RefName)?.DisplayName ?? slot.RefName,
            HotbarItemType.Block => world.BlockProvider?.GetDisplayName(slot.RefName) ?? slot.RefName,
            HotbarItemType.Consumable => world.Gameplay?.Consumables?.FirstOrDefault(c => c.RefName == slot.RefName)?.DisplayName ?? slot.RefName,
            HotbarItemType.Equipment => world.Gameplay?.Equipment?.FirstOrDefault(e => e.RefName == slot.RefName)?.DisplayName ?? slot.RefName,
            _ => slot.RefName
        };
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
