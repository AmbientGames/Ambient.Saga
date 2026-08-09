namespace Ambient.Rpg.Engine.Domain.AvatarProgress;

public class AvatarCharacterTrait
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AvatarId { get; set; }
    public string CharacterRef { get; set; } = string.Empty;
    public string TraitType { get; set; } = string.Empty;
    public int? TraitValue { get; set; }
    public DateTime AssignedAt { get; set; }
}
