using Ambient.Domain;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;

namespace Ambient.Presentation.WindowsUI.RpgControls.ViewModels;

/// <summary>
/// Presentation ViewModel for a proximity trigger ring on the map.
/// Proximity triggers are geographic activation zones that spawn characters.
/// All trigger rings are blue (no type classification).
/// </summary>
public partial class ProximityTriggerViewModel : ObservableObject
{
    [ObservableProperty]
    private string _refName = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private double _enterRadius;

    [ObservableProperty]
    private int _zOrder;

    [ObservableProperty]
    private double _enterRadiusPixels;

    [ObservableProperty]
    private double _pixelX;

    [ObservableProperty]
    private double _pixelY;

    [ObservableProperty]
    private double _modelX;

    [ObservableProperty]
    private double _modelZ;

    [ObservableProperty]
    private string _sagaRefName = string.Empty;

    [ObservableProperty]
    private bool _isHovered = false;

    [ObservableProperty]
    private Vector4 _ringColor = new Vector4(64f / 255f, 64f / 255f, 64f / 255f, 1f); // Default grey

    [ObservableProperty]
    private double _ringOpacity = 0.15; // Default not hovered

    [ObservableProperty]
    private bool _isVisible = false; // Hidden by default, shown when Saga is hovered

    /// <summary>
    /// Creates ViewModel from domain trigger entity.
    /// </summary>
    /// <param name="sagaTrigger">Domain trigger entity</param>
    /// <param name="metadata">Height map metadata for coordinate conversion</param>
    /// <param name="sagaPixelX">Saga center X position in pixels</param>
    /// <param name="sagaPixelY">Saga center Y position in pixels</param>
    /// <param name="sagaModelX">Saga center X position in model/world coordinates</param>
    /// <param name="sagaModelZ">Saga center Z position in model/world coordinates</param>
    /// <param name="zOrder">Z-order for layering (0=back, higher=front)</param>
    /// <param name="horizontalScale">Horizontal scale for model coordinate conversion</param>
    public static ProximityTriggerViewModel FromDomain(
        SagaTrigger sagaTrigger,
        IHeightMapMetadata metadata,
        double sagaPixelX,
        double sagaPixelY,
        double sagaModelX,
        double sagaModelZ,
        int zOrder,
        double horizontalScale)
    {
        // Scale radius for model space (model coordinates have HorizontalScale already applied)
        var scaledEnterRadius = sagaTrigger.EnterRadius * horizontalScale;

        var vm = new ProximityTriggerViewModel
        {
            RefName = sagaTrigger.RefName,
            DisplayName = sagaTrigger.DisplayName,
            EnterRadius = scaledEnterRadius,
            ZOrder = zOrder,
            PixelX = sagaPixelX,
            PixelY = sagaPixelY,
            ModelX = sagaModelX,
            ModelZ = sagaModelZ
        };

        // Convert radius to pixels for rendering (use original unscaled radius)
        vm.EnterRadiusPixels = CoordinateConverter.HeightMapMetersToPixelsApproximate(sagaTrigger.EnterRadius, metadata);

        return vm;
    }
}
