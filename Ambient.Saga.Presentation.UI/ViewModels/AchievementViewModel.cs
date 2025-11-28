using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.SagaEngine.Contracts;
using Ambient.SagaEngine.Domain.Rpg.Sagas;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Ambient.Presentation.WindowsUI.RpgControls.ViewModels;

/// <summary>
/// Represents a single achievement in the UI.
/// </summary>
public class AchievementDisplayItem
{
    public string RefName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsUnlocked { get; set; }
    public float ProgressPercentage { get; set; }
    public string? UnlockedDate { get; set; }

    // Criteria information
    public AchievementCriteriaType CriteriaType { get; set; }
    public float Threshold { get; set; }
    public float CurrentValue { get; set; }

    public string CriteriaText => GetCriteriaText();
    public string ProgressText => IsUnlocked ? "Unlocked" : $"{CurrentValue:F0} / {Threshold:F0}";
    public string StatusText => IsUnlocked
        ? $"Unlocked on {UnlockedDate ?? "Unknown"}"
        : $"{ProgressPercentage:F1}% Complete";

    private string GetCriteriaText()
    {
        return CriteriaType switch
        {
            AchievementCriteriaType.BlocksPlaced => $"Place {Threshold:F0} blocks",
            AchievementCriteriaType.BlocksDestroyed => $"Destroy {Threshold:F0} blocks",
            AchievementCriteriaType.DistanceTraveled => $"Travel {Threshold:F0} meters",
            AchievementCriteriaType.PlayTimeHours => $"Play for {Threshold:F0} hours",
            _ => $"Reach {Threshold:F0}"
        };
    }
}

/// <summary>
/// ViewModel for achievement display.
/// Tracks achievement unlock status and progress towards completion.
/// Integrated with LiteDB persistence and Steam achievements.
/// </summary>
public partial class AchievementViewModel : ObservableObject
{
    private SagaInteractionContext _context;
    private IWorldStateRepository? _worldRepository; // WorldStateRepository from Schema.Sandbox (optional, for persistence)
    private ISteamAchievementService? _steamService; // SteamAchievementService from Schema.Sandbox (optional, for Steam sync)

    [ObservableProperty]
    private ObservableCollection<AchievementDisplayItem> _unlockedAchievements = new();

    [ObservableProperty]
    private ObservableCollection<AchievementDisplayItem> _lockedAchievements = new();

    [ObservableProperty]
    private AchievementDisplayItem? _selectedAchievement;

    [ObservableProperty]
    private bool _showLocked = true;

    public bool HasUnlockedAchievements => UnlockedAchievements.Count > 0;
    public bool HasLockedAchievements => LockedAchievements.Count > 0;
    public bool HasNoAchievements => !HasUnlockedAchievements && !HasLockedAchievements;

    public int TotalAchievements => UnlockedAchievements.Count + LockedAchievements.Count;
    public int UnlockedCount => UnlockedAchievements.Count;
    public string CompletionText => TotalAchievements > 0
        ? $"{UnlockedCount}/{TotalAchievements} ({(UnlockedCount * 100.0 / TotalAchievements):F0}%)"
        : "0/0";

    public AchievementViewModel(SagaInteractionContext context)
    {
        _context = context;
        RefreshAchievements();
    }

    /// <summary>
    /// Sets the repository and Steam service for persistence (optional, called from MainViewModel).
    /// </summary>
    public void SetPersistence(IWorldStateRepository? worldRepository, ISteamAchievementService? steamService)
    {
        _worldRepository = worldRepository;
        _steamService = steamService;
    }

    /// <summary>
    /// Refreshes the achievement lists from the avatar and global achievement catalog.
    /// Call this when achievement progress changes or when switching avatars.
    /// </summary>
    public void RefreshAchievements()
    {
        UnlockedAchievements.Clear();
        LockedAchievements.Clear();

        if (_context.World?.Gameplay?.Achievements == null || _context.AvatarEntity == null)
        {
            OnPropertyChanged(nameof(HasUnlockedAchievements));
            OnPropertyChanged(nameof(HasLockedAchievements));
            OnPropertyChanged(nameof(HasNoAchievements));
            OnPropertyChanged(nameof(TotalAchievements));
            OnPropertyChanged(nameof(UnlockedCount));
            OnPropertyChanged(nameof(CompletionText));
            return;
        }

        // Build lookup of unlocked achievements
        var unlockedLookup = new Dictionary<string, AchievementEntry>(StringComparer.OrdinalIgnoreCase);
        if (_context.AvatarEntity.Achievements != null)
        {
            foreach (var entry in _context.AvatarEntity.Achievements)
            {
                unlockedLookup[entry.AchievementRef] = entry;
            }
        }

        // Process all achievements from catalog
        foreach (var achievement in _context.World.Gameplay.Achievements)
        {
            var isUnlocked = unlockedLookup.TryGetValue(achievement.RefName, out var entry);
            var currentValue = GetCurrentStatValue(achievement.Criteria.Type);
            var progressPercentage = achievement.Criteria.Threshold > 0
                ? Math.Min(100f, (currentValue / achievement.Criteria.Threshold) * 100f)
                : 0f;

            var displayItem = new AchievementDisplayItem
            {
                RefName = achievement.RefName,
                DisplayName = achievement.DisplayName,
                Description = achievement.Description,
                IsUnlocked = isUnlocked,
                ProgressPercentage = isUnlocked ? 100f : progressPercentage,
                UnlockedDate = entry?.UnlockedDate,
                CriteriaType = achievement.Criteria.Type,
                Threshold = achievement.Criteria.Threshold,
                CurrentValue = currentValue
            };

            if (isUnlocked)
                UnlockedAchievements.Add(displayItem);
            else
                LockedAchievements.Add(displayItem);
        }

        OnPropertyChanged(nameof(HasUnlockedAchievements));
        OnPropertyChanged(nameof(HasLockedAchievements));
        OnPropertyChanged(nameof(HasNoAchievements));
        OnPropertyChanged(nameof(TotalAchievements));
        OnPropertyChanged(nameof(UnlockedCount));
        OnPropertyChanged(nameof(CompletionText));
    }

