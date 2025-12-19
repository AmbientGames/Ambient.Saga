using Ambient.Saga.Presentation.UI.ViewModels;

namespace Ambient.Saga.UI.Components.Modals.Adapters;

/// <summary>
/// Adapter for QuestModal to work with the Modal Registry Pattern.
/// </summary>
public class QuestModalAdapter : IModal
{
    private readonly QuestModal _modal;

    public QuestModalAdapter(MediatR.IMediator mediator)
    {
        _modal = new QuestModal(mediator ?? throw new ArgumentNullException(nameof(mediator)));
    }

    public string Name => "Quest";

    public bool CanOpen(object? context)
    {
        return context is QuestContext;
    }

    public void OnOpening(object? context)
    {
        if (context is QuestContext ctx)
        {
            System.Diagnostics.Debug.WriteLine($"[QuestModal] Opening for quest: {ctx.QuestRef}");
            _modal.Open(ctx.QuestRef, ctx.SagaRef, ctx.SignpostRef, ctx.ViewModel);
        }
    }

    public void Render(object? context, ref bool isOpen)
    {
        if (context is QuestContext ctx)
        {
            _modal.Render(ctx.ViewModel, ref isOpen);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[QuestModal] Invalid context, closing");
            isOpen = false;
        }
    }

    public void OnClosed()
    {
        System.Diagnostics.Debug.WriteLine("[QuestModal] Closed");
    }
}
