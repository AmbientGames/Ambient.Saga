using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Saga.Presentation.UI.Services;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using CommunityToolkit.Mvvm.ComponentModel;

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

    /// <summary>
    /// Creates ViewModel from SagaState (event-sourced).
    /// This is the new preferred method using transaction-based state.
    /// </summary>
    public static CharacterLocationViewModel FromSagaState(
        CharacterState characterState,
        Character characterTemplate,
        SagaArc sagaArc,
        IHeightMapMetadata metadata,
        IWorld world)
    {
        // Use Saga location as character spawn location
        var latitude = sagaArc.LatitudeZ;
        var longitude = sagaArc.LongitudeX;

        var vm = new CharacterLocationViewModel
        {
            RefName = characterTemplate.RefName,
            DisplayName = characterTemplate.DisplayName,
            InteractionType = DialogueTreeAnalyzer.GetInteractionType(characterTemplate, world),
            Instance = null, // No longer using CharacterInstance - using SagaState instead
            CharacterInstanceId = characterState.CharacterInstanceId,
            Latitude = latitude,
            Longitude = longitude
        };

        // Convert geographic coordinates to pixel coordinates for rendering
        vm.PixelX = CoordinateConverter.HeightMapLongitudeToPixelX(longitude, metadata);
        vm.PixelY = CoordinateConverter.HeightMapLatitudeToPixelY(latitude, metadata);

        // Convert geographic coordinates to model coordinates for hit detection
        vm.ModelX = CoordinateConverter.LongitudeToModelX(longitude, world);
        vm.ModelZ = CoordinateConverter.LatitudeToModelZ(latitude, world);

        return vm;
    }

    /// <summary>
    /// Creates ViewModel from CharacterState and Character template.
    /// DEPRECATED: Use FromSagaState instead.
    /// </summary>
    [Obsolete("Use FromSagaState instead - use the CharacterState-based method")]
    public static CharacterLocationViewModel FromCharacterInstance(
        CharacterState instance,
        Character character,
        IHeightMapMetadata metadata,
        IWorld world)
    {
        var vm = new CharacterLocationViewModel
        {
            RefName = character.RefName,
            DisplayName = character.DisplayName,
            InteractionType = DialogueTreeAnalyzer.GetInteractionType(character, world),
            Instance = instance,
            Latitude = instance.CurrentLatitudeZ,
            Longitude = instance.CurrentLongitudeX
        };

        // Convert geographic coordinates to pixel coordinates for rendering
        vm.PixelX = CoordinateConverter.HeightMapLongitudeToPixelX(instance.CurrentLongitudeX, metadata);
        vm.PixelY = CoordinateConverter.HeightMapLatitudeToPixelY(instance.CurrentLatitudeZ, metadata);

        // Convert geographic coordinates to model coordinates for hit detection
        vm.ModelX = CoordinateConverter.LongitudeToModelX(instance.CurrentLongitudeX, world);
        vm.ModelZ = CoordinateConverter.LatitudeToModelZ(instance.CurrentLatitudeZ, world);

        return vm;
    }

    /// <summary>
    /// Gets the marker color based on character interaction type (determined by dialogue tree analysis).
    /// </summary>
    public string MarkerColor => InteractionType switch
    {
        DialogueInteractionType.Boss => "Red",
        DialogueInteractionType.Merchant => "Gold",
        DialogueInteractionType.Encounter => "Blue",
        _ => "Gray"
    };
}
