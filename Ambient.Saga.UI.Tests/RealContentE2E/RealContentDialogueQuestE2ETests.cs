using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Queries.Loading;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Domain;
using Ambient.Saga.Engine.Domain.AvatarProgress;
using Ambient.Saga.Engine.Domain.Rpg.Quests;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Xunit.Abstractions;

namespace Ambient.Saga.UI.Tests.RealContentE2E;

/// <summary>
/// Full conversation E2E at the view-model layer over REAL world content (the vendored
/// Ise world: real Dialogue.xml / Quests.xml / Characters.xml / Sagas.xml loaded through
/// the real schema-validating loader).
///
/// Drives the EXACT command/query sequence the dialogue UI issues, in the same order:
/// DialogueModal (StartDialogueCommand → GetDialogueStateQuery → SelectDialogueChoiceCommand
/// → re-query, checking result data for GameComplete/PendingEvents each step), plus the
/// quest log through SagaMainViewModel.QuestLog and the quest detail modal's
/// GetQuestProgressQuery.
///
/// WHY THIS EXISTS: handler-level integration tests build synthetic worlds in C#, so a
/// content bug (e.g. a dialogue action attribute the schema doesn't have) crashed real
/// conversations at runtime while every handler suite stayed green. This test walks a
/// real authored conversation node by node — any action/condition in that path that the
/// engine can't execute fails HERE.
///
/// Content used: Ise → character "FalseProphet_Concerned" (Worried Chaperone), dialogue
/// tree FalseProphet_Start, quest FalseProphet_Quest ("The Urban Legend"). The accepting
/// node runs BOTH `AcceptQuest FalseProphet_Quest` and `GiveQuestToken FalseProphet_Rumor`,
/// which is exactly the shape the ScopeToCurrentAcceptance fix addresses (the token
/// commits with a LOWER sequence number than QuestAccepted).
/// </summary>
[Collection("Real Content E2E")]
public class RealContentDialogueQuestE2ETests : IAsyncLifetime, IDisposable
{
    private const string WorldRef = "Ise";
    private const string SagaRef = "Kazahinominomiya";           // real Ise arc (first shrine)
    private const string QuestGiverRef = "FalseProphet_Concerned";
    private const string QuestRef = "FalseProphet_Quest";
    private const string AcceptTokenRef = "FalseProphet_Rumor";

    private readonly ITestOutputHelper _output;
    private RealContentE2EHost _host = null!;

