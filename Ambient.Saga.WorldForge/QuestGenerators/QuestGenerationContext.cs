using Ambient.Domain;
using Ambient.Saga.WorldForge;

namespace Ambient.Saga.WorldForge.QuestGenerators;

/// <summary>
/// Context object containing all dependencies needed for quest generation.
/// Reduces parameter lists and makes dependencies explicit.
/// </summary>
public class QuestGenerationContext
{
    public RefNameGenerator RefNameGenerator { get; }
    public QuestRewardFactory RewardFactory { get; }
    public ThemeItemResolver ItemResolver { get; }
    public Random Random { get; }

    public QuestGenerationContext(
        RefNameGenerator refNameGenerator,
        QuestRewardFactory rewardFactory,
        ThemeItemResolver itemResolver,
        Random random)
    {
        RefNameGenerator = refNameGenerator;
        RewardFactory = rewardFactory;
        ItemResolver = itemResolver;
        Random = random;
    }
}
