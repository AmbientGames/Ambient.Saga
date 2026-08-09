namespace Ambient.Rpg.Engine.Domain.AvatarProgress;

public class AvatarBossDefeat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AvatarId { get; set; }
    public string CharacterRef { get; set; } = string.Empty;
    public int DefeatedCount { get; set; }
    public DateTime LastDefeatedAt { get; set; }
}
