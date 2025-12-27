using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Presentation.WindowsUI.RpgControls.ViewModels;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Engine.Domain.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Numerics;
using Ambient.Saga.UI.ViewModels;

namespace Ambient.Saga.Presentation.UI.ViewModels;

/// <summary>
/// Pure presentation ViewModel for a Saga.
/// Only contains display properties - no game logic.
/// </summary>
public partial class SagaViewModel : ObservableObject
{
    [ObservableProperty]
    private string _refName = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private double _latitudeZ;

    [ObservableProperty]
    private double _longitudeX;

    [ObservableProperty]
    private double _pixelX;

    [ObservableProperty]
    private double _pixelY;

    [ObservableProperty]
    private ObservableCollection<ProximityTriggerViewModel> _triggers = new();

    [ObservableProperty]
    private string[]? _requiresQuestTokens;

    [ObservableProperty]
    private string[]? _givesQuestTokens;

    [ObservableProperty]
    private FeatureType _featureType;

    [ObservableProperty]
    private Vector4 _featureDotColor = new Vector4(1f, 1f, 1f, 1f); // White

    [ObservableProperty]
    private double _featureDotOpacity = 1.0;

    [ObservableProperty]
    private InteractionStatus _interactionStatus = InteractionStatus.Available;

    /// <summary>
    /// True if any trigger in this Saga is currently hovered (for showing label).
    /// </summary>
    public bool IsAnyTriggerHovered => Triggers.Any(t => t.IsHovered);

