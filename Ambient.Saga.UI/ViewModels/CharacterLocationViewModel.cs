using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using CommunityToolkit.Mvvm.ComponentModel;
using Ambient.Saga.UI.Services;

namespace Ambient.Saga.Presentation.UI.ViewModels;

/// <summary>
/// Pure presentation ViewModel for a spawned Character on the map.
/// Only contains display properties - no game logic.
/// </summary>
public partial class CharacterLocationViewModel : ObservableObject
{
    [ObservableProperty]
    private string _refName = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private double _latitude;

    [ObservableProperty]
    private double _longitude;

    [ObservableProperty]
    private double _pixelX;

    [ObservableProperty]
    private double _pixelY;

    [ObservableProperty]
    private double _modelX;

    [ObservableProperty]
    private double _modelZ;

    [ObservableProperty]
    private DialogueInteractionType _interactionType;

    [ObservableProperty]
    private CharacterState? _instance;

    [ObservableProperty]
    private Guid _characterInstanceId;
}
