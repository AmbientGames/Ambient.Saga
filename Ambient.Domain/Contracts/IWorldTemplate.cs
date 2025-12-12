namespace Ambient.Domain.Contracts;

public interface IWorldTemplate
{
    TemplateMetadata Metadata { get; set; }
    GameplayComponents Gameplay { get; set; }
}