    /// <summary>
    /// Creates ViewModel from domain Saga entity and its pre-expanded triggers.
    /// </summary>
    public static SagaViewModel FromDomain(
        SagaArc sagaArc,
        List<SagaTrigger> expandedSagaTriggers,
        IHeightMapMetadata metadata,
        IWorld world)
    {
        // Determine feature type from the SagaArc.Type (for coloring the center dot)
        FeatureType featureType = sagaArc.Type switch
        {
            SagaArcType.Landmark => FeatureType.Landmark,
            SagaArcType.Structure => FeatureType.Structure,
            SagaArcType.Quest => FeatureType.QuestSignpost,
            SagaArcType.ResourceNode => FeatureType.ResourceNode,
            SagaArcType.Vendor => FeatureType.Vendor,
            _ => FeatureType.Structure // Default for unknown types
        };

        var vm = new SagaViewModel
        {
            RefName = sagaArc.RefName,
            DisplayName = sagaArc.DisplayName,
            LatitudeZ = sagaArc.LatitudeZ,
            LongitudeX = sagaArc.LongitudeX,
            FeatureType = featureType
        };

        // Convert geographic coordinates to pixel coordinates for rendering
        vm.PixelX = CoordinateConverter.HeightMapLongitudeToPixelX(sagaArc.LongitudeX, metadata);
        vm.PixelY = CoordinateConverter.HeightMapLatitudeToPixelY(sagaArc.LatitudeZ, metadata);

        // Convert geographic coordinates to model coordinates for hit detection
        var modelX = CoordinateConverter.LongitudeToModelX(sagaArc.LongitudeX, world);
        var modelZ = CoordinateConverter.LatitudeToModelZ(sagaArc.LatitudeZ, world);

        // Create proximity trigger ViewModels with Z-order priority (inner rings on top)
        // Triggers are already sorted outer→inner by domain service
        // All triggers are blue (no type classification)
        for (int i = 0; i < expandedSagaTriggers.Count; i++)
        {
            var trigger = expandedSagaTriggers[i];

            // Get horizontal scale for model space calculations
            var horizontalScale = world.IsProcedural ? 1.0 : world.WorldConfiguration.HeightMapSettings.HorizontalScale;

            // Create proximity trigger ViewModel (all triggers are blue)
            var triggerVM = ProximityTriggerViewModel.FromDomain(
                trigger,
                metadata,
                vm.PixelX,
                vm.PixelY,
                modelX,
                modelZ,
                zOrder: i,
                horizontalScale: horizontalScale);

            // Subscribe to trigger hover changes to update label visibility
            triggerVM.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ProximityTriggerViewModel.IsHovered))
                {
                    vm.OnPropertyChanged(nameof(IsAnyTriggerHovered));
                }
            };

            vm.Triggers.Add(triggerVM);
        }

        return vm;
    }

    /// <summary>
    /// Loads all Sagas from World and creates ViewModels.
    /// Returns both Saga ViewModels and flattened trigger list for XAML binding.
    /// </summary>
    public static async Task<(List<SagaViewModel> Sagas, List<ProximityTriggerViewModel> AllSagaTriggers)> LoadFromWorldAsync(
        IWorld world,
        AvatarBase? avatar = null,
        IWorldStateRepository worldRepository = null)
    {
        var sagas = new List<SagaViewModel>();
        var allSagaTriggers = new List<ProximityTriggerViewModel>();

        if (world.HeightMapMetadata != null && world.Gameplay.SagaArcs != null)
        {
            foreach (var sagaArc in world.Gameplay.SagaArcs)
            {
                // Get pre-expanded triggers from world lookup
                if (!world.SagaTriggersLookup.TryGetValue(sagaArc.RefName, out var sagaTriggers))
                    continue;

                // Create ViewModel using FromDomain method
                var sagaVM = FromDomain(
                    sagaArc,
                    sagaTriggers.OrderByDescending(t => t.EnterRadius).ToList(), // Sorted outer→inner
                    world.HeightMapMetadata,
                    world);

                // Set feature dot visual properties based on interaction status
                await SetFeatureStatusAsync(sagaVM, sagaArc, avatar, world, worldRepository);

                sagas.Add(sagaVM);

                // Populate AllTriggers for XAML rendering (MainWindow.xaml binds to it)
                // Set trigger colors based on status
                foreach (var triggerVM in sagaVM.Triggers)
                {
                    triggerVM.SagaRefName = sagaVM.RefName;
                    triggerVM.IsHovered = false;

                    // Query trigger status and set color
                    await SetTriggerStatusAsync(triggerVM, sagaArc, avatar, world, worldRepository);

                    allSagaTriggers.Add(triggerVM);
                }
            }
        }

        return (sagas, allSagaTriggers);
    }

    /// <summary>
    /// Sets the feature dot color and opacity based on interaction status.
    /// </summary>
    private static async Task SetFeatureStatusAsync(
        SagaViewModel sagaVM,
        SagaArc sagaArc,
        AvatarBase? avatar,
        IWorld world,
        IWorldStateRepository worldRepository)
    {
        // Convert Saga GPS to model coordinates for query
        var sagaModelX = CoordinateConverter.LongitudeToModelX(sagaArc.LongitudeX, world);
        var sagaModelZ = CoordinateConverter.LatitudeToModelZ(sagaArc.LatitudeZ, world);

        // Set default status based on SagaArc type
        // Characters spawned by triggers determine actual interaction availability
        sagaVM.InteractionStatus = InteractionStatus.Available;
        sagaVM.FeatureDotColor = FeatureColors.GetColor(sagaVM.FeatureType, InteractionStatus.Available);
        sagaVM.FeatureDotOpacity = 1.0;
    }

    /// <summary>
    /// Sets the trigger ring color based on interaction status.
    /// </summary>
    private static async Task SetTriggerStatusAsync(
        ProximityTriggerViewModel triggerVM,
        SagaArc sagaArc,
        AvatarBase? avatar,
        IWorld world,
        IWorldStateRepository worldRepository)
    {
        // Convert Saga GPS to model coordinates for query
        var sagaModelX = CoordinateConverter.LongitudeToModelX(sagaArc.LongitudeX, world);
        var sagaModelZ = CoordinateConverter.LatitudeToModelZ(sagaArc.LatitudeZ, world);

        // Query application service for trigger status at Saga center
        var interactions = await SagaProximityService.QueryAllInteractionsAtPositionAsync(
            sagaModelX, sagaModelZ, avatar, world, worldRepository);

        var triggerInteraction = interactions.FirstOrDefault(i =>
            i.Type == SagaInteractionType.SagaTrigger &&
            i.SagaRef == sagaArc.RefName &&
            i.SagaTriggerRef == triggerVM.RefName);

        if (triggerInteraction != null)
        {
            // Store status for filtering completed triggers
            triggerVM.Status = triggerInteraction.Status;

            // Use pre-calculated solid colors based on status
            triggerVM.RingColor = TriggerColors.GetColor(triggerInteraction.Status);
            triggerVM.RingOpacity = 0.15; // Base opacity when not hovered
        }
    }
}
