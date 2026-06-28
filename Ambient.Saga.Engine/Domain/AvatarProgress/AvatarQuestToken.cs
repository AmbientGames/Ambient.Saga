namespace Ambient.Saga.Engine.Domain.AvatarProgress;

public class AvatarQuestToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AvatarId { get; set; }
    public string QuestTokenRef { get; set; } = string.Empty;
    public DateTime AwardedAt { get; set; }
    public string SourceSagaRef { get; set; } = string.Empty;
}
