using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Contracts.Persistence;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Domain;

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
    // Sentinel Y — "unknown, resolve against terrain at render time". Matches Archimedea's SpatialConstants.DeepUnderGround.
    private const string SpawnHeightSentinel = "-1073741824";

    private readonly SagaArc _template;
    private readonly List<SagaTrigger> _expandedSagaTriggers;
    private readonly IWorld _world;
    private readonly SagaStateMachine _stateMachine;

    public SagaInteractionService(
        SagaArc template,
        List<SagaTrigger> expandedSagaTriggers,
        IWorld world)
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
        AvatarBase avatar,
        IAvatarProgressRepository? progressRepo = null,
        Guid avatarId = default)
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

            // Check quest token requirements from avatar progress table
            var canActivate = progressRepo != null && avatarId != Guid.Empty
                ? TriggerAvailabilityChecker.CanActivate(trigger, progressRepo, avatarId)
                : TriggerAvailabilityChecker.CanActivate(trigger, currentState);
            if (!canActivate)
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
        AvatarBase avatar,
        IAvatarProgressRepository? progressRepo = null)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));

        if (avatar == null)
            throw new ArgumentNullException(nameof(avatar));

        // Get current state by replaying transactions
        var currentState = _stateMachine.ReplayToNow(instance);

        // Calculate distance from Saga center (used for discovery, enter, and exit checks)
        var distanceFromCenter = Math.Sqrt(avatarX * avatarX + avatarZ * avatarZ);

        // PHASE 0: Check for SagaArc discovery
        // If avatar is within DiscoverRadius and hasn't discovered this saga yet, log discovery
        var avatarId = avatar.AvatarId.ToString();
        if (distanceFromCenter <= _template.DiscoverRadius &&
            !currentState.DiscoveredByAvatars.Contains(avatarId))
        {
            var discoveryTx = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.SagaDiscovered,
                AvatarId = avatarId,
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    [TransactionDataKeys.SagaArcRef] = _template.RefName,
                    [TransactionDataKeys.DistanceMeters] = distanceFromCenter.ToString("F2"),
                    [TransactionDataKeys.DiscoverRadius] = _template.DiscoverRadius.ToString("F2")
                }
            };
            instance.AddTransaction(discoveryTx);

            // Update state so subsequent checks in this call see the saga as discovered
            currentState.DiscoveredByAvatars.Add(avatarId);
            if (currentState.Status == SagaStatus.Undiscovered)
            {
                currentState.Status = SagaStatus.Active;
                currentState.FirstDiscoveredAt = discoveryTx.LocalTimestamp;
            }
        }

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
                // Avatar has exited the trigger zone - record the exit but leave spawned
                // characters where they are. Encounters persist across visits; defeated
                // characters with a RespawnIntervalSeconds come back via Phase 3 once the
                // interval has elapsed and the avatar re-enters the zone.
                var exitTx = new SagaTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    Type = SagaTransactionType.AvatarExited,
                    AvatarId = avatar.AvatarId.ToString(),
                    Status = TransactionStatus.Pending,
                    LocalTimestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, string>
                    {
                        [TransactionDataKeys.TriggerRef] = sagaTrigger.RefName,
                        [TransactionDataKeys.DistanceMeters] = distanceFromCenter.ToString("F2"),
                        [TransactionDataKeys.ExitRadius] = exitRadius.ToString("F2")
                    }
                };
                instance.AddTransaction(exitTx);
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

            // Check quest token requirements from avatar progress table
            var canActivate = progressRepo != null && avatar.AvatarId != Guid.Empty
                ? TriggerAvailabilityChecker.CanActivate(sagaTrigger, progressRepo, avatar.AvatarId)
                : TriggerAvailabilityChecker.CanActivate(sagaTrigger, currentState);
            if (!canActivate)
                continue;

            // Trigger activated! Create entry transaction first
            var enterTx = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.AvatarEntered,
                AvatarId = avatar.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    [TransactionDataKeys.TriggerRef] = sagaTrigger.RefName,
                    [TransactionDataKeys.DistanceMeters] = distanceFromCenter.ToString("F2"),
                    [TransactionDataKeys.EnterRadius] = sagaTrigger.EnterRadius.ToString("F2")
                }
            };
            instance.AddTransaction(enterTx);

            // Then activate trigger and spawn characters
            ActivateSagaTrigger(instance, sagaTrigger, avatarX, avatarZ, avatar.AvatarId.ToString());
        }

        // PHASE 3: Respawn evaluation for completed triggers the avatar is currently inside.
        // Triggers are one-and-done for activation, but defeated characters from a completed
        // trigger can come back once their template's RespawnIntervalSeconds has elapsed —
        // provided the avatar is physically in the zone to witness it.
        foreach (var sagaTrigger in _expandedSagaTriggers)
        {
            if (!currentState.Triggers.TryGetValue(sagaTrigger.RefName, out var triggerState)
                || triggerState.Status != SagaTriggerStatus.Completed)
            {
                continue;
            }

            if (distanceFromCenter > sagaTrigger.EnterRadius)
                continue;

            var respawnSeed = Random.Shared.Next();
            CheckAndRespawnDefeatedCharacters(instance, sagaTrigger, avatarX, avatarZ, respawnSeed, avatar.AvatarId.ToString());
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
                [TransactionDataKeys.SagaTriggerRef] = sagaTrigger.RefName,
                [TransactionDataKeys.AvatarX] = avatarX.ToString("F6"),
                [TransactionDataKeys.AvatarZ] = avatarZ.ToString("F6"),
                [TransactionDataKeys.Seed] = seed.ToString()
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
                        [TransactionDataKeys.QuestTokenRef] = questTokenRef,
                        [TransactionDataKeys.SagaTriggerRef] = sagaTrigger.RefName,
                        [TransactionDataKeys.Reason] = $"Trigger '{sagaTrigger.RefName}' activated"
                    }
                };

                instance.AddTransaction(questTokenTx);
            }
        }

        // Spawn characters if trigger has spawns
        if (sagaTrigger.Spawn != null && sagaTrigger.Spawn.Length > 0)
        {
            var spawnCountBefore = instance.Transactions.Count(tx => tx.Type == SagaTransactionType.CharacterSpawned);
            SpawnCharacters(instance, sagaTrigger, avatarX, avatarZ, seed, avatarId);
            var spawnCountAfter = instance.Transactions.Count(tx => tx.Type == SagaTransactionType.CharacterSpawned);

            // Only mark trigger as completed if characters were actually spawned
            // Per design: "The instant the characters are triggered and instantiated that trigger is 'done'"
            if (spawnCountAfter > spawnCountBefore)
            {
                var completedTx = new SagaTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    Type = SagaTransactionType.TriggerCompleted,
                    AvatarId = avatarId,
                    Status = TransactionStatus.Pending,
                    LocalTimestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, string>
                    {
                        [TransactionDataKeys.SagaTriggerRef] = sagaTrigger.RefName,
                        [TransactionDataKeys.Reason] = "Characters spawned"
                    }
                };
                instance.AddTransaction(completedTx);
            }
        }
    }

    /// <summary>
    /// Spawns characters around the avatar's position using deterministic seed.
    /// - SpawnAndInitiate: 2m from avatar (inside ApproachRadius for immediate engagement)
    /// - SpawnPassive: 10m from avatar at random angles (avatar must approach)
    /// Called on the initial trigger activation only. Respawn of defeated characters
    /// is handled separately in Phase 3 of UpdateWithAvatarPosition.
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

        var spawns = sagaTrigger.Spawn;
        var resolver = new CharacterSpawnResolver(_world, seed);
        var resolvedSpawns = resolver.ResolveSpawns(spawns);

        System.Diagnostics.Debug.WriteLine($"[SpawnCharacters] Resolved {resolvedSpawns.Count} character spawns");

        if (resolvedSpawns.Count == 0)
            return;

        // Place characters ahead of the avatar toward the Saga origin, so the encounter
        // always materializes between the player and the POI rather than wherever the
        // player happened to be facing. Depth scales with the trigger's EnterRadius but
        // is capped so characters are always within a chunk (~15 blocks) of where the
        // avatar crossed the ring — close enough to notice and be drawn toward.
        const double maxSpawnDepthMeters = 15.0;
        var spawnDepth = Math.Min(sagaTrigger.EnterRadius * 0.5, maxSpawnDepthMeters);

        var spawnPositions = CalculateForwardArcSpawnPositions(
            avatarX,
            avatarZ,
            spawnDepth,
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

            System.Diagnostics.Debug.WriteLine($"[SpawnCharacters] Creating spawn tx for '{characterRef}' at ({spawnX:F2}, {spawnZ:F2}), depth={spawnDepth:F2}m");

            var spawnTx = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.CharacterSpawned,
                AvatarId = avatarId,
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    [TransactionDataKeys.CharacterInstanceId] = characterInstanceId.ToString(),
                    [TransactionDataKeys.CharacterRef] = characterRef,
                    [TransactionDataKeys.SagaTriggerRef] = sagaTrigger.RefName,
                    [TransactionDataKeys.X] = spawnX.ToString("F6"),  // Saga-relative
                    [TransactionDataKeys.Z] = spawnZ.ToString("F6"),  // Saga-relative
                    [TransactionDataKeys.SpawnHeight] = SpawnHeightSentinel // "Y unknown" — game resolves against terrain
                }
            };

            instance.AddTransaction(spawnTx);
        }
    }

    /// <summary>
    /// Calculates spawn positions in a small forward arc ahead of the avatar, in the
    /// direction of the Saga origin (0, 0 in Saga-relative coords). Characters are
    /// spread across a modest arc centered on that forward direction, with mild
    /// angular and radial jitter for a natural feel.
    ///
    /// If the avatar is at (or essentially at) the origin there is no "forward"
    /// direction, so we pick an arbitrary one deterministically from the seed.
    /// </summary>
    private List<(double x, double z)> CalculateForwardArcSpawnPositions(
        double avatarX,
        double avatarZ,
        double depth,
        int count,
        int seed)
    {
        var positions = new List<(double, double)>();

        if (count <= 0)
            return positions;

        var rng = new Random(seed);

        // Unit vector from avatar toward origin, or a random direction if avatar is at origin.
        const double epsilon = 0.01; // metres
        var avatarDistanceFromOrigin = Math.Sqrt(avatarX * avatarX + avatarZ * avatarZ);
        double forwardAngle;
        if (avatarDistanceFromOrigin < epsilon)
        {
            forwardAngle = rng.NextDouble() * 2.0 * Math.PI;
        }
        else
        {
            // Using the existing sin-for-X, cos-for-Z convention: angle = atan2(x, z).
            forwardAngle = Math.Atan2(-avatarX, -avatarZ);
        }

        // Spread the group across a 60° arc centred on the forward direction.
        // Single-character spawns land straight ahead; groups fan out symmetrically.
        const double totalArcRadians = Math.PI / 3.0; // 60°
        var angleStep = count > 1 ? totalArcRadians / (count - 1) : 0.0;
        var startOffset = -totalArcRadians / 2.0;

        // Keep jitter proportional to the per-character slice, or a small fixed
        // amount when there is only one character, so it never lands perfectly on
        // the avatar→origin line.
        var jitterScale = count > 1 ? angleStep * 0.2 : Math.PI / 36.0; // ~5° fallback

        for (var i = 0; i < count; i++)
        {
            var baseOffset = count > 1 ? startOffset + i * angleStep : 0.0;
            var angle = forwardAngle + baseOffset + (rng.NextDouble() - 0.5) * jitterScale;

            var radiusVariation = depth * (0.9 + rng.NextDouble() * 0.1);

            var offsetX = radiusVariation * Math.Sin(angle);
            var offsetZ = radiusVariation * Math.Cos(angle);

            positions.Add((avatarX + offsetX, avatarZ + offsetZ));
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
                       t.Data.TryGetValue(TransactionDataKeys.SagaTriggerRef, out var triggerRef) &&
                       triggerRef == sagaTrigger.RefName)
            .ToList();

        if (!spawnedByTrigger.Any())
            return; // No characters ever spawned by this trigger

        // Check each spawned character to see if it was defeated and can respawn
        foreach (var spawnTx in spawnedByTrigger)
        {
            if (!spawnTx.Data.TryGetValue(TransactionDataKeys.CharacterRef, out var characterRef))
                continue;

            if (!spawnTx.Data.TryGetValue(TransactionDataKeys.CharacterInstanceId, out var instanceIdStr) ||
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
                                   t.Data.TryGetValue(TransactionDataKeys.CharacterInstanceId, out var defId) &&
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
                         t.Data.TryGetValue(TransactionDataKeys.CharacterRef, out var respawnRef) &&
                         respawnRef == characterRef &&
                         t.Data.TryGetValue(TransactionDataKeys.SagaTriggerRef, out var respawnTriggerRef) &&
                         respawnTriggerRef == sagaTrigger.RefName &&
                         t.GetCanonicalTimestamp() > defeatTx.GetCanonicalTimestamp());

            if (respawnedAfterDefeat)
                continue; // Already respawned after this defeat

            // RESPAWN: Create new CharacterSpawned transaction
            var newCharacterInstanceId = Guid.NewGuid();

            // Get original spawn position (or use current avatar position if not found)
            var spawnX = avatarX;
            var spawnZ = avatarZ;
            if (spawnTx.Data.TryGetValue(TransactionDataKeys.X, out var origX) && double.TryParse(origX, out var parsedX))
                spawnX = parsedX;
            if (spawnTx.Data.TryGetValue(TransactionDataKeys.Z, out var origZ) && double.TryParse(origZ, out var parsedZ))
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
                    [TransactionDataKeys.CharacterInstanceId] = newCharacterInstanceId.ToString(),
                    [TransactionDataKeys.CharacterRef] = characterRef,
                    [TransactionDataKeys.SagaTriggerRef] = sagaTrigger.RefName,
                    [TransactionDataKeys.X] = spawnX.ToString("F6"),
                    [TransactionDataKeys.Z] = spawnZ.ToString("F6"),
                    [TransactionDataKeys.SpawnHeight] = SpawnHeightSentinel, // "Y unknown" — game resolves against terrain
                    [TransactionDataKeys.IsRespawn] = "true", // Mark as respawn for analytics
                    [TransactionDataKeys.PreviousInstanceId] = characterInstanceId.ToString() // Link to defeated instance
                }
            };

            instance.AddTransaction(respawnTx);
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

