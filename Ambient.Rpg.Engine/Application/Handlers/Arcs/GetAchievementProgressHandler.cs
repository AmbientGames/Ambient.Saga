using MediatR;
using Ambient.Rpg.Engine.Domain.Achievements;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using Ambient.Domain.Contracts;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for GetAchievementProgressQuery.
/// Evaluates achievement progress by querying transaction logs.
/// </summary>
internal sealed class GetAchievementProgressHandler : IRequestHandler<GetAchievementProgressQuery, List<AchievementProgressDto>>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IWorld _world;

    public GetAchievementProgressHandler(
        IArcInstanceRepository instanceRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _world = world;
    }

    public async Task<List<AchievementProgressDto>> Handle(GetAchievementProgressQuery query, CancellationToken ct)
    {
        try
        {
            // Get all arc instances for this avatar
            var allInstances = await _instanceRepository.GetAllInstancesForAvatarAsync(query.AvatarId, ct);

            // Get all achievements from World.Gameplay.Achievements
            var achievements = _world.Gameplay.Achievements;
            if (achievements == null || !achievements.Any())
            {
                return new List<AchievementProgressDto>();
            }

            var results = new List<AchievementProgressDto>();

            foreach (var achievement in achievements)
            {
                // Evaluate progress using the achievement evaluator (returns 0.0-1.0)
                var progressPercent = AchievementProgressEvaluator.EvaluateProgress(
                    achievement,
                    allInstances,
                    _world,
                    query.AvatarId.ToString());

                // Apply filters
                if (query.OnlyStarted && progressPercent == 0f)
                    continue;

                if (query.OnlyUnlocked && progressPercent < 1f)
                    continue;

                // Calculate current value based on criteria threshold
                var threshold = achievement.Criteria?.Threshold ?? 1;
                var currentValue = progressPercent * threshold;

                results.Add(new AchievementProgressDto
                {
                    AchievementRef = achievement.RefName,
                    DisplayName = achievement.DisplayName,
                    Progress = progressPercent,
                    IsUnlocked = progressPercent >= 1f,
                    CurrentValue = currentValue,
                    Threshold = threshold
                });
            }

            return results;
        }
        catch (Exception)
        {
            return new List<AchievementProgressDto>();
        }
    }
}
