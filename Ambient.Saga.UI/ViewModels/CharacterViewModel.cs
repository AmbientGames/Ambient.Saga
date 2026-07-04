using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.ViewModels;

public partial class CharacterViewModel : ObservableObject
{
    [ObservableProperty]
    private Guid _characterInstanceId = Guid.Empty;

    [ObservableProperty]
    private string _characterRef = string.Empty;

    [ObservableProperty]
    private string _sagaRef = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _characterType = string.Empty;

    // Map display coordinates (for Sandbox/UI)
    [ObservableProperty]
    private double _pixelX;

    [ObservableProperty]
    private double _pixelY;

    // GPS world coordinates
    [ObservableProperty]
    private double _latitude;

    [ObservableProperty]
    private double _longitude;

    [ObservableProperty]
    private double _elevation;

    // 3D model/world coordinates (for game engines)
    [ObservableProperty]
    private double _modelX;

    [ObservableProperty]
    private double _modelY;

    [ObservableProperty]
    private double _modelZ;

    [ObservableProperty]
    private bool _isAlive;

    [ObservableProperty]
    private bool _canDialogue;

    [ObservableProperty]
    private bool _canTrade;

    /// <summary>
    /// True when this character represents a cache (geocache, remnant Loot) rather than a paid merchant.
    /// Affects the trade modal: hides money/price, relabels Buy→Take and Sell→Deposit.
    /// </summary>
    [ObservableProperty]
    private bool _isCache;

    /// <summary>
    /// Source arc kind ("Market", "GeoCache", "RemnantLoot") when this character was opened
    /// from a saga arc. Empty for plain NPC dialogue. The trade modal uses it to title the
    /// window appropriately so a cache doesn't show up as "Trade".
    /// </summary>
    [ObservableProperty]
    private string _arcKind = string.Empty;

    /// <summary>
    /// Optional callback that renders extra ImGui content inside the trade modal, after the inventory
    /// list and before the close button. Used by callers to inject bespoke sections (e.g. a geocache
    /// logbook) without pushing game-specific concepts into the Saga layer.
    /// </summary>
    public Action? ExtraContentRenderer { get; set; }

    [ObservableProperty]
    private bool _canAttack;

    [ObservableProperty]
    private Vector4 _markerColor = new Vector4(1f, 0.65f, 0f, 1f); // Orange
}
