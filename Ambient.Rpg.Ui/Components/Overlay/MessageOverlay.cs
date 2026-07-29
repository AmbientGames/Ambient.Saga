using System.Numerics;
using Ambient.Rpg.Rendering.DirectX;
using Ambient.Rpg.Ui.Configuration;
using ImGuiNET;

namespace Ambient.Rpg.Ui.Components.Overlay;

/// <summary>
/// Message types for visual styling.
/// </summary>
public enum MessageType
{
    /// <summary>General information (white)</summary>
    Info,
    /// <summary>Warning message (yellow)</summary>
    Warning,
    /// <summary>Error message (red)</summary>
    Error,
    /// <summary>Combat narration (orange)</summary>
    Combat,
    /// <summary>Quest update (cyan)</summary>
    Quest,
    /// <summary>Loot/reward (green)</summary>
    Loot,
    /// <summary>Developer info (gray) - only shown when ShowDeveloperInfo is true</summary>
    Dev
}

/// <summary>
/// A message displayed in the overlay.
/// </summary>
public record OverlayMessage
{
    public required string Text { get; init; }
    public required MessageType Type { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required float Duration { get; init; }

    /// <summary>
    /// Returns value from 0 (just created) to 1 (expired).
    /// </summary>
    public float GetProgress(DateTime now)
    {
        var elapsed = (float)(now - CreatedAt).TotalSeconds;
        return Math.Clamp(elapsed / Duration, 0f, 1f);
    }

    /// <summary>
    /// Returns alpha value that fades out in the last 0.5s.
    /// </summary>
    public float GetAlpha(DateTime now)
    {
        var remaining = Duration - (float)(now - CreatedAt).TotalSeconds;
        if (remaining > 0.5f) return 1f;
        if (remaining <= 0f) return 0f;
        return remaining / 0.5f; // Fade from 1 to 0 over last 0.5s
    }

    public bool IsExpired(DateTime now) => GetProgress(now) >= 1f;
}

/// <summary>
/// Floating message overlay that displays toast-style notifications.
/// Messages stack from bottom and fade out over time.
/// </summary>
/// <remarks>
/// Usage:
/// 1. Call AddMessage() to queue a message
/// 2. Call Render() each frame in your ImGui loop
///
/// Message types have different colors:
/// - Info: White
/// - Warning: Yellow
/// - Error: Red
/// - Combat: Orange
/// - Quest: Cyan
/// - Loot: Green
/// - Dev: Gray (only shown when GameConfiguration.ShowDeveloperInfo is true)
/// </remarks>
public class MessageOverlay
{
    private readonly List<OverlayMessage> _messages = new();
    private readonly object _lock = new();

    /// <summary>
    /// Maximum messages to display at once.
    /// </summary>
    public int MaxVisibleMessages { get; set; } = 8;

    /// <summary>
    /// Padding from screen edge.
    /// </summary>
    public float EdgePadding { get; set; } = 20f;

    /// <summary>
    /// Vertical spacing between messages.
    /// </summary>
    public float MessageSpacing { get; set; } = 4f;

    /// <summary>
    /// Add a message to the overlay. If an identical message already exists,
    /// its duration is extended instead of adding a duplicate.
    /// </summary>
    /// <param name="text">Message text</param>
    /// <param name="type">Message type for styling</param>
    /// <param name="duration">How long to display (seconds)</param>
    public void AddMessage(string text, MessageType type = MessageType.Info, float duration = 3f)
    {
        // Skip dev messages if not in dev mode
        if (type == MessageType.Dev && !GameConfiguration.ShowDeveloperInfo)
            return;

        lock (_lock)
        {
            // Check if identical message already exists
            var existing = _messages.FindIndex(m => m.Text == text && m.Type == type);
            if (existing >= 0)
            {
                // Replace with fresh timing (extends the message duration)
                _messages.RemoveAt(existing);
            }

            _messages.Add(new OverlayMessage
            {
                Text = text,
                Type = type,
                CreatedAt = DateTime.UtcNow,
                Duration = duration
            });
        }
    }

