using Ambient.Saga.Presentation.UI.ViewModels;

namespace Ambient.Saga.UI.Components.Modals.Adapters;

/// <summary>
/// Adapter for QuestDetailModal to work with the Modal Registry Pattern.
/// </summary>
public class QuestDetailModalAdapter : IModal
{
    private readonly QuestDetailModal _modal;
    private bool _initialized;

    public QuestDetailModalAdapter(MediatR.IMediator mediator)
    {
        _modal = new QuestDetailModal(mediator ?? throw new ArgumentNullException(nameof(mediator)));
    }

    public string Name => "QuestDetail";

    public bool CanOpen(object? context)
    {
        return context is QuestDetailContext;
    }

    public void OnOpening(object? context)
    {
        if (context is QuestDetailContext ctx)
        {
            System.Diagnostics.Debug.WriteLine($"[QuestDetailModal] Opening for quest: {ctx.QuestRef}");
            // Fire and forget async initialization
            _ = InitializeAsync(ctx);
        }
    }

    private async Task InitializeAsync(QuestDetailContext context)
    {
        try
        {
            await _modal.OpenAsync(context.QuestRef, context.SagaRef, context.ViewModel);
            _initialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QuestDetailModal] Failed to initialize: {ex.Message}");
            _initialized = false;
        }
    }

    public void Render(object? context, ref bool isOpen)
    {
        if (!_initialized)
        {
            // Show loading state
            ImGuiNET.ImGui.Begin("Quest Details", ref isOpen);
            ImGuiNET.ImGui.Text("Loading quest details...");
            ImGuiNET.ImGui.End();
            return;
        }

        if (context is QuestDetailContext ctx)
        {
            _modal.Render(ctx.ViewModel, ref isOpen);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[QuestDetailModal] Invalid context, closing");
            isOpen = false;
        }
    }

    public void OnClosed()
    {
        System.Diagnostics.Debug.WriteLine("[QuestDetailModal] Closed");
        _initialized = false;
    }
}
