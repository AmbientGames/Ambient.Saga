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

    [ObservableProperty]
    private double _pixelX;

    [ObservableProperty]
    private double _pixelY;

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
