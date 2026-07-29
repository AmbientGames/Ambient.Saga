using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Presentation.WindowsUI.RpgControls.ViewModels;
using Ambient.Rpg.Engine.Contracts;
using Ambient.Rpg.Engine.Contracts.Persistence;
using Ambient.Rpg.Engine.Domain.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Numerics;
using Ambient.Rpg.Ui.ViewModels;

namespace Ambient.Rpg.Presentation.UI.ViewModels;

/// <summary>
/// Pure presentation ViewModel for an arc.
/// Only contains display properties - no game logic.
/// </summary>
public partial class ArcViewModel : ObservableObject
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
    private ObservableCollection<ProximityTriggerViewModel> _triggers = new();

    [ObservableProperty]
    private string[]? _requiresQuestTokens;

    [ObservableProperty]
    private string[]? _givesQuestTokens;

    [ObservableProperty]
    private ArcCategory _category;

    [ObservableProperty]
    private Vector4 _featureDotColor = new Vector4(1f, 1f, 1f, 1f); // White

    [ObservableProperty]
    private double _featureDotOpacity = 1.0;

    [ObservableProperty]
    private InteractionStatus _interactionStatus = InteractionStatus.Available;

    /// <summary>
    /// Whether this arc's marker/rings should be drawn on the map. Hidden arcs stay off the map
    /// for clutter reasons until discovered, but still participate in gameplay (proximity, triggers, tokens).
    /// </summary>
    [ObservableProperty]
    private bool _isVisibleOnMap = true;

    /// <summary>
    /// True if any trigger in this arc is currently hovered (for showing label).
    /// </summary>
    public bool IsAnyTriggerHovered => Triggers.Any(t => t.IsHovered);

    /// <summary>
    /// Creates ViewModel from domain Arc entity and its pre-expanded triggers.
    /// </summary>
    public static ArcViewModel FromDomain(
        Arc arc,
        List<ArcTrigger> expandedArcTriggers,
        IHeightMapMetadata metadata,
        IWorld world)
    {
        var vm = new ArcViewModel
        {
            RefName = arc.RefName,
            DisplayName = arc.DisplayName,
            Latitude = arc.Latitude,
            Longitude = arc.Longitude,
            Category = arc.Category
        };

        // Convert geographic coordinates to pixel coordinates for rendering
        vm.PixelX = CoordinateConverter.HeightMapLongitudeToPixelX(arc.Longitude, metadata);
        vm.PixelY = CoordinateConverter.HeightMapLatitudeToPixelY(arc.Latitude, metadata);

        // Convert geographic coordinates to model coordinates for hit detection
        var modelX = CoordinateConverter.LongitudeToModelX(arc.Longitude, world);
        var modelZ = CoordinateConverter.LatitudeToModelZ(arc.Latitude, world);

        // Create proximity trigger ViewModels with Z-order priority (inner rings on top)
        // Triggers are already sorted outer→inner by domain service
        // All triggers are blue (no type classification)
        for (int i = 0; i < expandedArcTriggers.Count; i++)
        {
            var trigger = expandedArcTriggers[i];

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
    /// Loads all Arcs from World and creates ViewModels.
    /// Filters based on InitialState and discovery status.
    /// Returns both Arc ViewModels and flattened trigger list for XAML binding.
    /// </summary>
    public static async Task<(List<ArcViewModel> Arcs, List<ProximityTriggerViewModel> AllArcTriggers)> LoadFromWorldAsync(
        IWorld world,
        AvatarBase? avatar = null,
        IWorldStateRepository? worldRepository = null,
        IAvatarProgressRepository? progressRepo = null)
    {
        var arcs = new List<ArcViewModel>();
        var allArcTriggers = new List<ProximityTriggerViewModel>();
        var isDebugMode = System.Diagnostics.Debugger.IsAttached;

        if (world.HeightMapMetadata != null && world.Gameplay.Saga != null)
        {
            foreach (var arc in world.Gameplay.Saga)
            {
                // Ensure every arc has a per-avatar instance, regardless of visibility —
                // gives token fan-out a target and lets Hidden arcs receive their ArcDiscovered transaction later.
                await worldRepository.GetArcInstanceAsync(avatar.AvatarId.ToString(), arc.RefName);

                // Get pre-expanded triggers from world lookup
                if (!world.ArcTriggersLookup.TryGetValue(arc.RefName, out var arcTriggers))
                    continue;

                // Create ViewModel using FromDomain method (always — hidden only affects map display)
                var arcVM = FromDomain(
                    arc,
                    arcTriggers.OrderByDescending(t => t.EnterRadius).ToList(), // Sorted outer→inner
                    world.HeightMapMetadata,
                    world);

                // Map visibility: Hidden arcs stay off the map until discovered. Gameplay is unaffected.
                arcVM.IsVisibleOnMap = await IsArcVisibleAsync(arc, avatar, worldRepository, isDebugMode);

                // Set feature dot visual properties based on interaction status
                await SetFeatureStatusAsync(arcVM, arc, avatar, world, worldRepository);

                arcs.Add(arcVM);

                // Populate AllTriggers for XAML rendering (MainWindow.xaml binds to it)
                // Set trigger colors based on status
                foreach (var triggerVM in arcVM.Triggers)
                {
                    triggerVM.ArcRefName = arcVM.RefName;
                    triggerVM.IsHovered = false;

                    // Query trigger status and set color
                    await SetTriggerStatusAsync(triggerVM, arc, avatar, world, worldRepository, progressRepo);

                    allArcTriggers.Add(triggerVM);
                }
            }
        }

        return (arcs, allArcTriggers);
    }

    /// <summary>
    /// Determines if an arc should be visible on the map based on InitialState.
    /// </summary>
    private static async Task<bool> IsArcVisibleAsync(
        Arc arc,
        AvatarBase? avatar,
        IWorldStateRepository? worldRepository,
        bool isDebugMode)
    {
        // Debug mode: show all arcs regardless of InitialState
        if (isDebugMode)
            return true;

        return arc.InitialState switch
        {
            ArcInitialState.Visible => true,
            ArcInitialState.Hidden => await IsArcDiscoveredAsync(arc.RefName, avatar, worldRepository),
            _ => true // Default to visible
        };
    }

    /// <summary>
    /// Checks if the avatar has discovered an arc.
    /// </summary>
    private static async Task<bool> IsArcDiscoveredAsync(
        string arcRef,
        AvatarBase? avatar,
        IWorldStateRepository? worldRepository)
    {
        if (avatar == null || worldRepository == null)
            return false;

        try
        {
            var avatarId = avatar.AvatarId.ToString();
            return await worldRepository.HasDiscoveredAsync(avatarId, "Arc", arcRef);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets the feature dot color and opacity based on interaction status.
    /// </summary>
    private static Task SetFeatureStatusAsync(
        ArcViewModel arcVM,
        Arc arc,
        AvatarBase? avatar,
        IWorld world,
        IWorldStateRepository worldRepository)
    {
        // Set default status based on Arc type
        // Characters spawned by triggers determine actual interaction availability
        arcVM.InteractionStatus = InteractionStatus.Available;
        var color = ArcColors.GetColor(arcVM.Category, InteractionStatus.Available);
        arcVM.FeatureDotColor = color;
        arcVM.FeatureDotOpacity = 1.0;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets the trigger ring color based on interaction status.
    /// </summary>
    private static async Task SetTriggerStatusAsync(
        ProximityTriggerViewModel triggerVM,
        Arc arc,
        AvatarBase? avatar,
        IWorld world,
        IWorldStateRepository worldRepository,
        IAvatarProgressRepository? progressRepo = null)
    {
        // Convert Arc GPS to model coordinates for query
        var arcModelX = CoordinateConverter.LongitudeToModelX(arc.Longitude, world);
        var arcModelZ = CoordinateConverter.LatitudeToModelZ(arc.Latitude, world);

        // Query application service for trigger status at Arc center; the progress
        // repo lets lock status honor quest tokens awarded by other arcs
        var interactions = await ArcProximityService.QueryAllInteractionsAtPositionAsync(
            arcModelX, arcModelZ, avatar, world, worldRepository, progressRepo);

        var triggerInteraction = interactions.FirstOrDefault(i =>
            i.Type == ArcInteractionType.ArcTrigger &&
            i.ArcRef == arc.RefName &&
            i.ArcTriggerRef == triggerVM.RefName);

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
