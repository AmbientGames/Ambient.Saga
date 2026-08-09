using MediatR;
using Ambient.Domain;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for DefeatCharacterCommand.
/// Creates CharacterDefeated transaction.
/// </summary>
internal sealed class DefeatCharacterHandler : IRequestHandler<DefeatCharacterCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public DefeatCharacterHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<ArcCommandResult> Handle(DefeatCharacterCommand command, CancellationToken ct)
    {
        try
        {
            // Verify Arc template exists
            if (!_world.ArcLookup.ContainsKey(command.ArcRef))
            {
                return ArcCommandResult.Failure(Guid.Empty, $"Arc '{command.ArcRef}' not found");
            }

            // Get Arc instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.ArcRef, ct);

            // Validate character exists by checking transactions
            var characterExists = instance.GetCommittedTransactions()
                .Any(t => t.Type == ArcTransactionType.CharacterSpawned &&
                         t.Data.ContainsKey(TransactionDataKeys.CharacterInstanceId) &&
                         t.Data[TransactionDataKeys.CharacterInstanceId] == command.CharacterInstanceId.ToString());

            if (!characterExists)
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Character with instance ID '{command.CharacterInstanceId}' not found");
            }

            // Already-defeated guard: battle end and quest hooks can both report the
            // same kill — a duplicate CharacterDefeated would double kill credit and
            // re-award defeat tokens. No-op succeed without writing new transactions.
            var alreadyDefeated = instance.GetCommittedTransactions()
                .Any(t => t.Type == ArcTransactionType.CharacterDefeated &&
                          t.Data.TryGetValue(TransactionDataKeys.CharacterInstanceId, out var defeatedId) &&
                          defeatedId == command.CharacterInstanceId.ToString());

            if (alreadyDefeated)
            {
                System.Diagnostics.Debug.WriteLine($"[DefeatCharacter] Character {command.CharacterInstanceId} already defeated - skipping duplicate");
                return ArcCommandResult.Success(instance.InstanceId, new List<Guid>(), 0);
            }

            // Resolve the character template — its ref/tags/traits enrich the transaction
            // for quest objective evaluation, and GivesQuestTokenOnDefeat is read below
            var characterRef = instance.GetCommittedTransactions()
                .Where(t => t.Type == ArcTransactionType.CharacterSpawned &&
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

            var transaction = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.CharacterDefeated,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = transactionData
            };

            instance.AddTransaction(transaction);

            var transactions = new List<ArcTransaction> { transaction };

            // Award quest tokens declared on the character template
            // (GivesQuestTokenOnDefeat) — single shared implementation used by all
            // CharacterDefeated producers (battle victory, scripted dialogue victory,
            // and this explicit command).
            if (characterTemplate != null)
            {
                BattleEndTransactionFactory.AppendDefeatTokenGrants(
                    command.AvatarId, command.CharacterInstanceId, characterTemplate, instance, transactions);
            }

            // Persist and commit transaction atomically
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                transactions,
                ct);

            if (!committed)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);

            return ArcCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            return ArcCommandResult.Failure(Guid.Empty, $"Error defeating character: {ex.Message}");
        }
    }
}
