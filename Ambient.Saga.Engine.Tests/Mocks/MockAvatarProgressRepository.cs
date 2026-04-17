using Ambient.Saga.Engine.Contracts.Persistence;
using Ambient.Saga.Engine.Domain.AvatarProgress;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Tests.Mocks;

public class MockAvatarProgressRepository : IAvatarProgressRepository
{
    private readonly HashSet<(Guid, string)> _questTokens = new();
    private readonly Dictionary<(Guid, string), QuestProgressStatus> _questProgress = new();
    private readonly Dictionary<(Guid, string), int> _bossDefeats = new();
    private readonly Dictionary<(Guid, string), int> _factionReputation = new();
    private readonly Dictionary<(Guid, string, string), int?> _characterTraits = new();

    public void ProjectTransactions(Guid avatarId, string sagaRef, IReadOnlyList<SagaTransaction> transactions) { }

    public bool HasQuestToken(Guid avatarId, string questTokenRef) => _questTokens.Contains((avatarId, questTokenRef));
    public HashSet<string> GetAllQuestTokens(Guid avatarId) => _questTokens.Where(t => t.Item1 == avatarId).Select(t => t.Item2).ToHashSet();

    public QuestProgressStatus? GetQuestStatus(Guid avatarId, string questRef) => _questProgress.GetValueOrDefault((avatarId, questRef));
    public AvatarQuestProgress? GetQuestProgress(Guid avatarId, string questRef) => null;

    public int GetBossDefeatedCount(Guid avatarId, string characterRef) => _bossDefeats.GetValueOrDefault((avatarId, characterRef));
    public int GetFactionReputation(Guid avatarId, string factionRef) => _factionReputation.GetValueOrDefault((avatarId, factionRef));
    public int? GetCharacterTraitValue(Guid avatarId, string characterRef, string traitType) => _characterTraits.GetValueOrDefault((avatarId, characterRef, traitType));

    public void SetQuestToken(Guid avatarId, string tokenRef) => _questTokens.Add((avatarId, tokenRef));
    public void SetFactionReputation(Guid avatarId, string factionRef, int value) => _factionReputation[(avatarId, factionRef)] = value;
    public void SetBossDefeatedCount(Guid avatarId, string characterRef, int count) => _bossDefeats[(avatarId, characterRef)] = count;
    public void SetQuestStatus(Guid avatarId, string questRef, QuestProgressStatus status) => _questProgress[(avatarId, questRef)] = status;
    public void SetCharacterTrait(Guid avatarId, string characterRef, string traitType, int? value) => _characterTraits[(avatarId, characterRef, traitType)] = value;
}
