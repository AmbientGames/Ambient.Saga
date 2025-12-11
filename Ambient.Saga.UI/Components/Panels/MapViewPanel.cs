using Ambient.Domain;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;
using Ambient.Saga.UI.ViewModels;
using Ambient.Saga.UI.Components.Modals;

namespace Ambient.Saga.UI.Components.Panels;

/// <summary>
/// Center panel showing the map view with sagas and characters
/// Displays heightmap texture with overlay markers
/// </summary>
public class MapViewPanel
{
    private const float MinZoom = 0.1f;
    private const float MaxZoom = 100.0f;
    private const float ZoomSpeed = 0.3f; // Increased for more responsive zooming
    private const int CenterViewCells = 50; // Number of cells to show when centering on avatar

    // Track if we need to center on avatar (initial load or button press)
    private bool _needsInitialCenter = true;
    private float _pendingScrollX = -1;
    private float _pendingScrollY = -1;
    private double _pendingZoom = -1;

    public void Render(MainViewModel viewModel, nint heightMapTexturePtr, int heightMapWidth, int heightMapHeight, ModalManager modalManager)
    {
        var availableRegion = ImGui.GetContentRegionAvail();

        // Two-column layout: Map on left, Legend on right
        var legendWidth = 180f;
        var mapWidth = availableRegion.X - legendWidth - 10; // 10px gap

        // Left side: Map viewport
        ImGui.BeginChild("MapContainer", new Vector2(mapWidth, availableRegion.Y), ImGuiChildFlags.None);
        var mapRegion = ImGui.GetContentRegionAvail();

        // Map viewport with scrolling enabled for panning
        ImGui.BeginChild("MapViewport", new Vector2(mapRegion.X, mapRegion.Y - 40),
            ImGuiChildFlags.Borders,
            ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        // Store the child window's actual screen position (constant, not affected by scroll or cursor)
        var windowPos = ImGui.GetWindowPos();
        var windowPadding = ImGui.GetStyle().WindowPadding;
        windowPos.X += windowPadding.X;
        windowPos.Y += windowPadding.Y;

        if (heightMapTexturePtr != nint.Zero)
        {
            // Calculate scaled size to fit viewport while maintaining aspect ratio
            var aspectRatio = (float)heightMapWidth / heightMapHeight;
            var baseWidth = availableRegion.X - 20;
            var baseHeight = availableRegion.Y - 60;

            // Start with fitting to available space
            var displayWidth = baseWidth;
            var displayHeight = displayWidth / aspectRatio;

            if (displayHeight > baseHeight)
            {
                displayHeight = baseHeight;
                displayWidth = displayHeight * aspectRatio;
            }

            // Handle center on avatar request (initial load or button press)
            if ((_needsInitialCenter || viewModel.ShouldCenterOnAvatar) && viewModel.HasAvatarPosition)
            {
                // Calculate zoom to show CenterViewCells x CenterViewCells pixels
                // We want the viewport to show ~100 heightmap pixels
                var viewportWidth = mapRegion.X;
                var viewportHeight = mapRegion.Y - 40;

                // At zoom=1, displayWidth shows the full heightMapWidth
                // We want to show CenterViewCells pixels, so zoom = viewportSize / (baseSizePerPixel * CenterViewCells)
                var baseSizePerPixelX = displayWidth / heightMapWidth;
                var baseSizePerPixelY = displayHeight / heightMapHeight;

                // Calculate zoom needed to fit CenterViewCells in the viewport
                var zoomForWidth = viewportWidth / (baseSizePerPixelX * CenterViewCells);
                var zoomForHeight = viewportHeight / (baseSizePerPixelY * CenterViewCells);
                var targetZoom = Math.Min(zoomForWidth, zoomForHeight);
                targetZoom = Math.Clamp(targetZoom, MinZoom, MaxZoom);

                // Set the zoom
                viewModel.ZoomFactor = targetZoom;

                // Calculate avatar position in display coordinates (after zoom)
                var zoomedDisplayWidth = displayWidth * (float)targetZoom;
                var zoomedDisplayHeight = displayHeight * (float)targetZoom;

                // Get avatar pixel position
                var avatarPixelX = CoordinateConverter.HeightMapLongitudeToPixelX(viewModel.AvatarLongitude, viewModel.CurrentWorld!.HeightMapMetadata);
                var avatarPixelY = CoordinateConverter.HeightMapLatitudeToPixelY(viewModel.AvatarLatitude, viewModel.CurrentWorld!.HeightMapMetadata);

                // Convert to display coordinates
                var avatarDisplayX = avatarPixelX / heightMapWidth * zoomedDisplayWidth;
                var avatarDisplayY = avatarPixelY / heightMapHeight * zoomedDisplayHeight;

                // Calculate scroll to center avatar in viewport
                _pendingScrollX = (float)(avatarDisplayX - viewportWidth / 2);
                _pendingScrollY = (float)(avatarDisplayY - viewportHeight / 2);
                _pendingZoom = targetZoom;

                _needsInitialCenter = false;
                viewModel.ShouldCenterOnAvatar = false;
            }

            // Apply pending scroll if set (must be done before Image call affects scroll)
            if (_pendingScrollX >= 0)
            {
                ImGui.SetScrollX(_pendingScrollX);
                _pendingScrollX = -1;
            }
            if (_pendingScrollY >= 0)
            {
                ImGui.SetScrollY(_pendingScrollY);
                _pendingScrollY = -1;
            }

            // Apply zoom
            displayWidth *= (float)viewModel.ZoomFactor;
            displayHeight *= (float)viewModel.ZoomFactor;

            var imageSize = new Vector2(displayWidth, displayHeight);

            // Render heightmap
            ImGui.Image(heightMapTexturePtr, imageSize);

            // Store if image was hovered for input handling
            var imageHovered = ImGui.IsItemHovered();
            var imageActive = ImGui.IsItemActive();

            // Handle mouse wheel zoom centered on cursor position (keeps GPS location constant)
            if (imageHovered && !ImGui.IsAnyItemActive())
            {
                var wheel = ImGui.GetIO().MouseWheel;
                if (wheel != 0)
                {
                    // Get current scroll position
                    var currentScrollX = ImGui.GetScrollX();
                    var currentScrollY = ImGui.GetScrollY();

                    // Get mouse position relative to the window (constant regardless of scroll)
                    var mousePos = ImGui.GetMousePos();
                    var mouseOffsetX = mousePos.X - windowPos.X;
                    var mouseOffsetY = mousePos.Y - windowPos.Y;

                    // Calculate position in the FULL image (including scrolled parts)
                    var posInImageX = mouseOffsetX + currentScrollX;
                    var posInImageY = mouseOffsetY + currentScrollY;

                    // Convert to normalized coordinates (0-1) - this represents the GPS location
                    var normalizedX = posInImageX / displayWidth;
                    var normalizedY = posInImageY / displayHeight;

                    // Convert to pixel coordinates and then to GPS for BEFORE logging
                    var pixelXBefore = (int)(normalizedX * heightMapWidth);
                    var pixelYBefore = (int)(normalizedY * heightMapHeight);
                    double latBefore = 0, lonBefore = 0;
                    if (viewModel.CurrentWorld?.HeightMapMetadata != null)
                    {
                        latBefore = CoordinateConverter.HeightMapPixelYToLatitude(pixelYBefore, viewModel.CurrentWorld.HeightMapMetadata);
                        lonBefore = CoordinateConverter.HeightMapPixelXToLongitude(pixelXBefore, viewModel.CurrentWorld.HeightMapMetadata);
                    }

                    // Apply zoom
                    var oldZoom = viewModel.ZoomFactor;
                    var newZoom = oldZoom + wheel * ZoomSpeed;
                    newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);

                    if (newZoom != oldZoom)
                    {
                        viewModel.ZoomFactor = newZoom;

                        // Calculate new display size
                        var newDisplayWidth = displayWidth / (float)oldZoom * (float)newZoom;
                        var newDisplayHeight = displayHeight / (float)oldZoom * (float)newZoom;

                        // Calculate where that same GPS location (normalized coords) is in the new image
                        var newPosInImageX = normalizedX * newDisplayWidth;
                        var newPosInImageY = normalizedY * newDisplayHeight;

                        // Adjust scroll so that GPS location stays under cursor
                        // We want: newScroll + mouseOffset = newPosInImage
                        // So: newScroll = newPosInImage - mouseOffset
                        var newScrollX = newPosInImageX - mouseOffsetX;
                        var newScrollY = newPosInImageY - mouseOffsetY;

                        ImGui.SetScrollX(newScrollX);
                        ImGui.SetScrollY(newScrollY);

                        // Now check what GPS location is under the mouse AFTER zoom
                        // We need to wait for ImGui to process, but let's calculate what SHOULD be there
                        var pixelXAfter = (int)(normalizedX * heightMapWidth);
                        var pixelYAfter = (int)(normalizedY * heightMapHeight);
                        double latAfter = 0, lonAfter = 0;
                        if (viewModel.CurrentWorld?.HeightMapMetadata != null)
                        {
                            latAfter = CoordinateConverter.HeightMapPixelYToLatitude(pixelYAfter, viewModel.CurrentWorld.HeightMapMetadata);
                            lonAfter = CoordinateConverter.HeightMapPixelXToLongitude(pixelXAfter, viewModel.CurrentWorld.HeightMapMetadata);
                        }

                        System.Diagnostics.Debug.WriteLine($"ZOOM: Before=({latBefore:F6}, {lonBefore:F6}) After=({latAfter:F6}, {lonAfter:F6})");
                        System.Diagnostics.Debug.WriteLine($"      Normalized: ({normalizedX:F6}, {normalizedY:F6}) Scroll: ({currentScrollX:F1}, {currentScrollY:F1}) Mouse: ({mouseOffsetX:F1}, {mouseOffsetY:F1})");
                    }
                }
            }

            // Update mouse position tracking when hovering
            if (imageHovered)
            {
                var mousePos = ImGui.GetMousePos();
                var currentScrollX = ImGui.GetScrollX();
                var currentScrollY = ImGui.GetScrollY();

                var relativeX = mousePos.X - windowPos.X + currentScrollX;
                var relativeY = mousePos.Y - windowPos.Y + currentScrollY;

                // Check if mouse is within image bounds
                if (relativeX >= 0 && relativeX <= displayWidth &&
                    relativeY >= 0 && relativeY <= displayHeight)
                {
                    var normalizedX = relativeX / displayWidth;
                    var normalizedY = relativeY / displayHeight;

                    // Convert to pixel coordinates (keep as double for smooth interpolation)
                    var pixelX = normalizedX * heightMapWidth;
                    var pixelY = normalizedY * heightMapHeight;

                    // Update mouse position - ViewModel converts pixel to lat/lon/elevation
                    viewModel.UpdateMousePosition(pixelX, pixelY);
                }
            }
            else
            {
                viewModel.HasMousePosition = false;
            }

            // Handle right-drag for panning (when zoomed in)
            if (imageHovered && ImGui.IsMouseDragging(ImGuiMouseButton.Right))
            {
                var delta = ImGui.GetIO().MouseDelta;

                // Adjust scroll position (inverted for natural panning)
                ImGui.SetScrollX(ImGui.GetScrollX() - delta.X);
                ImGui.SetScrollY(ImGui.GetScrollY() - delta.Y);
            }

            // Handle left-click to position avatar
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                // Get mouse position relative to full image (including scroll)
                var mousePos = ImGui.GetMousePos();
                var currentScrollX = ImGui.GetScrollX();
                var currentScrollY = ImGui.GetScrollY();

                var relativeX = mousePos.X - windowPos.X + currentScrollX;
                var relativeY = mousePos.Y - windowPos.Y + currentScrollY;

                // Convert to normalized coordinates (0-1)
                if (relativeX >= 0 && relativeX <= displayWidth &&
                    relativeY >= 0 && relativeY <= displayHeight)
                {
                    var normalizedX = relativeX / displayWidth;
                    var normalizedY = relativeY / displayHeight;

                    // Convert to pixel coordinates (keep as double, don't truncate)
                    var pixelX = normalizedX * heightMapWidth;
                    var pixelY = normalizedY * heightMapHeight;

                    // Move avatar to clicked position (converts pixel to lat/lon internally)
                    viewModel.SetAvatarPositionFromPixels(pixelX, pixelY);
                }
            }

            // Overlay sagas, characters, and avatar on the heightmap
            var drawList = ImGui.GetWindowDrawList();

            // Helper to convert pixel coords to screen position
            Vector2 PixelToScreen(double pixelX, double pixelY)
            {
                // Pixel → normalized (0-1)
                var normalizedX = (float)(pixelX / heightMapWidth);
                var normalizedY = (float)(pixelY / heightMapHeight);

                // Normalized → display coords (zoomed image size)
                var displayX = normalizedX * displayWidth;
                var displayY = normalizedY * displayHeight;

                // Display → screen coords (account for window position and scroll)
                var screenX = windowPos.X + displayX - ImGui.GetScrollX();
                var screenY = windowPos.Y + displayY - ImGui.GetScrollY();

                return new Vector2(screenX, screenY);
            }

            // Helper to convert GPS coords to screen position
            Vector2 GpsToScreen(double latitude, double longitude)
            {
                if (viewModel.CurrentWorld?.HeightMapMetadata == null)
                    return new Vector2(-1000, -1000); // Off-screen

                // GPS → pixel coords
                var pixelX = CoordinateConverter.HeightMapLongitudeToPixelX(longitude, viewModel.CurrentWorld.HeightMapMetadata);
                var pixelY = CoordinateConverter.HeightMapLatitudeToPixelY(latitude, viewModel.CurrentWorld.HeightMapMetadata);

                return PixelToScreen(pixelX, pixelY);
            }

            // Helper to convert radius in pixels to screen pixels (accounting for zoom and viewport fit)
            // The trigger radius is in heightmap pixels. Convert to screen pixels using the same
            // scale factor as the image. displayWidth already includes both viewport fitting and zoom.
            float RadiusPixelsToScreen(double radiusPixels)
            {
                var pixelsPerHeightMapPixel = displayWidth / heightMapWidth;
                return (float)(radiusPixels * pixelsPerHeightMapPixel);
            }

            // Draw Saga zones (proximity trigger rings)
            foreach (var saga in viewModel.Sagas)
            {
                foreach (var trigger in saga.Triggers)
                {
                    // Only show triggers when visible (mouse hover)
                    if (!trigger.IsVisible)
                        continue;

                    var center = PixelToScreen(trigger.PixelX, trigger.PixelY);

                    // Skip if off-screen
                    if (center.X < -500 || center.Y < -500)
                        continue;

                    var enterRadius = RadiusPixelsToScreen(trigger.EnterRadiusPixels);

                    // Use ring color from ViewModel (ImGui Vector4 color)
                    var color = trigger.RingColor;
                    var opacity = (float)trigger.RingOpacity;
                    var circleColor = ImGui.ColorConvertFloat4ToU32(new Vector4(
                        color.X,
                        color.Y,
                        color.Z,
                        opacity));

                    // Draw trigger ring
                    drawList.AddCircleFilled(center, enterRadius, circleColor, 32);
                }
            }

            // Draw Saga feature center dots (always visible at Saga centers)
            foreach (var saga in viewModel.Sagas)
            {
                var center = PixelToScreen(saga.PixelX, saga.PixelY);

                // Skip if off-screen
                if (center.X < -500 || center.Y < -500)
                    continue;

                // Use feature dot color from ViewModel (ImGui Vector4 color)
                var color = saga.FeatureDotColor;
                var opacity = (float)saga.FeatureDotOpacity;
                var dotColor = ImGui.ColorConvertFloat4ToU32(new Vector4(
                    color.X,
                    color.Y,
                    color.Z,
                    opacity));

                // Draw feature marker dot (scaled with zoom, clamped) - small size like WPF
                var dotRadius = 2.5f * (float)viewModel.ZoomFactor;
                dotRadius = Math.Max(2, Math.Min(dotRadius, 5)); // Clamp between 2 and 5 pixels

                drawList.AddCircleFilled(center, dotRadius, dotColor, 12);
                drawList.AddCircle(center, dotRadius, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 1)), 12, 1.0f); // Black outline

