using Ambient.Domain;
using Ambient.Rpg.Engine;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Domain;
using Ambient.Rpg.Engine.Domain.Battle;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Xunit.Abstractions;

namespace Ambient.Rpg.Ui.Tests.RealContentE2E;

/// <summary>
/// Full battle E2E at the view-model layer over REAL world content (the vendored Ise
/// world). Drives the EXACT command/query sequence BattleModal issues, in the same order:
///
/// - UpdateAvatarPositionCommand at a real trigger's coordinates (the command the view
///   model sends from TryProcessAvatarMovementAsync) to spawn the authored hostile,
/// - GetSpawnedCharactersQuery (the view model's character discovery query),
/// - BattleSetup from the loaded world + the avatar's real archetype (BattleModal.
///   InitializeBattleAsync verbatim) → StartBattleCommand,
/// - the turn loop: GetBattleStateQuery each round; SubmitReactionCommand when a
///   telegraphed tell is pending (server-resolved, round-3 contract), otherwise
///   ExecuteBattleTurnCommand(Attack),
/// - and finally a full "reload": everything in memory is discarded, a fresh host is
///   built over the same LiteDB file, and GetBattleStateQuery must reconstruct the
///   identical end state from the committed absolute snapshots.
///
/// Determinism: fixed battleSeed — StartBattleCommand.RandomSeed and the enemy
/// CombatAI share it, and each turn derives its RNG as battleSeed + 104729 * turnIndex
/// (ExecuteBattleTurnHandler), so the fight plays out identically on every run.
///
/// Content used: Ise arc "Aramatsurinomiya_to_ToyokeDaijinguOuterShrine_3" (Danger on
/// the Trail), hostile "HOSTILE_Aramatsurinomiya_to_ToyokeDaijinguOuterShrine_3"
/// (Trail Hazard) with real AttackTells.xml telegraphs, fought by the real "Warrior"
/// archetype avatar.
/// </summary>
[Collection("Real Content E2E")]
public class RealContentBattleE2ETests : IAsyncLifetime, IDisposable
{
    private const string WorldRef = "Ise";
    private const string ArcRef = "Aramatsurinomiya_to_ToyokeDaijinguOuterShrine_3";
    private const string EnemyRef = "HOSTILE_Aramatsurinomiya_to_ToyokeDaijinguOuterShrine_3";
    private const int BattleSeed = 12345;
    private const int MaxRounds = 100;

    private readonly ITestOutputHelper _output;
    private RealContentE2EHost _host = null!;

    public RealContentBattleE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        _host = await RealContentE2EHost.CreateAsync(WorldRef);
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _host?.Dispose();

