using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Domain.Rpg.Sagas;

/// <summary>
/// Domain service for Saga interactions.
/// Handles avatar position updates and trigger activation logic.
///
/// Key Principles:
/// - Works in Saga-relative coordinates (Saga center is origin)
/// - All trigger checks and spawn calculations use Saga-relative coords
/// - Generates deterministic spawn positions using stored seed
/// - Creates transactions for state changes
/// - Provides query methods for "peek without triggering"
/// </summary>
public class SagaInteractionService
{
    private readonly SagaArc _template;
    private readonly List<SagaTrigger> _expandedSagaTriggers;
    private readonly World _world;
    private readonly SagaStateMachine _stateMachine;

    public SagaInteractionService(
        SagaArc template,
        List<SagaTrigger> expandedSagaTriggers,
        World world)
    {
        _template = template ?? throw new ArgumentNullException(nameof(template));
        _expandedSagaTriggers = expandedSagaTriggers ?? throw new ArgumentNullException(nameof(expandedSagaTriggers));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _stateMachine = new SagaStateMachine(template, expandedSagaTriggers, world);
    }

    #region Query Methods (Non-mutating)

    /// <summary>
    /// Gets the innermost trigger that would be activated at the given position.
    /// Does not modify state - this is a "peek" operation.
    /// </summary>
    /// <param name="instance">Saga instance to check state</param>
    /// <param name="avatarX">Avatar X position in Saga-relative coordinates</param>
    /// <param name="avatarZ">Avatar Z position in Saga-relative coordinates</param>
    /// <param name="avatar">Avatar with quest tokens and other data</param>
    /// <returns>The innermost trigger that would activate, or null if none</returns>
    public SagaTrigger? GetSagaTriggerAtPosition(
        SagaInstance instance,
        double avatarX,
        double avatarZ,
        AvatarBase avatar)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));

        if (avatar == null)
            throw new ArgumentNullException(nameof(avatar));

        var currentState = _stateMachine.ReplayToNow(instance);

        SagaTrigger? innermostSagaTrigger = null;
        var smallestRadius = double.MaxValue;

        foreach (var trigger in _expandedSagaTriggers)
        {
            // Skip if trigger already completed
            if (currentState.Triggers.TryGetValue(trigger.RefName, out var triggerState)
                && triggerState.Status == SagaTriggerStatus.Completed)
            {
                continue;
            }

            // Check proximity
            var distanceFromCenter = Math.Sqrt(avatarX * avatarX + avatarZ * avatarZ);
            var isWithinRadius = distanceFromCenter <= trigger.EnterRadius;

            if (!isWithinRadius)
                continue;

            // Check quest token requirements
            if (!TriggerAvailabilityChecker.CanActivate(trigger, avatar))
                continue;

            // Keep track of smallest (innermost) trigger
            if (trigger.EnterRadius < smallestRadius)
            {
                smallestRadius = trigger.EnterRadius;
                innermostSagaTrigger = trigger;
            }
        }

        return innermostSagaTrigger;
    }

    /// <summary>
    /// Gets all triggers within range at the given position.
    /// Does not check quest token requirements - returns all triggers that are geometrically active.
    /// </summary>
    /// <param name="instance">Saga instance to check state</param>
    /// <param name="avatarX">Avatar X position in Saga-relative coordinates</param>
    /// <param name="avatarZ">Avatar Z position in Saga-relative coordinates</param>
    /// <returns>List of triggers within range, sorted from outermost to innermost</returns>
    public List<SagaTriggerProximityInfo> GetTriggersAtPosition(
        SagaInstance instance,
        double avatarX,
        double avatarZ)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));

        var currentState = _stateMachine.ReplayToNow(instance);
        var results = new List<SagaTriggerProximityInfo>();
        var distanceFromCenter = Math.Sqrt(avatarX * avatarX + avatarZ * avatarZ);

        foreach (var trigger in _expandedSagaTriggers)
        {
            var isWithinRadius = distanceFromCenter <= trigger.EnterRadius;

            // Get trigger state
            var triggerState = currentState.Triggers.TryGetValue(trigger.RefName, out var state)
                ? state
                : null;

            results.Add(new SagaTriggerProximityInfo
            {
                SagaTrigger = trigger,
                DistanceFromCenter = distanceFromCenter,
                IsWithinRadius = isWithinRadius,
                TriggerStatus = triggerState?.Status ?? SagaTriggerStatus.Inactive,
                IsCompleted = triggerState?.Status == SagaTriggerStatus.Completed
            });
        }

        // Sort outermost to innermost
        return results.OrderByDescending(t => t.SagaTrigger.EnterRadius).ToList();
    }

    /// <summary>
    /// Checks if a specific trigger can be activated by the avatar at the given position.
    /// This is a comprehensive check including proximity, quest tokens, and completion status.
    /// </summary>
    /// <param name="instance">Saga instance to check state</param>
    /// <param name="sagaTrigger">The trigger to check</param>
    /// <param name="avatarX">Avatar X position in Saga-relative coordinates</param>
    /// <param name="avatarZ">Avatar Z position in Saga-relative coordinates</param>
    /// <param name="avatar">Avatar with quest tokens and other data</param>
    /// <returns>Result indicating whether trigger can activate and why/why not</returns>
    public SagaTriggerActivationCheck CanActivateSagaTrigger(
        SagaInstance instance,
        SagaTrigger sagaTrigger,
        double avatarX,
        double avatarZ,
        AvatarBase avatar)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));

        if (sagaTrigger == null)
            throw new ArgumentNullException(nameof(sagaTrigger));

        if (avatar == null)
            throw new ArgumentNullException(nameof(avatar));

        var result = new SagaTriggerActivationCheck
        {
            SagaTrigger = sagaTrigger,
            CanActivate = false
        };

        // Get current state
        var currentState = _stateMachine.ReplayToNow(instance);

        // Check if already completed
        if (currentState.Triggers.TryGetValue(sagaTrigger.RefName, out var triggerState)
            && triggerState.Status == SagaTriggerStatus.Completed)
        {
            result.BlockedReason = "Trigger already completed";
            return result;
        }

        // Check proximity
        var distanceFromCenter = Math.Sqrt(avatarX * avatarX + avatarZ * avatarZ);
        result.DistanceFromCenter = distanceFromCenter;
        result.IsWithinRadius = distanceFromCenter <= sagaTrigger.EnterRadius;

        if (!result.IsWithinRadius)
        {
            result.BlockedReason = $"Avatar outside trigger radius (distance: {distanceFromCenter:F2}m, radius: {sagaTrigger.EnterRadius:F2}m)";
            return result;
        }

        // Check quest token requirements
        result.HasRequiredQuestTokens = TriggerAvailabilityChecker.CanActivate(sagaTrigger, avatar);

        if (!result.HasRequiredQuestTokens)
        {
            var missingTokens = TriggerAvailabilityChecker.GetMissingQuestTokens(sagaTrigger, avatar);
            result.MissingQuestTokens = missingTokens;
            result.BlockedReason = $"Missing quest tokens: {string.Join(", ", missingTokens)}";
            return result;
        }

        // All checks passed
        result.CanActivate = true;
        return result;
    }

    /// <summary>
    /// Checks if an avatar can interact with a Saga feature (loot chest, landmark, quest marker).
    /// This is a comprehensive check including quest tokens, max interactions, and cooldowns.
    /// </summary>
    /// <param name="instance">Saga instance to check state</param>
    /// <param name="feature">The feature to check</param>
    /// <param name="avatar">Avatar attempting to interact</param>
    /// <returns>Result indicating whether interaction is allowed and why/why not</returns>
    public FeatureInteractionCheck CanInteractWithFeature(
        SagaInstance instance,
        SagaFeature feature,
        AvatarBase avatar)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));

        if (feature == null)
            throw new ArgumentNullException(nameof(feature));

        if (avatar == null)
            throw new ArgumentNullException(nameof(avatar));

        var result = new FeatureInteractionCheck
        {
            Feature = feature,
            CanInteract = false
        };

        // If no interactable defined, feature cannot be interacted with
        if (feature.Interactable == null)
        {
            result.BlockedReason = "Feature has no Interactable defined";
            return result;
        }

        var interactable = feature.Interactable;

        // Get current state by replaying transactions
        var currentState = _stateMachine.ReplayToNow(instance);

        // Get feature interaction state (if any)
        var featureState = currentState.FeatureInteractions.TryGetValue(feature.RefName, out var state)
            ? state
            : null;

        var avatarState = featureState?.AvatarInteractions.TryGetValue(avatar.AvatarId.ToString(), out var aState) == true
            ? aState
            : null;

        // Check quest token requirements
        if (interactable.RequiresQuestTokenRef != null && interactable.RequiresQuestTokenRef.Length > 0)
        {
            var hasAllTokens = true;
            var missingTokens = new List<string>();

            foreach (var requiredTokenRef in interactable.RequiresQuestTokenRef)
            {
                var hasToken = avatar.Capabilities?.QuestTokens != null &&
                    Array.Exists(avatar.Capabilities.QuestTokens, qt => qt.QuestTokenRef == requiredTokenRef);

                if (!hasToken)
                {
                    hasAllTokens = false;
                    missingTokens.Add(requiredTokenRef);
                }
            }

            result.HasRequiredQuestTokens = hasAllTokens;
            result.MissingQuestTokens = missingTokens.ToArray();

            if (!hasAllTokens)
            {
                result.BlockedReason = $"Missing quest tokens: {string.Join(", ", missingTokens)}";
                return result;
            }
        }
        else
        {
            result.HasRequiredQuestTokens = true;
        }

        // Check MaxInteractions limit (if set)
        if (interactable.MaxInteractions > 0)
        {
            var currentCount = avatarState?.InteractionCount ?? 0;
            result.CurrentInteractionCount = currentCount;
            result.MaxInteractionsReached = currentCount >= interactable.MaxInteractions;

            if (result.MaxInteractionsReached)
            {
                result.BlockedReason = $"Max interactions reached ({currentCount}/{interactable.MaxInteractions})";
                return result;
            }
        }

        // Note: Cooldown checking removed from domain service
        // Cooldowns are a game concern - game should check LastInteractedAt from state
        // Domain just records interactions and enforces quest tokens + max interactions

        // All checks passed
        result.CanInteract = true;
        return result;
    }

    #endregion

    #region Command Methods (Mutating)

    /// <summary>
    /// Updates Saga with avatar's current position and checks for trigger activations.
    /// This is the main entry point from the game engine.
    /// </summary>
    /// <param name="instance">Saga instance to update</param>
    /// <param name="avatarX">Avatar X position in Saga-relative coordinates</param>
    /// <param name="avatarZ">Avatar Z position in Saga-relative coordinates</param>
    /// <param name="avatar">Avatar with quest tokens and other data</param>
    public void UpdateWithAvatarPosition(
        SagaInstance instance,
        double avatarX,
        double avatarZ,
        AvatarBase avatar)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));

        if (avatar == null)
            throw new ArgumentNullException(nameof(avatar));

        // Get current state by replaying transactions
        var currentState = _stateMachine.ReplayToNow(instance);

        // Calculate distance from Saga center (used for both enter and exit checks)
        var distanceFromCenter = Math.Sqrt(avatarX * avatarX + avatarZ * avatarZ);

        // PHASE 1: Check for trigger exits (process before enters to avoid state conflicts)
        foreach (var sagaTrigger in _expandedSagaTriggers)
        {
            // Only check triggers that are currently Active (not Inactive or Completed)
            if (!currentState.Triggers.TryGetValue(sagaTrigger.RefName, out var triggerState)
                || triggerState.Status != SagaTriggerStatus.Active)
            {
                continue;
            }

            // Calculate exit radius with hysteresis
            var exitRadius = TriggerProximityChecker.GetExitRadius(sagaTrigger.EnterRadius);
            var isOutsideExitRadius = distanceFromCenter > exitRadius;

            if (isOutsideExitRadius)
            {
                // Player has exited the trigger zone - create exit transaction
                var exitTx = new SagaTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    Type = SagaTransactionType.PlayerExited,
                    AvatarId = avatar.AvatarId.ToString(),
                    Status = TransactionStatus.Pending,
                    LocalTimestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, string>
                    {
                        ["TriggerRef"] = sagaTrigger.RefName,
                        ["DistanceMeters"] = distanceFromCenter.ToString("F2"),
                        ["ExitRadius"] = exitRadius.ToString("F2")
                    }
                };
                instance.AddTransaction(exitTx);

                // Despawn any living characters spawned by this trigger
                foreach (var character in currentState.Characters.Values)
                {
                    if (character.SpawnedByTriggerRef == sagaTrigger.RefName &&
                        character.IsAlive &&
                        character.IsSpawned)
                    {
                        var despawnTx = new SagaTransaction
                        {
                            TransactionId = Guid.NewGuid(),
                            Type = SagaTransactionType.CharacterDespawned,
                            AvatarId = avatar.AvatarId.ToString(),
                            Status = TransactionStatus.Pending,
                            LocalTimestamp = DateTime.UtcNow,
                            Data = new Dictionary<string, string>
                            {
                                ["CharacterInstanceId"] = character.CharacterInstanceId.ToString(),
                                ["CharacterRef"] = character.CharacterRef,
                                ["Reason"] = "Player exited trigger zone",
                                ["TriggerRef"] = sagaTrigger.RefName
                            }
                        };
                        instance.AddTransaction(despawnTx);
                    }
                }
            }
        }

        // PHASE 2: Check for trigger activations (enters)
        foreach (var sagaTrigger in _expandedSagaTriggers)
        {
            // Skip if trigger already active or completed (prevents duplicate character spawning)
            if (currentState.Triggers.TryGetValue(sagaTrigger.RefName, out var triggerState)
                && (triggerState.Status == SagaTriggerStatus.Active || triggerState.Status == SagaTriggerStatus.Completed))
            {
                continue;
            }

            // Check proximity (in Saga-relative coords, Saga center is at 0,0)
            var isWithinEnterRadius = distanceFromCenter <= sagaTrigger.EnterRadius;

            if (!isWithinEnterRadius)
                continue;

            // Check quest token requirements
            if (!TriggerAvailabilityChecker.CanActivate(sagaTrigger, avatar))
                continue;

            // Trigger activated! Create entry transaction first
            var enterTx = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.PlayerEntered,
                AvatarId = avatar.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["TriggerRef"] = sagaTrigger.RefName,
                    ["DistanceMeters"] = distanceFromCenter.ToString("F2"),
                    ["EnterRadius"] = sagaTrigger.EnterRadius.ToString("F2")
                }
            };
            instance.AddTransaction(enterTx);

            // Then activate trigger and spawn characters
            ActivateSagaTrigger(instance, sagaTrigger, avatarX, avatarZ, avatar.AvatarId.ToString());
        }
    }

    /// <summary>
    /// Interacts with a Saga feature (loot chest, landmark, quest marker).
    /// Creates transactions for the interaction, loot awards, and quest tokens.
    ///
    /// IMPORTANT: This method validates interaction requirements before creating transactions.
    /// If validation fails, throws InvalidOperationException with the reason.
    /// Callers should use CanInteractWithFeature() first to check eligibility.
    /// </summary>
    /// <param name="instance">Saga instance</param>
    /// <param name="feature">The feature to interact with</param>
    /// <param name="avatar">Avatar performing the interaction</param>
    /// <exception cref="InvalidOperationException">Thrown when interaction is not allowed</exception>
    public void InteractWithFeature(
        SagaInstance instance,
        SagaFeature feature,
        AvatarBase avatar)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));

        if (feature == null)
            throw new ArgumentNullException(nameof(feature));

        if (avatar == null)
            throw new ArgumentNullException(nameof(avatar));

        var avatarId = avatar.AvatarId.ToString();
        var interactable = feature.Interactable;

        if (interactable == null)
            throw new InvalidOperationException("Feature has no Interactable defined");

        // Validate interaction is allowed
        var check = CanInteractWithFeature(instance, feature, avatar);
        if (!check.CanInteract)
        {
            throw new InvalidOperationException($"Cannot interact with feature '{feature.RefName}': {check.BlockedReason}");
        }

        // Create EntityInteracted transaction
        var interactionTx = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.EntityInteracted,
            AvatarId = avatarId,
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["FeatureRef"] = feature.RefName,
                ["FeatureType"] = feature.GetType().Name // Structure, Landmark, QuestSignpost
            }
        };

        instance.AddTransaction(interactionTx);

        // Award loot if feature has any
        if (interactable.Loot != null)
        {
            var lootTx = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.LootAwarded,
                AvatarId = avatarId,
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["FeatureRef"] = feature.RefName,
                    ["LootSource"] = $"Feature '{feature.RefName}' interaction"
                }
            };

            instance.AddTransaction(lootTx);

            // Apply loot to avatar immediately (for sandbox/single-player)
            ApplyLootToAvatar(avatar, interactable.Loot);
        }

        // Apply stat effects if feature has any
        if (interactable.Effects != null)
        {
            ApplyEffectsToAvatar(avatar, interactable.Effects);
        }

        // Award quest tokens if feature gives any
        if (interactable.GivesQuestTokenRef != null && interactable.GivesQuestTokenRef.Length > 0)
        {
            foreach (var questTokenRef in interactable.GivesQuestTokenRef)
            {
                var questTokenTx = new SagaTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    Type = SagaTransactionType.QuestTokenAwarded,
                    AvatarId = avatarId,
                    Status = TransactionStatus.Pending,
                    LocalTimestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, string>
                    {
                        ["QuestTokenRef"] = questTokenRef,
                        ["FeatureRef"] = feature.RefName,
                        ["Reason"] = $"Feature '{feature.RefName}' interaction"
                    }
                };

                instance.AddTransaction(questTokenTx);

                // Apply quest token to avatar immediately (for sandbox/single-player)
                ApplyQuestTokenToAvatar(avatar, questTokenRef);
            }
        }
    }

    /// <summary>
    /// Activates a trigger and spawns associated characters.
    /// </summary>
    private void ActivateSagaTrigger(
        SagaInstance instance,
        SagaTrigger sagaTrigger,
        double avatarX,
        double avatarZ,
        string avatarId)
    {
        // Generate seed for deterministic spawn
        var seed = Random.Shared.Next();

        // Create TriggerActivated transaction
        var sagaTriggerTx = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.TriggerActivated,
            AvatarId = avatarId,
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["SagaTriggerRef"] = sagaTrigger.RefName,
                ["AvatarX"] = avatarX.ToString("F6"),
                ["AvatarZ"] = avatarZ.ToString("F6"),
                ["Seed"] = seed.ToString()
            }
        };

        instance.AddTransaction(sagaTriggerTx);

        // Award quest tokens if trigger gives any
        if (sagaTrigger.GivesQuestTokenRef != null && sagaTrigger.GivesQuestTokenRef.Length > 0)
        {
            foreach (var questTokenRef in sagaTrigger.GivesQuestTokenRef)
            {
                var questTokenTx = new SagaTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    Type = SagaTransactionType.QuestTokenAwarded,
                    AvatarId = avatarId,
                    Status = TransactionStatus.Pending,
                    LocalTimestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, string>
                    {
                        ["QuestTokenRef"] = questTokenRef,
                        ["SagaTriggerRef"] = sagaTrigger.RefName,
                        ["Reason"] = $"Trigger '{sagaTrigger.RefName}' activated"
                    }
                };

                instance.AddTransaction(questTokenTx);
            }
        }

        // Spawn characters if trigger has spawns
        if (sagaTrigger.Spawn != null && sagaTrigger.Spawn.Length > 0)
        {
            SpawnCharacters(instance, sagaTrigger, avatarX, avatarZ, seed, avatarId);
        }
    }

    /// <summary>
    /// Spawns characters around the avatar's position using deterministic seed.
    /// - SpawnAndInitiate: 2m from player (inside ApproachRadius for immediate engagement)
    /// - SpawnPassive: 10m from player at random angles (player must approach)
    /// Respawns defeated characters if RespawnIntervalSeconds has elapsed.
    /// </summary>
    private void SpawnCharacters(
        SagaInstance instance,
        SagaTrigger sagaTrigger,
        double avatarX,
        double avatarZ,
        int seed,
        string avatarId)
    {
        System.Diagnostics.Debug.WriteLine($"[SpawnCharacters] Called for trigger '{sagaTrigger.RefName}' at ({avatarX:F2}, {avatarZ:F2})");

        // Check if characters from this trigger were previously defeated and can respawn
        CheckAndRespawnDefeatedCharacters(instance, sagaTrigger, avatarX, avatarZ, seed, avatarId);

        var spawns = sagaTrigger.Spawn;
        var resolver = new CharacterSpawnResolver(_world, seed);
        var resolvedSpawns = resolver.ResolveSpawns(spawns);

        System.Diagnostics.Debug.WriteLine($"[SpawnCharacters] Resolved {resolvedSpawns.Count} character spawns");

        if (resolvedSpawns.Count == 0)
            return;

        // Spawn characters close to avatar - they will immediately initiate interaction
        var spawnRadius = 2.0;

        // Calculate spawn positions in circle around avatar (Saga-relative)
        var spawnPositions = CalculateCircularSpawnPositions(
            avatarX,
            avatarZ,
            spawnRadius,
            resolvedSpawns.Count,
            seed);

        // Create CharacterSpawned transaction for each character
        for (var i = 0; i < resolvedSpawns.Count; i++)
        {
            var resolvedSpawn = resolvedSpawns[i];
            var characterRef = resolvedSpawn.CharacterRef;
            var (spawnX, spawnZ) = spawnPositions[i];

            // Verify character template exists
            if (!_world.CharactersLookup.TryGetValue(characterRef, out var characterTemplate))
            {
                System.Diagnostics.Debug.WriteLine($"[Saga] Character template '{characterRef}' not found");
                continue;
            }

            var characterInstanceId = Guid.NewGuid();

            System.Diagnostics.Debug.WriteLine($"[SpawnCharacters] Creating spawn tx for '{characterRef}' at ({spawnX:F2}, {spawnZ:F2}), radius={spawnRadius}m");

            var spawnTx = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.CharacterSpawned,
                AvatarId = avatarId,
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["CharacterInstanceId"] = characterInstanceId.ToString(),
                    ["CharacterRef"] = characterRef,
                    ["SagaTriggerRef"] = sagaTrigger.RefName,
                    ["X"] = spawnX.ToString("F6"),  // Saga-relative
                    ["Z"] = spawnZ.ToString("F6"),  // Saga-relative
                    ["SpawnHeight"] = "0"           // Default, game will adjust to terrain
                }
            };

            instance.AddTransaction(spawnTx);
        }
    }

    /// <summary>
    /// Calculates spawn positions in a circle around a center point.
    /// Uses deterministic random seed for consistent placement on replay.
    /// All coordinates are in Saga-relative space (X/Z plane, Y is height).
    /// </summary>
    private List<(double x, double z)> CalculateCircularSpawnPositions(
        double centerX,
        double centerZ,
        double radius,
        int count,
        int seed)
    {
        var positions = new List<(double, double)>();

        if (count <= 0)
            return positions;

        var rng = new Random(seed);

        // Distribute evenly around circle with slight randomization
        var baseAngleStep = 2.0 * Math.PI / count;

        for (var i = 0; i < count; i++)
        {
            // Base angle with small random offset for natural feel
            var angle = i * baseAngleStep + (rng.NextDouble() - 0.5) * baseAngleStep * 0.2;

            // Slight radius variation (90-100% of specified radius)
            var radiusVariation = radius * (0.9 + rng.NextDouble() * 0.1);

            var offsetX = radiusVariation * Math.Sin(angle);
            var offsetZ = radiusVariation * Math.Cos(angle);

            var spawnX = centerX + offsetX;
            var spawnZ = centerZ + offsetZ;

            positions.Add((spawnX, spawnZ));
        }

        return positions;
    }

    /// <summary>
    /// Checks for defeated characters from this trigger and respawns them if RespawnIntervalSeconds has elapsed.
    /// Only creates CharacterSpawned transactions for characters that can respawn (RespawnIntervalSeconds > 0).
    /// </summary>
    private void CheckAndRespawnDefeatedCharacters(
        SagaInstance instance,
        SagaTrigger sagaTrigger,
        double avatarX,
        double avatarZ,
        int seed,
        string avatarId)
    {
        // Get all CharacterSpawned transactions for this trigger
        var spawnedByTrigger = instance.GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.CharacterSpawned &&
                       t.Data.TryGetValue("SagaTriggerRef", out var triggerRef) &&
                       triggerRef == sagaTrigger.RefName)
            .ToList();

        if (!spawnedByTrigger.Any())
            return; // No characters ever spawned by this trigger

        // Check each spawned character to see if it was defeated and can respawn
        foreach (var spawnTx in spawnedByTrigger)
        {
            if (!spawnTx.Data.TryGetValue("CharacterRef", out var characterRef))
                continue;

            if (!spawnTx.Data.TryGetValue("CharacterInstanceId", out var instanceIdStr) ||
                !Guid.TryParse(instanceIdStr, out var characterInstanceId))
                continue;

            // Get character template to check RespawnIntervalSeconds
            if (!_world.CharactersLookup.TryGetValue(characterRef, out var characterTemplate))
                continue;

            // Skip if character doesn't respawn (RespawnIntervalSeconds = 0)
            if (characterTemplate.RespawnIntervalSeconds <= 0)
                continue;

            // Check if this character was defeated
            var defeatTx = instance.GetCommittedTransactions()
                .FirstOrDefault(t => t.Type == SagaTransactionType.CharacterDefeated &&
                                   t.Data.TryGetValue("CharacterInstanceId", out var defId) &&
                                   defId == characterInstanceId.ToString());

            if (defeatTx == null)
                continue; // Character not defeated, no need to respawn

            // Check if enough time has passed since defeat
            var timeSinceDefeat = (DateTime.UtcNow - defeatTx.GetCanonicalTimestamp()).TotalSeconds;
            if (timeSinceDefeat < characterTemplate.RespawnIntervalSeconds)
                continue; // Not enough time elapsed

            // Check if character was already respawned after this defeat
            var respawnedAfterDefeat = instance.GetCommittedTransactions()
                .Any(t => t.Type == SagaTransactionType.CharacterSpawned &&
                         t.Data.TryGetValue("CharacterRef", out var respawnRef) &&
                         respawnRef == characterRef &&
                         t.Data.TryGetValue("SagaTriggerRef", out var respawnTriggerRef) &&
                         respawnTriggerRef == sagaTrigger.RefName &&
                         t.GetCanonicalTimestamp() > defeatTx.GetCanonicalTimestamp());

            if (respawnedAfterDefeat)
                continue; // Already respawned after this defeat

            // RESPAWN: Create new CharacterSpawned transaction
            var newCharacterInstanceId = Guid.NewGuid();

            // Get original spawn position (or use current avatar position if not found)
            var spawnX = avatarX;
            var spawnZ = avatarZ;
            if (spawnTx.Data.TryGetValue("X", out var origX) && double.TryParse(origX, out var parsedX))
                spawnX = parsedX;
            if (spawnTx.Data.TryGetValue("Z", out var origZ) && double.TryParse(origZ, out var parsedZ))
                spawnZ = parsedZ;

            System.Diagnostics.Debug.WriteLine($"[RESPAWN] Character '{characterRef}' respawning after {timeSinceDefeat:F0}s (interval: {characterTemplate.RespawnIntervalSeconds}s)");

            var respawnTx = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.CharacterSpawned,
                AvatarId = avatarId,
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["CharacterInstanceId"] = newCharacterInstanceId.ToString(),
                    ["CharacterRef"] = characterRef,
                    ["SagaTriggerRef"] = sagaTrigger.RefName,
                    ["X"] = spawnX.ToString("F6"),
                    ["Z"] = spawnZ.ToString("F6"),
                    ["SpawnHeight"] = "0",
                    ["IsRespawn"] = "true", // Mark as respawn for analytics
                    ["PreviousInstanceId"] = characterInstanceId.ToString() // Link to defeated instance
                }
            };

            instance.AddTransaction(respawnTx);
        }
    }

    #endregion

    #region Reward Application (Sandbox/Single-Player)

    /// <summary>
    /// Applies loot items to avatar's inventory.
    /// For now: Just add everything the avatar doesn't already have.
    /// Future: Handle stacking, quantity limits, progression gating.
    /// </summary>
    private void ApplyLootToAvatar(AvatarBase avatar, ItemCollection loot)
    {
        if (avatar.Capabilities == null)
            avatar.Capabilities = new ItemCollection();

        // Equipment (not stackable - you either have it or you don't)
        if (loot.Equipment != null && loot.Equipment.Length > 0)
        {
            var existingEquipment = avatar.Capabilities.Equipment?.ToList() ?? new List<EquipmentEntry>();
            foreach (var equipment in loot.Equipment)
            {
                // Only add if avatar doesn't already have this equipment
                if (!existingEquipment.Any(e => e.EquipmentRef == equipment.EquipmentRef))
                {
                    existingEquipment.Add(new EquipmentEntry
                    {
                        EquipmentRef = equipment.EquipmentRef,
                        Condition = 1.0f // Full condition
                    });
                }
            }
            avatar.Capabilities.Equipment = existingEquipment.ToArray();
        }

        // Consumables
        if (loot.Consumables != null && loot.Consumables.Length > 0)
        {
            var existingConsumables = avatar.Capabilities.Consumables?.ToList() ?? new List<ConsumableEntry>();
            foreach (var consumable in loot.Consumables)
            {
                var existing = existingConsumables.FirstOrDefault(c => c.ConsumableRef == consumable.ConsumableRef);
                if (existing != null)
                {
                    // Stack existing consumable
                    existing.Quantity += consumable.Quantity;
                }
                else
                {
                    // Add new consumable
                    existingConsumables.Add(new ConsumableEntry
                    {
                        ConsumableRef = consumable.ConsumableRef,
                        Quantity = consumable.Quantity
                    });
                }
            }
            avatar.Capabilities.Consumables = existingConsumables.ToArray();
        }

        // Blocks
        if (loot.Blocks != null && loot.Blocks.Length > 0)
        {
            var existingBlocks = avatar.Capabilities.Blocks?.ToList() ?? new List<BlockEntry>();
            foreach (var block in loot.Blocks)
            {
                var existing = existingBlocks.FirstOrDefault(b => b.BlockRef == block.BlockRef);
                if (existing != null)
                {
                    // Stack existing block
                    existing.Quantity += block.Quantity;
                }
                else
                {
                    // Add new block
                    existingBlocks.Add(new BlockEntry
                    {
                        BlockRef = block.BlockRef,
                        Quantity = block.Quantity
                    });
                }
            }
            avatar.Capabilities.Blocks = existingBlocks.ToArray();
        }

        // Tools (not stackable - condition tracks durability)
        if (loot.Tools != null && loot.Tools.Length > 0)
        {
            var existingTools = avatar.Capabilities.Tools?.ToList() ?? new List<ToolEntry>();
            foreach (var tool in loot.Tools)
            {
                // Only add if avatar doesn't already have this tool
                if (!existingTools.Any(t => t.ToolRef == tool.ToolRef))
                {
                    existingTools.Add(new ToolEntry
                    {
                        ToolRef = tool.ToolRef,
                        Condition = 1.0f // Full condition
                    });
                }
            }
            avatar.Capabilities.Tools = existingTools.ToArray();
        }

        // Building Materials
        if (loot.BuildingMaterials != null && loot.BuildingMaterials.Length > 0)
        {
            var existingMaterials = avatar.Capabilities.BuildingMaterials?.ToList() ?? new List<BuildingMaterialEntry>();
            foreach (var material in loot.BuildingMaterials)
            {
                var existing = existingMaterials.FirstOrDefault(m => m.BuildingMaterialRef == material.BuildingMaterialRef);
                if (existing != null)
                {
                    // Stack existing material
                    existing.Quantity += material.Quantity;
                }
                else
                {
                    // Add new material
                    existingMaterials.Add(new BuildingMaterialEntry
                    {
                        BuildingMaterialRef = material.BuildingMaterialRef,
                        Quantity = material.Quantity
                    });
                }
            }
            avatar.Capabilities.BuildingMaterials = existingMaterials.ToArray();
        }

        // Spells
        if (loot.Spells != null && loot.Spells.Length > 0)
        {
            var existingSpells = avatar.Capabilities.Spells?.ToList() ?? new List<SpellEntry>();
            foreach (var spell in loot.Spells)
            {
                // Only add if avatar doesn't already have this spell
                if (!existingSpells.Any(s => s.SpellRef == spell.SpellRef))
                {
                    existingSpells.Add(new SpellEntry
                    {
                        SpellRef = spell.SpellRef
                    });
                }
            }
            avatar.Capabilities.Spells = existingSpells.ToArray();
        }
    }

    /// <summary>
    /// Applies stat effects to avatar.
    /// For now: Just add the values directly to avatar stats.
    /// Future: Handle percentages, multipliers, temporary buffs.
    /// </summary>
    private void ApplyEffectsToAvatar(AvatarBase avatar, RewardEffects effects)
    {
        if (avatar.Stats == null)
            return;

        // Apply each stat modification (additive for now)
        // RewardEffects has direct properties, not nested ModifiableCharacterStats
        avatar.Stats.Health += effects.Health;
        avatar.Stats.Stamina += effects.Stamina;
        avatar.Stats.Mana += effects.Mana;
        avatar.Stats.Hunger += effects.Hunger;
        avatar.Stats.Thirst += effects.Thirst;
        avatar.Stats.Temperature += effects.Temperature;
        avatar.Stats.Insulation += effects.Insulation;
        avatar.Stats.Credits += effects.Credits;
        avatar.Stats.Experience += effects.Experience;
        avatar.Stats.Strength += effects.Strength;
        avatar.Stats.Defense += effects.Defense;
        avatar.Stats.Speed += effects.Speed;
        avatar.Stats.Magic += effects.Magic;

        // Clamp vitals to valid ranges (0.0 - 1.0 for normalized stats)
        avatar.Stats.Health = Math.Clamp(avatar.Stats.Health, 0.0f, 1.0f);
        avatar.Stats.Stamina = Math.Clamp(avatar.Stats.Stamina, 0.0f, 1.0f);
        avatar.Stats.Mana = Math.Clamp(avatar.Stats.Mana, 0.0f, 1.0f);
    }

    /// <summary>
    /// Adds a quest token to avatar's inventory.
    /// Tokens are unique (no duplicates).
    /// </summary>
    private void ApplyQuestTokenToAvatar(AvatarBase avatar, string questTokenRef)
    {
        if (avatar.Capabilities == null)
            avatar.Capabilities = new ItemCollection();

        var existingTokens = avatar.Capabilities.QuestTokens?.ToList() ?? new List<QuestTokenEntry>();

        // Only add if avatar doesn't already have this token
        if (!existingTokens.Any(t => t.QuestTokenRef == questTokenRef))
        {
            existingTokens.Add(new QuestTokenEntry
            {
                QuestTokenRef = questTokenRef
            });

            avatar.Capabilities.QuestTokens = existingTokens.ToArray();
        }
    }

    #endregion
}

