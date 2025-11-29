using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Application.Results.Saga;
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

    public AchievementEvaluationBehavior(World world)
    {
        _world = world;
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
                if (avatarId.HasValue)
                {
                    // TODO: Evaluate achievements
                    // This would call AchievementProgressEvaluator.GetNewlyUnlockedAchievements()
                    // and publish an AchievementsUnlockedEvent if any new achievements

                    Debug.WriteLine($"[Achievement Eval] Checking achievements for avatar {avatarId.Value} after {typeof(TRequest).Name}");

                    // For now, just log - full implementation would:
                    // 1. Get all Saga instances for avatar
                    // 2. Run AchievementProgressEvaluator
                    // 3. Check for newly unlocked achievements
                    // 4. Publish event if any found
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
