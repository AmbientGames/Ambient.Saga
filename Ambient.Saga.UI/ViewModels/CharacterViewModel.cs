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

    [ObservableProperty]
    private bool _canAttack;

    [ObservableProperty]
    private bool _canLoot;

    [ObservableProperty]
    private bool _hasBeenLooted;

    [ObservableProperty]
    private Vector4 _markerColor = new Vector4(1f, 0.65f, 0f, 1f); // Orange
}