/// <summary>
/// Information about a trigger's proximity to a position.
/// Used for querying trigger state without activation.
/// </summary>
public class SagaTriggerProximityInfo
{
    /// <summary>The trigger being checked</summary>
    public required SagaTrigger SagaTrigger { get; init; }

    /// <summary>Distance from Saga center to the position being checked</summary>
    public double DistanceFromCenter { get; init; }

    /// <summary>Whether the position is within the trigger's radius</summary>
    public bool IsWithinRadius { get; init; }

    /// <summary>Current status of this trigger (from Saga state)</summary>
    public SagaTriggerStatus TriggerStatus { get; init; }

    /// <summary>Whether this trigger has been completed</summary>
    public bool IsCompleted { get; init; }
}

/// <summary>
/// Result of checking whether a trigger can be activated.
/// Provides detailed information about why activation is blocked.
/// </summary>
public class SagaTriggerActivationCheck
{
    /// <summary>The trigger being checked</summary>
    public required SagaTrigger SagaTrigger { get; init; }

    /// <summary>Whether the trigger can be activated</summary>
    public bool CanActivate { get; set; }

    /// <summary>Distance from Saga center</summary>
    public double DistanceFromCenter { get; set; }

    /// <summary>Whether avatar is within trigger radius</summary>
    public bool IsWithinRadius { get; set; }

