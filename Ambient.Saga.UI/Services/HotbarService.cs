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

        // If slot is empty, do nothing
        if (slot.IsEmpty)
            return;

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
    }

    private async Task PerformSlotActionAsync(AvatarBase avatar, HotbarSlot slot)
    {
        if (slot.IsEmpty || string.IsNullOrEmpty(slot.RefName))
            return;

        var world = _viewModel.CurrentWorld;

        switch (slot.ItemType)
        {
            case HotbarItemType.Tool:
                avatar.CurrentToolRef = slot.RefName;
                var toolDef = world?.Gameplay?.Tools?.FirstOrDefault(t => t.RefName == slot.RefName);
                var toolName = toolDef?.DisplayName ?? slot.RefName;
                _viewModel.AddToastMessage($"{toolName} equipped");
                break;

            case HotbarItemType.Block:
                avatar.CurrentBlockRef = slot.RefName;
                var blockDef = world?.BlockProvider?.GetBlockByRefName(slot.RefName);
                var blockName = blockDef?.DisplayName ?? slot.RefName;
                _viewModel.AddToastMessage($"{blockName} selected");
                break;

            case HotbarItemType.BuildingMaterial:
                avatar.CurrentBuildingMaterialRef = slot.RefName;
                var materialDef = world?.TryGetBuildingMaterialByRefName(slot.RefName);
                var materialName = materialDef?.DisplayName ?? slot.RefName;
                _viewModel.AddToastMessage($"{materialName} selected");
                break;

            case HotbarItemType.Consumable:
                // Use the consumable (toast is handled by UseConsumableAsync)
                await _viewModel.UseConsumableAsync(slot.RefName);
                break;

            case HotbarItemType.Equipment:
                // Equip the item (toast is handled by EquipItemAsync)
                var equipDef = world?.Gameplay?.Equipment?.FirstOrDefault(e => e.RefName == slot.RefName);
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
