using Ambient.Domain;

namespace Ambient.Saga.UI.ViewModels;

public class EquipmentChangedEventArgs : EventArgs
{
    public LoadoutSlot SlotRef { get; set; }
    public string EquipmentRef { get; set; } = string.Empty;
    public EquipmentAction Action { get; set; }
    public Dictionary<LoadoutSlot, string> EquippedItems { get; set; } = new();
}

public enum EquipmentAction
{
    Equipped,
    Unequipped
}