    /// <summary>
    /// Gets the current stat value from the avatar for achievement criteria checking.
    /// </summary>
    private float GetCurrentStatValue(AchievementCriteriaType criteriaType)
    {
        if (_context.AvatarEntity == null)
            return 0f;

        return criteriaType switch
        {
            AchievementCriteriaType.BlocksPlaced => _context.AvatarEntity.BlocksPlaced,
            AchievementCriteriaType.BlocksDestroyed => _context.AvatarEntity.BlocksDestroyed,
            AchievementCriteriaType.DistanceTraveled => _context.AvatarEntity.DistanceTraveled,
            AchievementCriteriaType.PlayTimeHours => _context.AvatarEntity.PlayTimeHours,
            _ => 0f
        };
    }

    /// <summary>
    /// Checks if achievement criteria is met and unlocks it if so.
    /// Returns true if achievement was newly unlocked, false otherwise.
    /// NOTE: In production, this will be replaced by Steam API calls.
    /// </summary>
    public bool CheckAndUnlockAchievement(string achievementRef)
    {
        if (_context.AvatarEntity?.Achievements == null || _context.World == null)
            return false;

        var achievement = _context.World.TryGetAchievementByRefName(achievementRef);
        if (achievement == null)
            return false;

        // Check if already unlocked
        var existingEntry = _context.AvatarEntity.Achievements?.FirstOrDefault(a =>
            a.AchievementRef.Equals(achievementRef, StringComparison.OrdinalIgnoreCase));

        if (existingEntry != null)
            return false; // Already unlocked

        // Check if criteria is met
        var currentValue = GetCurrentStatValue(achievement.Criteria.Type);
        if (currentValue < achievement.Criteria.Threshold)
            return false; // Criteria not met

        // Unlock achievement
        var achievementList = _context.AvatarEntity.Achievements?.ToList() ?? new List<AchievementEntry>();
        achievementList.Add(new AchievementEntry
        {
            AchievementRef = achievementRef,
            UnlockedDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            ProgressPercentage = 100f
        });
        _context.AvatarEntity.Achievements = achievementList.ToArray();

        RefreshAchievements();
        return true;
    }

    /// <summary>
    /// Checks all achievements and unlocks any whose criteria are met.
    /// Returns list of newly unlocked achievement RefNames.
    /// NOTE: In production, this will be replaced by Steam API calls.
    /// </summary>
    public List<string> CheckAllAchievements()
    {
        var newlyUnlocked = new List<string>();

        if (_context.World?.Gameplay?.Achievements == null)
            return newlyUnlocked;

        foreach (var achievement in _context.World.Gameplay.Achievements)
        {
            if (CheckAndUnlockAchievement(achievement.RefName))
            {
                newlyUnlocked.Add(achievement.RefName);
            }
        }

        return newlyUnlocked;
    }

    /// <summary>
    /// Manually unlocks an achievement (for testing or admin purposes).
    /// Persists to LiteDB and syncs to Steam if available.
    /// </summary>
    public async Task<bool> UnlockAchievementAsync(string achievementRef)
    {
        if (_context.AvatarEntity?.Achievements == null || _context.World == null)
            return false;

        var achievement = _context.World.TryGetAchievementByRefName(achievementRef);
        if (achievement == null)
            return false;

        // Check if already unlocked
        var existingEntry = _context.AvatarEntity.Achievements?.FirstOrDefault(a =>
            a.AchievementRef.Equals(achievementRef, StringComparison.OrdinalIgnoreCase));

        if (existingEntry != null)
            return false; // Already unlocked

        // Unlock achievement in memory (Avatar XML)
        var achievementList = _context.AvatarEntity.Achievements?.ToList() ?? new List<AchievementEntry>();
        achievementList.Add(new AchievementEntry
        {
            AchievementRef = achievementRef,
            UnlockedDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            ProgressPercentage = 100f
        });
        _context.AvatarEntity.Achievements = achievementList.ToArray();

        // Persist to LiteDB if repository is available
        if (_worldRepository != null)
        {
            try
            {
                var avatarId = _context.AvatarEntity.AvatarId.ToString();
                var instances = await _worldRepository.GetOrCreateAchievementInstancesAsync(avatarId);

                // Find or create instance for this achievement
                var instance = instances.FirstOrDefault(i => i.TemplateRef == achievementRef);

                if (instance != null)
                {
                    // Update instance
                    instance.IsUnlocked = true;
                    instance.UnlockedAt = DateTime.UtcNow;
                    instance.CurrentProgress = (int)(achievement.Criteria?.Threshold ?? 1f);

                    await _worldRepository.SaveAchievementAsync(instance);
                }
            }
            catch
            {
                // Silent fail if persistence not available
            }
        }

        // Sync to Steam if service is available
        // achievement.RefName IS the Steam API achievement ID (per definition)
        if (_steamService != null)
        {
            try
            {
                var avatarId = _context.AvatarEntity.AvatarId.ToString();
                _steamService.UnlockAchievement(achievement.RefName, avatarId, achievementRef);
            }
            catch
            {
                // Silent fail if Steam not available
            }
        }

        RefreshAchievements();
        return true;
    }

    partial void OnShowLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(HasLockedAchievements));
    }
}
