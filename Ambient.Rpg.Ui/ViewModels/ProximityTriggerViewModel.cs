using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Rpg.Engine.Domain.Services;
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
    private string _arcRefName = string.Empty;

    [ObservableProperty]
    private bool _isHovered = false;

    [ObservableProperty]
    private Vector4 _ringColor = new Vector4(64f / 255f, 64f / 255f, 64f / 255f, 1f); // Default grey

    [ObservableProperty]
    private double _ringOpacity = 0.15; // Default not hovered

    [ObservableProperty]
    private bool _isVisible = false; // Hidden by default, shown when Arc is hovered

    [ObservableProperty]
    private InteractionStatus _status = InteractionStatus.Available;

    /// <summary>
    /// Debug info from query result (only populated when Debugger.IsAttached)
    /// </summary>
    [ObservableProperty]
    private string _debugQueryInfo = string.Empty;

    /// <summary>
    /// Creates ViewModel from domain trigger entity.
    /// </summary>
    /// <param name="arcTrigger">Domain trigger entity</param>
    /// <param name="metadata">Height map metadata for coordinate conversion</param>
    /// <param name="arcPixelX">Arc center X position in pixels</param>
    /// <param name="arcPixelY">Arc center Y position in pixels</param>
    /// <param name="arcModelX">Arc center X position in model/world coordinates</param>
    /// <param name="arcModelZ">Arc center Z position in model/world coordinates</param>
    /// <param name="zOrder">Z-order for layering (0=back, higher=front)</param>
    /// <param name="horizontalScale">Horizontal scale for model coordinate conversion</param>
    public static ProximityTriggerViewModel FromDomain(
        ArcTrigger arcTrigger,
        IHeightMapMetadata metadata,
        double arcPixelX,
        double arcPixelY,
        double arcModelX,
        double arcModelZ,
        int zOrder,
        double horizontalScale)
    {
        // Scale radius for model space (model coordinates have HorizontalScale already applied)
        var scaledEnterRadius = arcTrigger.EnterRadius * horizontalScale;

        var vm = new ProximityTriggerViewModel
        {
            RefName = arcTrigger.RefName,
            DisplayName = arcTrigger.DisplayName,
            EnterRadius = scaledEnterRadius,
            ZOrder = zOrder,
            PixelX = arcPixelX,
            PixelY = arcPixelY,
            ModelX = arcModelX,
            ModelZ = arcModelZ
        };

        // Convert radius to pixels for rendering (use original unscaled radius)
        vm.EnterRadiusPixels = CoordinateConverter.HeightMapMetersToPixelsApproximate(arcTrigger.EnterRadius, metadata);

        return vm;
    }
}