    [Fact]
    public async Task FullBattle_RealHostile_ToVictory_WithReactions_AndConsistentReconstructionAfterReload()
    {
        var vm = _host.ViewModel;
        var avatar = _host.CreateAvatarFromWorldArchetype("Warrior", "E2E Battle Hero");

        // ================= ENTER THE ZONE (real trigger spawn) =================
        var arc = vm.CurrentWorld!.ArcLookup[ArcRef];
        var enter = await vm.Mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatar.AvatarId,
            ArcRef = ArcRef,
            Latitude = arc.Latitude,
            Longitude = arc.Longitude,
            Avatar = avatar
        });
        Assert.True(enter.Successful, $"Zone entry failed: {enter.ErrorMessage}");

        var spawned = await vm.Mediator.Send(new GetSpawnedCharactersQuery
        {
            AvatarId = avatar.AvatarId,
            ArcRef = ArcRef,
            SpawnedOnly = true,
            AliveOnly = true
        });
        var enemyState = Assert.Single(spawned, c => c.CharacterRef == EnemyRef);
        _output.WriteLine($"Spawned enemy: {EnemyRef} ({enemyState.CharacterInstanceId})");

        // ================= START THE BATTLE (BattleModal.InitializeBattleAsync) =================
        var enemyTemplate = vm.CurrentWorld.Gameplay!.Characters!.First(c => c.RefName == EnemyRef);
        var archetype = vm.CurrentWorld.Gameplay.AvatarArchetypes!.First(a => a.RefName == avatar.ArchetypeRef);

        var battleSetup = new BattleSetup();
        battleSetup.SetupFromWorld(vm.CurrentWorld);
        battleSetup.SelectedAvatarArchetype = archetype;
        battleSetup.AvatarCapabilities = avatar.Capabilities ?? new ItemCollection();
        var availableAffinities = vm.CurrentWorld.Gameplay.CharacterAffinities?
            .Select(a => a.RefName).ToList() ?? new List<string>();
        if (availableAffinities.Count == 0 && !string.IsNullOrEmpty(archetype.AffinityRef))
        {
            availableAffinities.Add(archetype.AffinityRef);
        }
        battleSetup.AvatarAffinityRefs = availableAffinities;
        battleSetup.SelectedOpponentCharacter = enemyTemplate;
        battleSetup.OpponentCapabilities = enemyTemplate.Capabilities ?? new ItemCollection();

        var battleEngine = battleSetup.CreateBattleEngine();

        var startResult = await vm.Mediator.Send(new StartBattleCommand
        {
            AvatarId = avatar.AvatarId,
            ArcRef = ArcRef,
            EnemyCharacterInstanceId = enemyState.CharacterInstanceId,
            AvatarCombatant = battleEngine.GetAvatar(),
            EnemyCombatant = battleEngine.GetEnemy(),
            AvatarAffinityRefs = availableAffinities,
            EnemyMind = new CombatAI(vm.CurrentWorld, BattleSeed),
            RandomSeed = BattleSeed,
            Avatar = avatar
        });
        Assert.True(startResult.Successful, $"StartBattle failed: {startResult.ErrorMessage}");
        Assert.True(startResult.Data.TryGetValue(TransactionDataKeys.BattleInstanceId, out var battleIdObj),
            "StartBattle result must carry the battle instance id (BattleModal reads it)");
        var battleId = (Guid)battleIdObj!;
        _output.WriteLine($"Battle {battleId} started with seed {BattleSeed}");

        // ================= FIGHT TO VICTORY (BattleModal turn loop) =================
        var state = await QueryBattleStateAsync(vm.Mediator, battleId, avatar);
        var reactionsSubmitted = 0;
        var attacksExecuted = 0;

        for (var round = 0; round < MaxRounds && !state.HasEnded; round++)
        {
            if (state.BattleState == BattleState.AwaitingReaction)
            {
                // The enemy telegraphed a real authored tell — react like the UI would.
                // (The query surfaces the persisted tell so a resuming UI can re-show
                // the telegraph; the live UI gets the tell text from the command result.)
                Assert.False(string.IsNullOrEmpty(state.PendingTellRefName),
                    "AwaitingReaction state must carry the persisted tell ref the UI resumes from");
                Assert.True(vm.CurrentWorld.Gameplay!.AttackTells!.Any(t => t.RefName == state.PendingTellRefName),
                    $"Pending tell '{state.PendingTellRefName}' must be real authored content");
                _output.WriteLine($"  TELL: {state.PendingTellRefName} (base damage {state.PendingTellBaseDamage:F3})");

                var reaction = await vm.Mediator.Send(new SubmitReactionCommand
                {
                    AvatarId = avatar.AvatarId,
                    ArcRef = ArcRef,
                    BattleInstanceId = battleId,
                    Reaction = AvatarDefenseType.Dodge,
                    Avatar = avatar
                });
                Assert.True(reaction.Successful, $"SubmitReaction failed: {reaction.ErrorMessage}");
                reactionsSubmitted++;
            }
            else
            {
                var turn = await vm.Mediator.Send(new ExecuteBattleTurnCommand
                {
                    AvatarId = avatar.AvatarId,
                    ArcRef = ArcRef,
                    BattleInstanceId = battleId,
                    AvatarAction = new CombatAction { ActionType = ActionType.Attack },
                    Avatar = avatar
                });
                Assert.True(turn.Successful, $"ExecuteBattleTurn failed: {turn.ErrorMessage}");
                attacksExecuted++;
            }

            state = await QueryBattleStateAsync(vm.Mediator, battleId, avatar);
            _output.WriteLine($"  Turn {state.TurnNumber}: avatar={state.AvatarCombatant?.Health:F3} " +
                              $"enemy={state.EnemyCombatant?.Health:F3} state={state.BattleState}");
        }

        _output.WriteLine($"Battle over after {attacksExecuted} attacks and {reactionsSubmitted} reactions");

        // ================= VICTORY ASSERTIONS =================
        Assert.True(state.HasEnded, $"Battle did not end within {MaxRounds} rounds");
        Assert.True(reactionsSubmitted > 0,
            "The Ise content authors attack tells and the hostile carries weapons — the telegraph/reaction path must have been exercised");
        Assert.Equal(BattleState.Victory, state.BattleState);
        Assert.True(state.AvatarVictory, "Avatar must win against the authored trail hostile");
        Assert.NotNull(state.AvatarCombatant);
        Assert.True(state.AvatarCombatant!.Health > 0f, "Avatar must survive the fight");
        Assert.NotNull(state.EnemyCombatant);
        Assert.True(state.EnemyCombatant!.Health <= 0f, "Enemy must be at zero health");

        // The defeat is durably recorded: BattleEnded + CharacterDefeated in the
        // committed transaction log (what the journal renders), and the boss-defeat
        // projection the rest of the game reads.
        var committed = (await _host.Repositories.ArcRepository.GetOrCreateInstanceAsync(avatar.AvatarId, ArcRef))
            .GetCommittedTransactions();

        var battleEnded = Assert.Single(committed, t =>
            t.Type == ArcTransactionType.BattleEnded &&
            t.Data[TransactionDataKeys.BattleTransactionId] == battleId.ToString());
        Assert.Equal(bool.TrueString, battleEnded.Data[TransactionDataKeys.AvatarVictory]);

        var defeated = Assert.Single(committed, t => t.Type == ArcTransactionType.CharacterDefeated);
        Assert.Equal(EnemyRef, defeated.Data[TransactionDataKeys.CharacterRef]);

        Assert.True(_host.Repositories.AvatarProgressRepository.GetBossDefeatedCount(avatar.AvatarId, EnemyRef) >= 1,
            "CharacterDefeated must project into the avatar progress table");

        // The view model's own character query now reports the corpse (IsAlive=false) —
        // this is what flips CanDialogue/CanAttack off in the rendered marker list.
        var afterBattle = await vm.Mediator.Send(new GetSpawnedCharactersQuery
        {
            AvatarId = avatar.AvatarId,
            ArcRef = ArcRef,
            SpawnedOnly = true,
            AliveOnly = false
        });
        var corpse = Assert.Single(afterBattle, c => c.CharacterInstanceId == enemyState.CharacterInstanceId);
        Assert.False(corpse.IsAlive);

        // Victory-loot seam: the replayed state records THIS avatar as the victor of
        // the defeated spawn instance — the gate for the free-take trade path (the
        // host offers the corpse's remaining Interactable.Loot via MerchantTradeModal).
        // The vendored Ise hostile authors no Loot, so the drop itself is empty here;
        // loot collection over seeded content is covered by
        // VictoryLootTradeViewModelTests and the engine's victory-loot tests.
        var arcState = await vm.Mediator.Send(new GetArcStateQuery
        {
            AvatarId = avatar.AvatarId,
            ArcRef = ArcRef
        });
        Assert.NotNull(arcState);
        var victorRecorded = arcState!.Characters[enemyState.CharacterInstanceId.ToString()];
        Assert.False(victorRecorded.IsAlive);
        Assert.Equal(avatar.AvatarId.ToString(), victorRecorded.DefeatedByAvatarId);

        // ================= RELOAD: reconstruction from the log =================
        // Discard EVERYTHING in memory (service provider, mediator, repositories,
        // read-model cache, view model) and rebuild over the same LiteDB file — the
        // reconstruction-from-absolute-snapshots seam. The re-queried battle state must
        // match what the live session last showed.
        var before = state;
        _host = await _host.RestartAsync(WorldRef);
        var reloadedVm = _host.ViewModel;
        reloadedVm.Avatar = avatar;

        var after = await QueryBattleStateAsync(reloadedVm.Mediator, battleId, avatar);

        Assert.True(after.HasEnded);
        Assert.Equal(before.BattleState, after.BattleState);
        Assert.Equal(before.AvatarVictory, after.AvatarVictory);
        Assert.Equal(before.TurnNumber, after.TurnNumber);
        Assert.NotNull(after.AvatarCombatant);
        Assert.NotNull(after.EnemyCombatant);
        Assert.Equal(before.AvatarCombatant!.Health, after.AvatarCombatant!.Health, 3);
        Assert.Equal(before.AvatarCombatant.Stamina, after.AvatarCombatant.Stamina, 3);
        Assert.Equal(before.EnemyCombatant!.Health, after.EnemyCombatant!.Health, 3);
        Assert.Equal(before.BattleLog.Count, after.BattleLog.Count);

        // And the defeat survives the reload end-to-end (journal + projection again,
        // this time rebuilt purely from disk).
        var reloadedCommitted = (await _host.Repositories.ArcRepository.GetOrCreateInstanceAsync(avatar.AvatarId, ArcRef))
            .GetCommittedTransactions();
        Assert.Contains(reloadedCommitted, t => t.Type == ArcTransactionType.CharacterDefeated);
        Assert.True(_host.Repositories.AvatarProgressRepository.GetBossDefeatedCount(avatar.AvatarId, EnemyRef) >= 1);
    }

    /// <summary>
    /// Regression for the recruited-companions-never-fight bug: BattleModal.
    /// InitializeBattleAsync used to ignore avatar.Party, so the engine's (tested)
    /// companion turn-loop always ran with an empty roster. This drives the SAME
    /// InitializeBattleAsync sequence — now including the party → BattleSetup.
    /// CompanionCharacters → StartBattleCommand.CompanionCombatants mapping — with a
    /// real recruited companion, and asserts the companion actually enters the battle
    /// (both the mapped combatant list and the reconstructed live battle state carry it).
    /// </summary>
    [Fact]
    public async Task StartBattle_WithRecruitedParty_CompanionEntersTheFight()
    {
        var vm = _host.ViewModel;
        var avatar = _host.CreateAvatarFromWorldArchetype("Warrior", "E2E Party Leader");

        // Recruit a real world character as a party member. A companion needs Stats for
        // BattleSetup to build a combatant, so pick any authored non-hostile with Stats.
        var companionTemplate = vm.CurrentWorld!.Gameplay!.Characters!
            .First(c => c.RefName != EnemyRef && c.Stats != null);
        avatar.Party = new PartyInventory
        {
            Member = new[]
            {
                new PartyMember { SlotRef = "Party1", CharacterRef = companionTemplate.RefName }
            }
        };
        _output.WriteLine($"Recruited companion: {companionTemplate.RefName} ({companionTemplate.DisplayName})");

        // ================= ENTER THE ZONE (real trigger spawn) =================
        var arc = vm.CurrentWorld.ArcLookup[ArcRef];
        var enter = await vm.Mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatar.AvatarId,
            ArcRef = ArcRef,
            Latitude = arc.Latitude,
            Longitude = arc.Longitude,
            Avatar = avatar
        });
        Assert.True(enter.Successful, $"Zone entry failed: {enter.ErrorMessage}");

        var spawned = await vm.Mediator.Send(new GetSpawnedCharactersQuery
        {
            AvatarId = avatar.AvatarId,
            ArcRef = ArcRef,
            SpawnedOnly = true,
            AliveOnly = true
        });
        var enemyState = Assert.Single(spawned, c => c.CharacterRef == EnemyRef);

        // ================= START THE BATTLE (BattleModal.InitializeBattleAsync) =================
        var enemyTemplate = vm.CurrentWorld.Gameplay!.Characters!.First(c => c.RefName == EnemyRef);
        var archetype = vm.CurrentWorld.Gameplay.AvatarArchetypes!.First(a => a.RefName == avatar.ArchetypeRef);

        var battleSetup = new BattleSetup();
        battleSetup.SetupFromWorld(vm.CurrentWorld);
        battleSetup.SelectedAvatarArchetype = archetype;
        battleSetup.AvatarCapabilities = avatar.Capabilities ?? new ItemCollection();
        var availableAffinities = vm.CurrentWorld.Gameplay.CharacterAffinities?
            .Select(a => a.RefName).ToList() ?? new List<string>();
        if (availableAffinities.Count == 0 && !string.IsNullOrEmpty(archetype.AffinityRef))
        {
            availableAffinities.Add(archetype.AffinityRef);
        }
        battleSetup.AvatarAffinityRefs = availableAffinities;
        battleSetup.SelectedOpponentCharacter = enemyTemplate;
        battleSetup.OpponentCapabilities = enemyTemplate.Capabilities ?? new ItemCollection();

        // The party → companion mapping BattleModal.InitializeBattleAsync now performs.
        foreach (var member in avatar.Party.Member)
        {
            var template = vm.CurrentWorld.Gameplay.Characters!
                .FirstOrDefault(c => c.RefName == member.CharacterRef);
            if (template != null)
            {
                battleSetup.CompanionCharacters.Add(template);
            }
        }

        var battleEngine = battleSetup.CreateBattleEngine();
        var companionCombatants = battleEngine.GetCompanions().ToList();

        // The fix: the recruited party member was mapped to a battle combatant.
        Assert.Contains(companionCombatants, c => c.RefName == companionTemplate.RefName);

        var startResult = await vm.Mediator.Send(new StartBattleCommand
        {
            AvatarId = avatar.AvatarId,
            ArcRef = ArcRef,
            EnemyCharacterInstanceId = enemyState.CharacterInstanceId,
            AvatarCombatant = battleEngine.GetAvatar(),
            EnemyCombatant = battleEngine.GetEnemy(),
            AvatarAffinityRefs = availableAffinities,
            EnemyMind = new CombatAI(vm.CurrentWorld, BattleSeed),
            RandomSeed = BattleSeed,
            Avatar = avatar,
            CompanionCombatants = companionCombatants.Count > 0 ? companionCombatants : null
        });
        Assert.True(startResult.Successful, $"StartBattle failed: {startResult.ErrorMessage}");
        var battleId = (Guid)startResult.Data[TransactionDataKeys.BattleInstanceId]!;

        // The companion is now part of the live battle the engine runs: the reconstructed
        // state (rebuilt from the committed transactions) carries the companion combatant.
        var state = await QueryBattleStateAsync(vm.Mediator, battleId, avatar);
        Assert.Contains(state.Companions, c => c.RefName == companionTemplate.RefName);
    }

    private static async Task<BattleStateResult> QueryBattleStateAsync(
        MediatR.IMediator mediator, Guid battleId, Ambient.Domain.Entities.AvatarEntity avatar)
    {
        var state = await mediator.Send(new GetBattleStateQuery
        {
            AvatarId = avatar.AvatarId,
            ArcRef = ArcRef,
            BattleInstanceId = battleId,
            Avatar = avatar
        });
        Assert.NotNull(state);
        Assert.True(string.IsNullOrEmpty(state.ErrorMessage), $"GetBattleState error: {state.ErrorMessage}");
        return state;
    }
}
