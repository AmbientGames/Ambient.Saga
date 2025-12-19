using ImGuiNET;

namespace Ambient.Saga.UI.Components.Modals.Examples;

/// <summary>
/// Example of a simple modal using the IModal interface and registry pattern.
/// This demonstrates the new extensible modal system.
/// </summary>
/// <remarks>
/// Usage:
/// 1. Register the modal: modalManager.RegisterModal(new SimpleModalExample());
/// 2. Open with context: modalManager.OpenRegisteredModal("SimpleExample", "Hello World");
/// 3. The modal renders automatically via ModalRegistry
/// </remarks>
public class SimpleModalExample : IModal
{
    private string? _displayMessage;

    public string Name => "SimpleExample";

    public bool CanOpen(object? context)
    {
        // Optional validation - only allow string contexts
        return context is string or null;
    }

    public void OnOpening(object? context)
    {
        // Initialize state from context
        _displayMessage = context as string ?? "No message provided";
        Console.WriteLine($"[SimpleModalExample] Opening with message: {_displayMessage}");
    }

    public void Render(object? context, ref bool isOpen)
    {
        // Render the modal using ImGui
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(400, 200), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Simple Modal Example", ref isOpen, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.TextWrapped($"Message: {_displayMessage}");
            ImGui.Separator();

            if (ImGui.Button("Close", new System.Numerics.Vector2(-1, 30)))
            {
                isOpen = false;
            }
        }

        ImGui.End();
    }

    public void OnClosed()
    {
        // Cleanup when modal closes
        Console.WriteLine($"[SimpleModalExample] Closed");
        _displayMessage = null;
    }

    public void OnObscured()
    {
        // Called when another modal opens on top
        Console.WriteLine($"[SimpleModalExample] Obscured by another modal");
    }

    public void OnRevealed()
    {
        // Called when obscuring modal closes
        Console.WriteLine($"[SimpleModalExample] Revealed - back on top");
    }
}
