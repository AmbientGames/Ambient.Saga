using Ambient.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Sagas;
using Ambient.Saga.Engine.Tests.Mocks;

namespace Ambient.Saga.Engine.Tests.Rpg.Sagas;

public class TriggerAvailabilityCheckerTests
{
    private readonly MockAvatarProgressRepository _repo = new();
    private readonly Guid _avatarId = Guid.NewGuid();

    private static SagaTrigger MakeTrigger(params string[] requiredTokens) => new()
    {
        RefName = "TestTrigger",
        EnterRadius = 10.0f,
        RequiresQuestTokenRef = requiredTokens.Length > 0 ? requiredTokens : null
    };

    [Fact]
    public void NoRequiredTokens_ReturnsTrue()
    {
        var trigger = MakeTrigger();

        var result = TriggerAvailabilityChecker.CanActivate(trigger, _repo, _avatarId);

        Assert.True(result);
    }

    [Fact]
    public void SingleTokenRequired_AvatarHasIt_ReturnsTrue()
    {
        var trigger = MakeTrigger("TOKEN_A");
        _repo.SetQuestToken(_avatarId, "TOKEN_A");

        var result = TriggerAvailabilityChecker.CanActivate(trigger, _repo, _avatarId);

        Assert.True(result);
    }

    [Fact]
    public void SingleTokenRequired_AvatarMissing_ReturnsFalse()
    {
        var trigger = MakeTrigger("TOKEN_A");

        var result = TriggerAvailabilityChecker.CanActivate(trigger, _repo, _avatarId);

        Assert.False(result);
    }

    [Fact]
    public void MultipleTokensRequired_AvatarHasAll_ReturnsTrue()
    {
        var trigger = MakeTrigger("TOKEN_A", "TOKEN_B", "TOKEN_C");
        _repo.SetQuestToken(_avatarId, "TOKEN_A");
        _repo.SetQuestToken(_avatarId, "TOKEN_B");
        _repo.SetQuestToken(_avatarId, "TOKEN_C");

        var result = TriggerAvailabilityChecker.CanActivate(trigger, _repo, _avatarId);

        Assert.True(result);
    }

    [Fact]
    public void MultipleTokensRequired_AvatarMissingOne_ReturnsFalse()
    {
        var trigger = MakeTrigger("TOKEN_A", "TOKEN_B", "TOKEN_C");
        _repo.SetQuestToken(_avatarId, "TOKEN_A");
        _repo.SetQuestToken(_avatarId, "TOKEN_C");

        var result = TriggerAvailabilityChecker.CanActivate(trigger, _repo, _avatarId);

        Assert.False(result);
    }

    [Fact]
    public void NullTrigger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TriggerAvailabilityChecker.CanActivate(null!, _repo, _avatarId));
    }
}