                // Check if mouse is hovering over this dot (with slightly larger hit area)
                var mousePos = ImGui.GetMousePos();
                var distance = MathF.Sqrt(MathF.Pow(mousePos.X - center.X, 2) + MathF.Pow(mousePos.Y - center.Y, 2));
                if (distance <= dotRadius + 3) // 3px tolerance for easier hovering
                {
                    // Draw tooltip with display name
                    ImGui.SetTooltip(saga.DisplayName);

                    // Handle click to interact with quest signpost
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && saga.FeatureType == FeatureType.QuestSignpost)
                    {
                        // Get quest signpost details from world
                        var sagaArc = viewModel.CurrentWorld?.Gameplay?.SagaArcs?.FirstOrDefault(s => s.RefName == saga.RefName);
                        if (sagaArc != null && !string.IsNullOrEmpty(sagaArc.SagaFeatureRef))
                        {
                            // Find quest feature
                            var questFeature = viewModel.CurrentWorld?.TryGetSagaFeatureByRefName(sagaArc.SagaFeatureRef);
                            if (questFeature != null && questFeature.Type == SagaFeatureType.Quest)
                            {
                                // Open quest modal with quest details
                                modalManager.OpenQuestSignpost(
                                    questFeature.QuestRef,
                                    saga.RefName,
                                    sagaArc.SagaFeatureRef,
                                    viewModel);
                            }
                        }
                    }
                }
            }

            //// Draw spawned characters
            //if (viewModel.Characters.Count > 0)
            //{
            //    System.Diagnostics.Debug.WriteLine($"[MapViewPanel] Rendering {viewModel.Characters.Count} characters");
            //}

            foreach (var character in viewModel.Characters)
            {
                var pos = PixelToScreen(character.PixelX, character.PixelY);
                //System.Diagnostics.Debug.WriteLine($"[MapViewPanel] Character '{character.DisplayName}' at pixel ({character.PixelX:F1}, {character.PixelY:F1}) -> screen ({pos.X:F1}, {pos.Y:F1})");

                // Skip if way off-screen (allow some buffer for partial visibility)
                if (pos.X < -100 || pos.Y < -100)
                {
                    System.Diagnostics.Debug.WriteLine($"[MapViewPanel]   -> SKIPPED (off-screen)");
                    continue;
                }

                // Use marker color from ViewModel (ImGui Vector4 color)
                var markerColor = ImGui.ColorConvertFloat4ToU32(character.MarkerColor);

                // Draw filled circle for character (match WPF size - 10px diameter = 5px radius)
                var radius = 5f; // Fixed size like WPF, don't scale with zoom to keep readable

                drawList.AddCircleFilled(pos, radius, markerColor, 12);
                drawList.AddCircle(pos, radius, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 1)), 12, 1.0f); // Black outline

                // Check if mouse is hovering over this character (with slightly larger hit area)
                var mousePos = ImGui.GetMousePos();
                var distance = MathF.Sqrt(MathF.Pow(mousePos.X - pos.X, 2) + MathF.Pow(mousePos.Y - pos.Y, 2));
                if (distance <= radius + 3) // 3px tolerance for easier hovering
                {
                    // Draw tooltip with display name
                    ImGui.SetTooltip(character.DisplayName);

                    // Handle click to interact with character
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        // Always start with dialogue - dialogue determines next steps (battle, trade, etc.)
                        modalManager.SelectedCharacter = character;
                        modalManager.ShowDialogue = true;
                    }
                }
            }

            // Draw avatar position (match WPF: Lime circle, 12px diameter = 6px radius)
            if (viewModel.HasAvatarPosition)
            {
                var avatarPos = GpsToScreen(viewModel.AvatarLatitude, viewModel.AvatarLongitude);

                if (avatarPos.X > -100 && avatarPos.Y > -100)
                {
                    // Draw avatar as lime green circle (matching WPF)
                    var avatarColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0, 1, 0, 1)); // Lime
                    var outlineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0.5f, 0, 1)); // DarkGreen
                    var radius = 6f;

                    drawList.AddCircleFilled(avatarPos, radius, avatarColor, 12);
                    drawList.AddCircle(avatarPos, radius, outlineColor, 12, 2.0f); // DarkGreen outline

                    // Check if mouse is hovering over avatar
                    var mousePos = ImGui.GetMousePos();
                    var distance = MathF.Sqrt(MathF.Pow(mousePos.X - avatarPos.X, 2) + MathF.Pow(mousePos.Y - avatarPos.Y, 2));
                    if (distance <= radius + 3)
                    {
                        var avatarName = viewModel.PlayerAvatar?.DisplayName ?? "Avatar";
                        ImGui.SetTooltip($"{avatarName} (You)");
                    }
                }
            }
        }
        else
        {
            ImGui.TextWrapped("No heightmap loaded. Select a world configuration to begin.");
        }

        ImGui.EndChild();

        // Map controls at bottom
        if (ImGui.Button("Center on Avatar"))
        {
            viewModel.ShouldCenterOnAvatar = true;
        }
        ImGui.SameLine();

        // Zoom controls
        if (ImGui.Button("-"))
        {
            viewModel.ZoomFactor = Math.Max(MinZoom, viewModel.ZoomFactor - 0.5);
        }
        ImGui.SameLine();
        ImGui.Text($"Zoom: {viewModel.ZoomFactor:F1}x");
        ImGui.SameLine();
        if (ImGui.Button("+"))
        {
            viewModel.ZoomFactor = Math.Min(MaxZoom, viewModel.ZoomFactor + 0.5);
        }
        ImGui.SameLine();
        if (ImGui.Button("Show All"))
        {
            viewModel.ZoomFactor = 1.0;
            ImGui.SetScrollX(0);
            ImGui.SetScrollY(0);
        }

        // Map info
        if (heightMapTexturePtr != nint.Zero)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), $"({heightMapWidth}x{heightMapHeight})");
        }

        ImGui.EndChild(); // End MapContainer

        // Right side: Legend panel
        ImGui.SameLine();
        ImGui.BeginChild("LegendPanel", new Vector2(legendWidth, availableRegion.Y), ImGuiChildFlags.Borders);

        MapLegend.Render();

        // World stats
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"Sagas: {viewModel.Sagas.Count}");
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"Characters: {viewModel.Characters.Count}");

        if (viewModel.Characters.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), "Move into Saga zones to spawn characters");
        }

        // Mouse position info (only visible when hovering over map)
        if (viewModel.HasMousePosition)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(new Vector4(1, 1, 0.5f, 1), "Mouse:");
            ImGui.Text($"Lat: {viewModel.MouseLatitude:F4}");
            ImGui.Text($"Lon: {viewModel.MouseLongitude:F4}");
            ImGui.Text($"Elev: {viewModel.MouseElevation}m");
        }

        ImGui.EndChild(); // End LegendPanel
    }
}
