using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain.Achievements;
using MediatR;
using System.Diagnostics;

namespace Ambient.Saga.Engine.Application.Behaviors;

/// <summary>
/// Pipeline behavior that automatically evaluates achievements after Saga commands.
/// Runs AFTER command succeeds, checks for newly unlocked achievements.
/// </summary>
public class AchievementEvaluationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly World _world;
    private readonly ISagaInstanceRepository _sagaRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;

    public AchievementEvaluationBehavior(
        World world,
        ISagaInstanceRepository sagaRepository,
        IAvatarUpdateService avatarUpdateService)
    {
        _world = world;
        _sagaRepository = sagaRepository;
        _avatarUpdateService = avatarUpdateService;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Execute command first
        var response = await next();

        // Only evaluate achievements for successful Saga commands
        if (response is SagaCommandResult commandResult && commandResult.Successful)
        {
            try
            {
                // Extract avatar ID from command (if available)
                var avatarId = GetAvatarIdFromCommand(request);
                if (avatarId.HasValue && _world.Gameplay?.Achievements != null)
                {
                    await EvaluateAchievementsAsync(avatarId.Value, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // Don't fail the command if achievement evaluation fails
                Debug.WriteLine($"[Achievement Eval] Error evaluating achievements: {ex.Message}");
            }
        }

        return response;
    }

    private async Task EvaluateAchievementsAsync(Guid avatarId, CancellationToken cancellationToken)
    {
        // Get all Saga instances for this avatar
        var sagaInstances = await _sagaRepository.GetAllInstancesForAvatarAsync(avatarId, cancellationToken);
        if (sagaInstances.Count == 0)
            return;

        // Get achievements from world
        var achievements = _world.Gameplay?.Achievements;
        if (achievements == null || achievements.Length == 0)
            return;

        // Get previous achievement state from avatar
        var previousInstances = await _avatarUpdateService.GetAchievementInstancesAsync(avatarId, cancellationToken);

        // Find newly unlocked achievements
        var newlyUnlocked = AchievementProgressEvaluator.GetNewlyUnlockedAchievements(
            achievements,
            previousInstances,
            sagaInstances,
            _world,
            avatarId.ToString());

        if (newlyUnlocked.Count > 0)
        {
            Debug.WriteLine($"[Achievement Eval] Avatar {avatarId} unlocked {newlyUnlocked.Count} achievement(s): {string.Join(", ", newlyUnlocked.Select(a => a.DisplayName))}");

            // Update avatar's achievement instances
            var allInstances = AchievementProgressEvaluator.EvaluateAllAchievements(
                achievements,
                sagaInstances,
                _world,
                avatarId.ToString());

            await _avatarUpdateService.UpdateAchievementInstancesAsync(avatarId, allInstances, cancellationToken);
        }
    }

    private static Guid? GetAvatarIdFromCommand(TRequest request)
    {
        // Use reflection to extract AvatarId property if it exists
        var avatarIdProp = typeof(TRequest).GetProperty("AvatarId");
        if (avatarIdProp != null && avatarIdProp.PropertyType == typeof(Guid))
        {
            return (Guid?)avatarIdProp.GetValue(request);
        }
        return null;
    }
}
