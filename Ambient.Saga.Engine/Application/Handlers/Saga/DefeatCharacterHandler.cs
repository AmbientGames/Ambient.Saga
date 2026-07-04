using MediatR;
using Ambient.Domain;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for DefeatCharacterCommand.
/// Creates CharacterDefeated transaction.
/// </summary>
internal sealed class DefeatCharacterHandler : IRequestHandler<DefeatCharacterCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public DefeatCharacterHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(DefeatCharacterCommand command, CancellationToken ct)
    {
        try
        {
            // Verify Saga template exists
            if (!_world.SagaArcLookup.ContainsKey(command.SagaArcRef))
            {
                return SagaCommandResult.Failure(Guid.Empty, $"Saga '{command.SagaArcRef}' not found");
            }

            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Validate character exists by checking transactions
            var characterExists = instance.GetCommittedTransactions()
                .Any(t => t.Type == SagaTransactionType.CharacterSpawned &&
                         t.Data.ContainsKey(TransactionDataKeys.CharacterInstanceId) &&
                         t.Data[TransactionDataKeys.CharacterInstanceId] == command.CharacterInstanceId.ToString());

            if (!characterExists)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Character with instance ID '{command.CharacterInstanceId}' not found");
            }

            // Already-defeated guard: battle end and quest hooks can both report the
            // same kill — a duplicate CharacterDefeated would double kill credit and
            // re-award defeat tokens. No-op succeed without writing new transactions.
            var alreadyDefeated = instance.GetCommittedTransactions()
                .Any(t => t.Type == SagaTransactionType.CharacterDefeated &&
                          t.Data.TryGetValue(TransactionDataKeys.CharacterInstanceId, out var defeatedId) &&
                          defeatedId == command.CharacterInstanceId.ToString());

            if (alreadyDefeated)
            {
                System.Diagnostics.Debug.WriteLine($"[DefeatCharacter] Character {command.CharacterInstanceId} already defeated - skipping duplicate");
                return SagaCommandResult.Success(instance.InstanceId, new List<Guid>(), 0);
            }

            // Resolve the character template — its ref/tags/traits enrich the transaction
            // for quest objective evaluation, and GivesQuestTokenOnDefeat is read below
            var characterRef = instance.GetCommittedTransactions()
                .Where(t => t.Type == SagaTransactionType.CharacterSpawned &&
                           t.Data.TryGetValue(TransactionDataKeys.CharacterInstanceId, out var id) &&
                           id == command.CharacterInstanceId.ToString())
                .Select(t => t.Data.GetValueOrDefault(TransactionDataKeys.CharacterRef))
                .FirstOrDefault();

            Character? characterTemplate = null;
            if (characterRef != null)
                _world.CharactersLookup.TryGetValue(characterRef, out characterTemplate);

            // Create CharacterDefeated transaction
            var transactionData = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterInstanceId] = command.CharacterInstanceId.ToString(),
                [TransactionDataKeys.VictorAvatarId] = command.AvatarId.ToString(),
                [TransactionDataKeys.DefeatMethod] = command.DefeatMethod ?? "Unknown"
            };

            if (!string.IsNullOrEmpty(characterRef))
                transactionData[TransactionDataKeys.CharacterRef] = characterRef;

            // Tags plus boolean trait names — both vocabularies are used by
            // CharactersDefeatedByTag quest objectives (e.g. "BanditScout", "hostile")
            var tagVocabulary = (characterTemplate?.Tags ?? Array.Empty<string>())
                .Concat((characterTemplate?.Traits ?? Array.Empty<CharacterTrait>()).Select(tr => tr.Name.ToString()))
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct()
                .ToList();
            if (tagVocabulary.Count > 0)
                transactionData[TransactionDataKeys.CharacterTag] = string.Join(",", tagVocabulary);

            var transaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.CharacterDefeated,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = transactionData
            };

            instance.AddTransaction(transaction);

            var transactions = new List<SagaTransaction> { transaction };

            // Award quest tokens declared on the character template (GivesQuestTokenOnDefeat)
            if (characterRef != null && characterTemplate?.GivesQuestTokenOnDefeat != null)
            {
                foreach (var tokenRef in characterTemplate.GivesQuestTokenOnDefeat)
                {
                    if (string.IsNullOrEmpty(tokenRef)) continue;
                    var tokenTx = new SagaTransaction
                    {
                        TransactionId = Guid.NewGuid(),
                        Type = SagaTransactionType.QuestTokenAwarded,
                        AvatarId = command.AvatarId.ToString(),
                        Status = TransactionStatus.Pending,
                        LocalTimestamp = DateTime.UtcNow,
                        Data = new Dictionary<string, string>
                        {
                            [TransactionDataKeys.QuestTokenRef] = tokenRef,
                            [TransactionDataKeys.Reason] = $"Defeated {characterRef}"
                        }
                    };
                    instance.AddTransaction(tokenTx);
                    transactions.Add(tokenTx);
                }
            }

            // Persist and commit transaction atomically
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                transactions,
                ct);

            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            return SagaCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error defeating character: {ex.Message}");
        }
    }
}