    /// <summary>Whether avatar has all required quest tokens</summary>
    public bool HasRequiredQuestTokens { get; set; }

    /// <summary>Quest tokens the avatar is missing (if any)</summary>
    public string[] MissingQuestTokens { get; set; } = Array.Empty<string>();

    /// <summary>Human-readable reason why trigger cannot be activated (if blocked)</summary>
    public string? BlockedReason { get; set; }
}

/// <summary>
/// Result of checking whether a feature can be interacted with.
/// Provides detailed information about interaction limits and requirements.
///
/// Note: Cooldown checking is NOT included here - that's a game concern.
/// Game should check LastInteractedAt from FeatureInteractionState if needed.
/// </summary>
public class FeatureInteractionCheck
{
    /// <summary>The feature being checked</summary>
    public required SagaFeature Feature { get; init; }

    /// <summary>Whether the feature can be interacted with</summary>
    public bool CanInteract { get; set; }

    /// <summary>Whether avatar has all required quest tokens</summary>
    public bool HasRequiredQuestTokens { get; set; }

    /// <summary>Quest tokens the avatar is missing (if any)</summary>
    public string[] MissingQuestTokens { get; set; } = Array.Empty<string>();

    /// <summary>Current interaction count for this avatar</summary>
    public int CurrentInteractionCount { get; set; }

    /// <summary>Whether max interactions limit has been reached</summary>
    public bool MaxInteractionsReached { get; set; }

    /// <summary>Human-readable reason why interaction is blocked (if blocked)</summary>
    public string? BlockedReason { get; set; }
}
