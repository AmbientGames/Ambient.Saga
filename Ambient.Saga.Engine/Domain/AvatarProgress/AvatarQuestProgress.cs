namespace Ambient.Saga.Engine.Domain.AvatarProgress;

public enum QuestProgressStatus
{
    Active,
    Completed,
    Failed,
    Abandoned
}

public class AvatarQuestProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AvatarId { get; set; }
    public string QuestRef { get; set; } = string.Empty;
    public QuestProgressStatus Status { get; set; }
    public string? CurrentStage { get; set; }
    public DateTime AcceptedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string OwnerSagaRef { get; set; } = string.Empty;
}
