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
    /// Performs the appropriate action based on item type.
    /// </summary>
    public async Task ActivateSlotAsync(int slotIndex)
    {
        var avatar = _viewModel.PlayerAvatar;
        if (avatar == null || slotIndex < 0 || slotIndex >= 9)
            return;

        var slot = avatar.Hotbar[slotIndex];

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
                break;

            case HotbarItemType.Block:
                avatar.CurrentBlockRef = slot.RefName;
                break;

            case HotbarItemType.BuildingMaterial:
                avatar.CurrentBuildingMaterialRef = slot.RefName;
                break;

            case HotbarItemType.Consumable:
                // Use the consumable
                await _viewModel.UseConsumableAsync(slot.RefName);
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
}
