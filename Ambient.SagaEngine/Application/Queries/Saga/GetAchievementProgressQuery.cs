using MediatR;

namespace Ambient.SagaEngine.Application.Queries.Saga;

/// <summary>
/// Query to get achievement progress for an avatar across all Sagas.
/// Uses transaction logs to compute progress dynamically.
/// </summary>
public record GetAchievementProgressQuery : IRequest<List<AchievementProgressDto>>
{
    /// <summary>
    /// Avatar to check progress for
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Filter: Only include achievements with progress > 0
    /// </summary>
    public bool OnlyStarted { get; init; } = false;

    /// <summary>
    /// Filter: Only include unlocked achievements
    /// </summary>
    public bool OnlyUnlocked { get; init; } = false;
}

/// <summary>
/// DTO representing achievement progress.
/// </summary>
public record AchievementProgressDto
{
    public required string AchievementRef { get; init; }
    public required string DisplayName { get; init; }
    public required float Progress { get; init; }  // 0.0 to 1.0
    public required bool IsUnlocked { get; init; }
    public required float CurrentValue { get; init; }
    public required float Threshold { get; init; }
}
