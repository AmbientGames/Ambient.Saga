using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for SpawnDevCharacterCommand.
/// Creates CharacterSpawned transaction for dev testing.
/// </summary>
internal sealed class SpawnDevCharacterHandler : IRequestHandler<SpawnDevCharacterCommand, SpawnDevCharacterResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IWorld _world;

    public SpawnDevCharacterHandler(
        IArcInstanceRepository instanceRepository,
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

            // Use a real arc ref so the dialogue system can find the template
            // Each dev spawn uses a unique arc ref suffix so they have independent state
            var baseArcRef = command.ArcRef;
            if (string.IsNullOrEmpty(baseArcRef) || !_world.ArcLookup.ContainsKey(baseArcRef))
            {
                var firstArc = _world.Gameplay?.Saga?.FirstOrDefault();
                if (firstArc == null)
                {
                    return SpawnDevCharacterResult.Failure("No arcs found in world");
                }
                baseArcRef = firstArc.RefName;
            }

            // Create unique arc instance per dev character (append unique suffix)
            // The dialogue system will use this unique ref for state, but template lookups
            // will be handled by stripping the DEV suffix
            var uniqueArcRef = $"{baseArcRef}__DEV__{Guid.NewGuid():N}";

            // Get or create unique arc instance for this dev character
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, uniqueArcRef, ct);

            // Create CharacterSpawned transaction
            var characterInstanceId = Guid.NewGuid();
            var spawnTransaction = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.CharacterSpawned,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    [TransactionDataKeys.CharacterInstanceId] = characterInstanceId.ToString(),
                    [TransactionDataKeys.CharacterRef] = command.CharacterRef,
                    [TransactionDataKeys.ArcTriggerRef] = "DEV_TRIGGER",
                    [TransactionDataKeys.X] = "0",
                    [TransactionDataKeys.Z] = "0",
                    [TransactionDataKeys.SpawnHeight] = "0",
                    [TransactionDataKeys.IsDevSpawn] = "true"
                }
            };

            instance.AddTransaction(spawnTransaction);

            // Persist and commit transaction
            var (_, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                new List<ArcTransaction> { spawnTransaction },
                ct);

            if (!committed)
            {
                return SpawnDevCharacterResult.Failure("Failed to commit spawn transaction");
            }

            System.Diagnostics.Debug.WriteLine($"[DevSpawn] Created CharacterSpawned transaction for {command.CharacterRef} with InstanceId {characterInstanceId} in arc {uniqueArcRef}");

            return SpawnDevCharacterResult.Success(characterInstanceId, uniqueArcRef);
        }
        catch (Exception ex)
        {
            return SpawnDevCharacterResult.Failure($"Error spawning dev character: {ex.Message}");
        }
    }
}