    public RealContentDialogueQuestE2ETests(ITestOutputHelper output)
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
    public async Task FullConversation_QuestOfferWalk_AcceptQuest_QuestActive_TokenScoped_SessionSealed()
    {
        var vm = _host.ViewModel;
        var avatar = _host.CreateAvatarFromWorldArchetype("Warrior", "E2E Dialogue Hero");

        // Sanity: the real world content is discoverable through the same query the
        // view model uses to populate its world-selection screen.
        var configurations = await vm.Mediator.Send(new LoadAvailableWorldConfigurationsQuery
        {
            DataDirectory = Path.Combine(AppContext.BaseDirectory, "Content", "Worlds"),
            DefinitionDirectory = Path.Combine(AppContext.BaseDirectory, "Content", "xsd")
        });
        Assert.Contains(configurations, c => c.RefName == WorldRef);

        // ARRANGE: put the quest giver into a REAL arc's transaction log.
        // The vendored generated worlds only trigger-spawn generic NPCs/hostiles; the
        // quest-line characters are reached in the game via the dev-spawn UI, which
        // isolates them in a "__DEV__" arc that the quest log doesn't read. Seeding the
        // identical CharacterSpawned transaction (same payload SpawnDevCharacterHandler
        // writes) into the real arc keeps every ACTED step below — dialogue, quest
        // accept, quest log, quest progress — on the exact real-arc path the UI drives
        // for trigger-spawned characters.
        var giverInstanceId = Guid.NewGuid();
        var instance = await _host.Repositories.SagaRepository.GetOrCreateInstanceAsync(avatar.AvatarId, SagaRef);
        var spawnTx = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterSpawned,
            AvatarId = avatar.AvatarId.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterInstanceId] = giverInstanceId.ToString(),
                [TransactionDataKeys.CharacterRef] = QuestGiverRef,
                [TransactionDataKeys.SagaTriggerRef] = "DEV_TRIGGER",
                [TransactionDataKeys.X] = "0",
                [TransactionDataKeys.Z] = "0",
                [TransactionDataKeys.SpawnHeight] = "0",
                [TransactionDataKeys.IsDevSpawn] = "true"
            }
        };
        await _host.Repositories.SagaRepository.AddAndCommitTransactionsAsync(
            instance.InstanceId, new List<SagaTransaction> { spawnTx });

        // The character must be visible through the view model's own character query
        // (the one CheckAvailableInteractionsAsync issues every tick).
        var spawned = await vm.Mediator.Send(new GetSpawnedCharactersQuery
        {
            AvatarId = avatar.AvatarId,
            SagaRef = SagaRef,
            SpawnedOnly = true,
            AliveOnly = false
        });
        var giverState = Assert.Single(spawned, c => c.CharacterRef == QuestGiverRef);
        Assert.True(giverState.IsAlive);

        var giverTemplate = vm.CurrentWorld!.CharactersLookup[QuestGiverRef];
        Assert.False(string.IsNullOrEmpty(giverTemplate.Interactable?.DialogueTreeRef),
            "Quest giver must advertise dialogue (CanDialogue derives from this in the view model)");

        // ================= CONVERSATION 1: walk to the quest offer =================
        // Exactly what DialogueModal.InitializeDialogueAsync sends.
        var start = await vm.Mediator.Send(new StartDialogueCommand
        {
            AvatarId = vm.Avatar!.AvatarId,
            SagaArcRef = SagaRef,
            CharacterInstanceId = giverInstanceId,
            Avatar = vm.Avatar
        });
        Assert.True(start.Successful, $"StartDialogue failed: {start.ErrorMessage}");

        // Step 1 — hub node. The player must see EXACTLY one choice: the
        // "[Quest not started]" route (the "[Quest active]" route is condition-filtered
        // because QuestActive is false). This is the choice list the UI renders.
        var state = await QueryDialogueStateAsync(giverInstanceId);
        Assert.Equal("worry_hub", state.CurrentNodeId);
        Assert.Contains(state.DialogueText, t => t.Contains("worried elder"));
        var hubChoice = Assert.Single(state.Choices);
        Assert.Equal("[Quest not started]", hubChoice.Text);
        Assert.True(hubChoice.IsAvailable);
        LogStep(state);

        // Step 2 — the quest hook. Both authored choices visible and selectable.
        state = await SelectChoiceAsync(giverInstanceId, hubChoice.ChoiceId);
        Assert.Equal("worry_not_started", state.CurrentNodeId);
        Assert.Contains(state.DialogueText, t => t.Contains("prophet"));
        Assert.Equal(
            new[] { "What's wrong with that?", "You don't believe them?" },
            state.Choices.Select(c => c.Text).ToArray());
        Assert.All(state.Choices, c => Assert.True(c.IsAvailable));
        LogStep(state);

        // Step 3 — take the "doubt" branch.
        var doubt = state.Choices.Single(c => c.Text == "You don't believe them?");
        state = await SelectChoiceAsync(giverInstanceId, doubt.ChoiceId);
        Assert.Equal("doubt", state.CurrentNodeId);
        Assert.Equal(
            new[] { "I can look into this.", "Maybe she's found her path." },
            state.Choices.Select(c => c.Text).ToArray());
        Assert.All(state.Choices, c => Assert.True(c.IsAvailable));
        LogStep(state);

        // Step 4 — agree to help.
        var help = state.Choices.Single(c => c.Text == "I can look into this.");
        state = await SelectChoiceAsync(giverInstanceId, help.ChoiceId);
        Assert.Equal("ask_help", state.CurrentNodeId);
        Assert.Equal(
            new[] { "I'll find out what's really happening.", "I need time to think." },
            state.Choices.Select(c => c.Text).ToArray());
        LogStep(state);

        // Step 5 — ACCEPT. The accepting node executes the real authored actions
        // (AcceptQuest + GiveQuestToken) and, being terminal, seals the session.
        var accept = state.Choices.Single(c => c.Text == "I'll find out what's really happening.");
        var acceptResult = await vm.Mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = vm.Avatar.AvatarId,
            SagaArcRef = SagaRef,
            CharacterInstanceId = giverInstanceId,
            ChoiceId = accept.ChoiceId,
            Avatar = vm.Avatar
        });
        Assert.True(acceptResult.Successful, $"Accept choice failed: {acceptResult.ErrorMessage}");
        Assert.False(acceptResult.Data.ContainsKey(TransactionDataKeys.GameComplete));
        if (acceptResult.Data.TryGetValue(TransactionDataKeys.QuestEventErrors, out var questErrors))
        {
            Assert.True(questErrors is System.Collections.ICollection { Count: 0 },
                $"Quest events raised errors: {questErrors}");
        }

        // (d) The session seals cleanly: the read model the modal renders from reports
        // the conversation over, and the committed log shows no dangling session — the
        // same invariant StartDialogueHandler checks (last DialogueCompleted must come
        // after the last DialogueStarted).
        state = await QueryDialogueStateAsync(giverInstanceId);
        Assert.True(state.HasEnded, "Accepting terminal node must end the conversation");
        Assert.False(state.IsActive, "Session must be sealed (DialogueCompleted committed)");

        var committed = (await _host.Repositories.SagaRepository.GetOrCreateInstanceAsync(avatar.AvatarId, SagaRef))
            .GetCommittedTransactions();
        var lastStarted = committed.Where(t => t.Type == SagaTransactionType.DialogueStarted)
            .OrderBy(t => t.SequenceNumber).Last();
        var lastCompleted = committed.Where(t => t.Type == SagaTransactionType.DialogueCompleted)
            .OrderBy(t => t.SequenceNumber).LastOrDefault();
        Assert.NotNull(lastCompleted);
        Assert.True(lastCompleted!.SequenceNumber > lastStarted.SequenceNumber,
            "Session must not be left dangling: DialogueCompleted must seal the accepting session");
        var acceptedTx = Assert.Single(committed, t => t.Type == SagaTransactionType.QuestAccepted);
        Assert.Equal(QuestRef, acceptedTx.Data[TransactionDataKeys.QuestRef]);

        // (b) The quest is ACTIVE afterwards — through the state query the quest log
        // uses, and through the actual QuestLogViewModel on the main view model.
        var sagaState = await vm.Mediator.Send(new GetSagaStateQuery
        {
            AvatarId = avatar.AvatarId,
            SagaRef = SagaRef
        });
        Assert.NotNull(sagaState);
        Assert.True(sagaState!.ActiveQuests.ContainsKey(QuestRef), "Quest must be active in the arc that accepted it");

        await vm.QuestLog!.RefreshQuestsAsync();
        var questItem = Assert.Single(vm.QuestLog.ActiveQuests, q => q.RefName == QuestRef);
        Assert.Equal("The Urban Legend", questItem.DisplayName);
        Assert.True(vm.QuestLog.HasActiveQuests);

        // The cross-arc progress projection (what dialogue gates elsewhere read) sees
        // both the acceptance and the token granted in the accepting conversation.
        var progressRepo = _host.Repositories.AvatarProgressRepository;
        Assert.Equal(QuestProgressStatus.Active, progressRepo.GetQuestStatus(avatar.AvatarId, QuestRef));
        Assert.True(progressRepo.HasQuestToken(avatar.AvatarId, AcceptTokenRef),
            "Token granted by the accepting node must project to the avatar progress table");

        // (c) Session-scoping seam (ScopeToCurrentAcceptance now starts at the accepting
        // dialogue SESSION, not at the QuestAccepted transaction): everything the
        // accepting conversation committed counts toward the current acceptance —
        // including transactions sequenced BEFORE QuestAccepted (the node visits of the
        // walk above), which the old strict from-accept scope excluded forever. The
        // token granted by the accepting node must be in scope too.
        var tokenTx = Assert.Single(committed, t =>
            t.Type == SagaTransactionType.QuestTokenAwarded &&
            t.Data.TryGetValue(TransactionDataKeys.QuestTokenRef, out var tokenRef) &&
            tokenRef == AcceptTokenRef);

        var quest = vm.CurrentWorld.QuestsLookup[QuestRef];
        var scoped = QuestProgressEvaluator.ScopeToCurrentAcceptance(quest, committed);
        Assert.Contains(scoped, t => t.TransactionId == tokenTx.TransactionId);

        // Pre-acceptance transactions from the SAME session are in scope — the direct
        // probe that the scope opens at the session's DialogueStarted.
        var hubVisit = committed.First(t =>
            t.Type == SagaTransactionType.DialogueNodeVisited &&
            t.Data.TryGetValue(TransactionDataKeys.DialogueNodeId, out var nodeId) && nodeId == "worry_hub");
        Assert.True(hubVisit.SequenceNumber < acceptedTx.SequenceNumber,
            "The conversation's first node visit precedes the acceptance in the log");
        Assert.Contains(scoped, t => t.TransactionId == hubVisit.TransactionId);

        // The quest detail modal's progress query evaluates stage 1 over that scoped
        // log against the REAL quest template. Ise's "hear_rumors" objective references
        // QuestTokenRef="FalseProphet_Rumor" — the very token the accepting node granted
        // in THIS session, BEFORE the QuestAccepted transaction. Seeing it complete here
        // directly exercises the ScopeToCurrentAcceptance session-scoping fix: the old
        // strict from-accept scope would have excluded that grant forever.
        var progress = await vm.Mediator.Send(new GetQuestProgressQuery
        {
            AvatarId = avatar.AvatarId,
            SagaRef = SagaRef,
            QuestRef = QuestRef
        });
        Assert.NotNull(progress);
        Assert.False(progress!.IsComplete);
        Assert.Equal("Gather Information", progress.CurrentStageDisplayName);
        var hearRumors = Assert.Single(progress.Objectives, o => o.ObjectiveRef == "hear_rumors");
        Assert.True(hearRumors.CurrentValue >= 1,
            "hear_rumors must count the FalseProphet_Rumor token granted by the accepting conversation");
        Assert.True(hearRumors.IsComplete,
            "hear_rumors (threshold 1) must complete from the token granted in the accepting session");
        Assert.Contains(progress.Objectives, o => o.ObjectiveRef == "meet_scholar" && !o.IsComplete);

        // ================= CONVERSATION 2: player-visible quest-active gating =================
        // Reopening the conversation must now route through the "[Quest active]" branch —
        // the choice list the player SEES proves the accepted state feeds back into
        // real dialogue conditions.
        var start2 = await vm.Mediator.Send(new StartDialogueCommand
        {
            AvatarId = vm.Avatar.AvatarId,
            SagaArcRef = SagaRef,
            CharacterInstanceId = giverInstanceId,
            Avatar = vm.Avatar
        });
        Assert.True(start2.Successful, $"Second StartDialogue failed: {start2.ErrorMessage}");

        state = await QueryDialogueStateAsync(giverInstanceId);
        Assert.Equal("worry_hub", state.CurrentNodeId);
        var activeChoice = Assert.Single(state.Choices);
        Assert.Equal("[Quest active]", activeChoice.Text);
        LogStep(state);

        state = await SelectChoiceAsync(giverInstanceId, activeChoice.ChoiceId);
        Assert.Equal("worry_active_end", state.CurrentNodeId);
        Assert.Contains(state.DialogueText, t => t.Contains("Have you learned anything?"));
        Assert.True(state.HasEnded, "The quest-active acknowledgement is a terminal node");
        LogStep(state);
    }

    #region UI-sequence helpers (mirror DialogueModal exactly)

    private async Task<DialogueStateResult> QueryDialogueStateAsync(Guid characterInstanceId)
    {
        var vm = _host.ViewModel;
        var state = await vm.Mediator.Send(new GetDialogueStateQuery
        {
            AvatarId = vm.Avatar!.AvatarId,
            SagaRef = SagaRef,
            CharacterInstanceId = characterInstanceId,
            Avatar = vm.Avatar
        });
        Assert.NotNull(state);
        return state;
    }

    /// <summary>
    /// Select a choice and re-query state — the same round trip
    /// DialogueModal.SelectChoiceAsync performs (including the modal's checks on the
    /// command result before it refreshes).
    /// </summary>
    private async Task<DialogueStateResult> SelectChoiceAsync(Guid characterInstanceId, string choiceId)
    {
        var vm = _host.ViewModel;
        var result = await vm.Mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = vm.Avatar!.AvatarId,
            SagaArcRef = SagaRef,
            CharacterInstanceId = characterInstanceId,
            ChoiceId = choiceId,
            Avatar = vm.Avatar
        });
        Assert.True(result.Successful, $"SelectDialogueChoice('{choiceId}') failed: {result.ErrorMessage}");
        Assert.False(result.Data.ContainsKey(TransactionDataKeys.GameComplete));
        return await QueryDialogueStateAsync(characterInstanceId);
    }

    private void LogStep(DialogueStateResult state)
    {
        _output.WriteLine($"[{state.CurrentNodeId}]");
        foreach (var text in state.DialogueText)
        {
            _output.WriteLine($"  NPC: {text}");
        }
        foreach (var choice in state.Choices)
        {
            _output.WriteLine($"  ({(choice.IsAvailable ? "x" : " ")}) {choice.Text}");
        }
    }

    #endregion
}
