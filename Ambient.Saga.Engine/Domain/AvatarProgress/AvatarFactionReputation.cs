namespace Ambient.Saga.Engine.Domain.AvatarProgress;

public class AvatarFactionReputation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AvatarId { get; set; }
    public string FactionRef { get; set; } = string.Empty;
    public int ReputationValue { get; set; }
    public DateTime LastChangedAt { get; set; }
}