    /// <summary>
    /// Render the message overlay. Call this each frame.
    /// </summary>
    /// <param name="displaySize">Screen size for positioning</param>
    /// <param name="hudHeight">Height of HUD bar to avoid overlapping (0 if no HUD)</param>
    /// <param name="topOffset">Offset from top of screen (to position below HudText1)</param>
    public void Render(Vector2 displaySize, float hudHeight = 0f, float topOffset = 0f)
    {
        var now = DateTime.UtcNow;

        // Remove expired messages
        lock (_lock)
        {
            _messages.RemoveAll(m => m.IsExpired(now));
        }

        List<OverlayMessage> visibleMessages;
        lock (_lock)
        {
            // Take most recent messages up to max
            visibleMessages = _messages
                .OrderByDescending(m => m.CreatedAt)
                .Take(MaxVisibleMessages)
                .Reverse() // Oldest at top, newest at bottom
                .ToList();
        }

        if (visibleMessages.Count == 0)
            return;

        // Position: bottom-right, above HUD
        var style = ImGui.GetStyle();
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();

        // Calculate total height needed
        var totalHeight = visibleMessages.Count * lineHeight +
                          (visibleMessages.Count - 1) * MessageSpacing +
                          style.WindowPadding.Y * 2;

        // Find max width needed
        var maxWidth = 0f;
        foreach (var msg in visibleMessages)
        {
            var textWidth = ImGui.CalcTextSize(msg.Text).X;
            maxWidth = Math.Max(maxWidth, textWidth);
        }
        maxWidth += style.WindowPadding.X * 2;

        // Position window in top-right corner, below HudText1 if offset provided
        var windowPos = new Vector2(
            displaySize.X - maxWidth - EdgePadding,
            EdgePadding + topOffset
        );

        ImGui.SetNextWindowPos(windowPos);
        ImGui.SetNextWindowSize(new Vector2(maxWidth, totalHeight));

        var windowFlags = ImGuiWindowFlags.NoTitleBar |
                          ImGuiWindowFlags.NoResize |
                          ImGuiWindowFlags.NoMove |
                          ImGuiWindowFlags.NoScrollbar |
                          ImGuiWindowFlags.NoInputs |
                          ImGuiWindowFlags.NoFocusOnAppearing |
                          ImGuiWindowFlags.NoBringToFrontOnFocus |
                          ImGuiWindowFlags.NoBackground;

        if (ImGui.Begin("##MessageOverlay", windowFlags))
        {
            foreach (var msg in visibleMessages)
            {
                var alpha = msg.GetAlpha(now);
                var color = GetMessageColor(msg.Type, alpha);

                // Optional: Add slight background for readability
                var bgColor = new Vector4(0f, 0f, 0f, 0.5f * alpha);
                var textSize = ImGui.CalcTextSize(msg.Text);
                var cursorPos = ImGui.GetCursorScreenPos();

                // Draw background rect (DPI-scaled padding and rounding)
                var drawList = ImGui.GetWindowDrawList();
                var scale = UIConstants.DpiScale;
                drawList.AddRectFilled(
                    cursorPos - new Vector2(2 * scale, 1 * scale),
                    cursorPos + textSize + new Vector2(4 * scale, 2 * scale),
                    ImGui.ColorConvertFloat4ToU32(bgColor),
                    3f * scale // Corner rounding
                );

                ImGui.TextColored(color, msg.Text);
            }
        }
        ImGui.End();
    }

    /// <summary>
    /// Get color for message type with alpha.
    /// </summary>
    private static Vector4 GetMessageColor(MessageType type, float alpha) => type switch
    {
        MessageType.Info => new Vector4(1f, 1f, 1f, alpha),
        MessageType.Warning => new Vector4(1f, 0.9f, 0.2f, alpha),
        MessageType.Error => new Vector4(1f, 0.3f, 0.3f, alpha),
        MessageType.Combat => new Vector4(1f, 0.6f, 0.2f, alpha),
        MessageType.Quest => new Vector4(0.3f, 0.9f, 1f, alpha),
        MessageType.Loot => new Vector4(0.3f, 1f, 0.3f, alpha),
        MessageType.Dev => new Vector4(0.6f, 0.6f, 0.6f, alpha),
        _ => new Vector4(1f, 1f, 1f, alpha)
    };

    /// <summary>
    /// Clear all messages.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _messages.Clear();
        }
    }

    /// <summary>
    /// Get current message count.
    /// </summary>
    public int MessageCount
    {
        get
        {
            lock (_lock)
            {
                return _messages.Count;
            }
        }
    }
}
