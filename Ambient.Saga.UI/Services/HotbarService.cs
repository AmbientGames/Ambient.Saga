using Ambient.Domain;
using Ambient.Domain.Hotbar;
using Ambient.Saga.Presentation.UI.ViewModels;

namespace Ambient.Saga.UI.Services;

/// <summary>
/// Service for managing hotbar slot activation and item actions.
/// </summary>
public class HotbarService
{
    private readonly SagaMainViewModel _viewModel;

    public HotbarService(SagaMainViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    /// <summary>
    /// Activates a hotbar slot by index (0-8).
    /// If the slot is already active, deactivates it.
    /// Otherwise, performs the appropriate action based on item type.
    /// </summary>
    public async Task ActivateSlotAsync(int slotIndex)
    {
        var avatar = _viewModel.PlayerAvatar;
        if (avatar == null || slotIndex < 0 || slotIndex >= 9)
            return;

        var slot = avatar.Hotbar[slotIndex];

        // If clicking the already-active slot, deactivate
        if (avatar.ActiveHotbarSlot == slotIndex)
        {
            avatar.ActiveHotbarSlot = -1;
            ClearCurrentSelections(avatar, slot.ItemType);
            return;
        }

        // If slot is empty, just select it (for assignment purposes)
        if (slot.IsEmpty)
        {
            avatar.ActiveHotbarSlot = slotIndex;
            return;
        }

        // Activate the slot
        avatar.ActiveHotbarSlot = slotIndex;

        // Perform action based on item type
        await PerformSlotActionAsync(avatar, slot);
    }

    /// <summary>
    /// Assigns an item to a hotbar slot.
    /// </summary>
    public void AssignToSlot(int slotIndex, HotbarItemType itemType, string refName)
    {
        var avatar = _viewModel.PlayerAvatar;
        if (avatar == null || slotIndex < 0 || slotIndex >= 9)
            return;

        avatar.Hotbar[slotIndex].Set(itemType, refName);
    }

    /// <summary>
    /// Clears a hotbar slot.
    /// </summary>
    public void ClearSlot(int slotIndex)
    {
        var avatar = _viewModel.PlayerAvatar;
        if (avatar == null || slotIndex < 0 || slotIndex >= 9)
            return;

        avatar.Hotbar[slotIndex].Clear();

        // If this was the active slot, deactivate
        if (avatar.ActiveHotbarSlot == slotIndex)
        {
            avatar.ActiveHotbarSlot = -1;
        }
    }

    private async Task PerformSlotActionAsync(AvatarBase avatar, HotbarSlot slot)
    {
        if (slot.IsEmpty || string.IsNullOrEmpty(slot.RefName))
            return;

        switch (slot.ItemType)
        {
            case HotbarItemType.Tool:
                avatar.CurrentToolRef = slot.RefName;
                // Clear other "active" types when selecting a tool
                avatar.CurrentBlockRef = null;
                avatar.CurrentBuildingMaterialRef = null;
                break;

            case HotbarItemType.Block:
                avatar.CurrentBlockRef = slot.RefName;
                avatar.CurrentToolRef = null;
                avatar.CurrentBuildingMaterialRef = null;
                break;

            case HotbarItemType.BuildingMaterial:
                avatar.CurrentBuildingMaterialRef = slot.RefName;
                avatar.CurrentToolRef = null;
                avatar.CurrentBlockRef = null;
                break;

            case HotbarItemType.Consumable:
                // Use the consumable
                await _viewModel.UseConsumableAsync(slot.RefName);
                break;

            case HotbarItemType.Spell:
                // Mark spell as ready (could set a "ready spell" state)
                // For now, just log it - the spell system may need integration
                System.Diagnostics.Debug.WriteLine($"[Hotbar] Spell ready: {slot.RefName}");
                break;

            case HotbarItemType.Equipment:
                // Equip the item (standard hotbar behavior - unequip is done from inventory)
                var equipDef = _viewModel.CurrentWorld?.Gameplay?.Equipment?.FirstOrDefault(e => e.RefName == slot.RefName);
                if (equipDef != null)
                {
                    var isEquipped = _viewModel.IsItemEquipped(slot.RefName);
                    if (!isEquipped)
                    {
                        var slotRef = equipDef.SlotRef.ToString();
                        await _viewModel.EquipItemAsync(slot.RefName, slotRef);
                    }
                }
                break;
        }
    }

    private void ClearCurrentSelections(AvatarBase avatar, HotbarItemType itemType)
    {
        // Clear the appropriate current ref based on item type
        switch (itemType)
        {
            case HotbarItemType.Tool:
                avatar.CurrentToolRef = null;
                break;
            case HotbarItemType.Block:
                avatar.CurrentBlockRef = null;
                break;
            case HotbarItemType.BuildingMaterial:
                avatar.CurrentBuildingMaterialRef = null;
                break;
        }
    }
}
