using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for SpawnDevCharacterCommand.
/// Creates CharacterSpawned transaction for dev testing.
/// </summary>
internal sealed class SpawnDevCharacterHandler : IRequestHandler<SpawnDevCharacterCommand, SpawnDevCharacterResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly IWorld _world;

    public SpawnDevCharacterHandler(
        ISagaInstanceRepository instanceRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _world = world;
    }

    public async Task<SpawnDevCharacterResult> Handle(SpawnDevCharacterCommand command, CancellationToken ct)
    {
        try
        {
            // Verify character template exists
            if (!_world.CharactersLookup.TryGetValue(command.CharacterRef, out var characterTemplate))
            {
                return SpawnDevCharacterResult.Failure($"Character template '{command.CharacterRef}' not found");
            }

            // Use a real saga ref so the dialogue system can find the template
            // Each dev spawn uses a unique saga ref suffix so they have independent state
            var baseSagaRef = command.SagaArcRef;
            if (string.IsNullOrEmpty(baseSagaRef) || !_world.SagaArcLookup.ContainsKey(baseSagaRef))
            {
                var firstSaga = _world.Gameplay?.SagaArcs?.FirstOrDefault();
                if (firstSaga == null)
                {
                    return SpawnDevCharacterResult.Failure("No sagas found in world");
                }
                baseSagaRef = firstSaga.RefName;
            }

            // Create unique saga instance per dev character (append unique suffix)
            // The dialogue system will use this unique ref for state, but template lookups
            // will be handled by stripping the DEV suffix
            var uniqueSagaRef = $"{baseSagaRef}__DEV__{Guid.NewGuid():N}";

            // Get or create unique saga instance for this dev character
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, uniqueSagaRef, ct);

            // Create CharacterSpawned transaction
            var characterInstanceId = Guid.NewGuid();
            var spawnTransaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.CharacterSpawned,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["CharacterInstanceId"] = characterInstanceId.ToString(),
                    ["CharacterRef"] = command.CharacterRef,
                    ["SagaTriggerRef"] = "DEV_TRIGGER",
                    ["X"] = "0",
                    ["Z"] = "0",
                    ["SpawnHeight"] = "0",
                    ["IsDevSpawn"] = "true"
                }
            };

            instance.AddTransaction(spawnTransaction);

            // Persist transaction
            await _instanceRepository.AddTransactionsAsync(
                instance.InstanceId,
                new List<SagaTransaction> { spawnTransaction },
                ct);

            // Commit transaction
            var committed = await _instanceRepository.CommitTransactionsAsync(
                instance.InstanceId,
                new List<Guid> { spawnTransaction.TransactionId },
                ct);

            if (!committed)
            {
                return SpawnDevCharacterResult.Failure("Failed to commit spawn transaction");
            }

            System.Diagnostics.Debug.WriteLine($"[DevSpawn] Created CharacterSpawned transaction for {command.CharacterRef} with InstanceId {characterInstanceId} in saga {uniqueSagaRef}");

            return SpawnDevCharacterResult.Success(characterInstanceId, uniqueSagaRef);
        }
        catch (Exception ex)
        {
            return SpawnDevCharacterResult.Failure($"Error spawning dev character: {ex.Message}");
        }
    }
}
