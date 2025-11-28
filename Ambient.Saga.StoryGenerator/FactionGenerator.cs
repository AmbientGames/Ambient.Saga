using Ambient.Domain;
using Ambient.Saga.StoryGenerator.Models;
using System.Text;
using System.Xml;

namespace Ambient.Saga.StoryGenerator;

/// <summary>
/// Generates faction definitions with reputation tiers and rewards
/// </summary>
public class FactionGenerator
{
    private readonly RefNameGenerator _refNameGenerator;
    private ThemeAwareContentGenerator? _contentGenerator;

    public FactionGenerator(RefNameGenerator refNameGenerator)
    {
        _refNameGenerator = refNameGenerator;
    }

    /// <summary>
    /// Generates factions based on narrative structure with theme-aware names
    /// </summary>
    public List<FactionDefinition> GenerateFactions(NarrativeStructure narrative, string worldRefName, ThemeContent? theme = null)
    {
        // Create theme-aware content generator
        _contentGenerator = new ThemeAwareContentGenerator(theme);

        var factions = new List<FactionDefinition>();

        // Generate core factions based on story threads
        var threadCount = 0;
        foreach (var thread in narrative.StoryThreads.Take(3)) // Top 3 story threads
        {
            // Create faction for major story thread
            var faction = CreateFactionFromThread(thread, threadCount, worldRefName);
            factions.Add(faction);
            threadCount++;
        }

        // Always generate core civilization factions with theme-aware names
        factions.Add(CreateCityFaction(worldRefName));
        factions.Add(CreateMerchantsGuildFaction(worldRefName));
        factions.Add(CreateExplorersFaction(worldRefName));

        // Generate opposing factions
        factions.Add(CreateBanditFaction(worldRefName));
        factions.Add(CreateWildernessFaction(worldRefName));

        return factions;
    }

    private FactionDefinition CreateFactionFromThread(StoryThread thread, int threadIndex, string worldRefName)
    {
        var refName = GenerateRefName($"FACTION_{thread.RefName}");
        var category = DetermineThreadCategory(threadIndex);

        return new FactionDefinition
        {
            RefName = refName,
            DisplayName = $"[AI: Faction for {thread.RefName}]",
            Description = $"[AI: Faction description for story thread {thread.RefName}]",
            StartingReputation = 0,
            Category = category,
            Relationships = GenerateRelationships(refName, category),
            ReputationRewards = GenerateReputationRewards(refName)
        };
    }

    private FactionDefinition CreateCityFaction(string worldRefName)
    {
        var displayName = _contentGenerator?.GenerateFactionName("City") ?? "City Watch";
        var description = _contentGenerator?.GenerateFactionDescription("City", displayName) ?? "Defenders of civilization and order";

        return new FactionDefinition
        {
            RefName = GenerateRefName("CITY_GUARDS"),
            DisplayName = displayName,
            Description = description,
            StartingReputation = 0,
            Category = "Military",
            Relationships = new List<FactionRelationship>
            {
                new() { FactionRef = "MERCHANTS_GUILD", RelationshipType = "Allied", SpilloverPercent = 0.25f },
                new() { FactionRef = "BANDITS", RelationshipType = "Enemy", SpilloverPercent = 0.50f }
            },
            ReputationRewards = GenerateReputationRewards("CITY_GUARDS")
        };
    }

    private FactionDefinition CreateMerchantsGuildFaction(string worldRefName)
    {
        var displayName = _contentGenerator?.GenerateFactionName("Merchant") ?? "Merchants' Guild";
        var description = _contentGenerator?.GenerateFactionDescription("Merchant", displayName) ?? "Trading consortium controlling commerce";

        return new FactionDefinition
        {
            RefName = GenerateRefName("MERCHANTS_GUILD"),
            DisplayName = displayName,
            Description = description,
            StartingReputation = 0,
            Category = "Merchant",
            Relationships = new List<FactionRelationship>
            {
                new() { FactionRef = "CITY_GUARDS", RelationshipType = "Allied", SpilloverPercent = 0.15f },
                new() { FactionRef = "BANDITS", RelationshipType = "Enemy", SpilloverPercent = 0.30f }
            },
            ReputationRewards = GenerateMerchantRewards()
        };
    }

    private FactionDefinition CreateExplorersFaction(string worldRefName)
    {
        return new FactionDefinition
        {
            RefName = GenerateRefName("EXPLORERS_SOCIETY"),
            DisplayName = "[AI: Explorers Society Name]",
            Description = "[AI: Adventurers and cartographers seeking discovery]",
            StartingReputation = 0,
            Category = "Guild",
            Relationships = new List<FactionRelationship>
            {
                new() { FactionRef = "WILDERNESS_WARDENS", RelationshipType = "Allied", SpilloverPercent = 0.20f }
            },
            ReputationRewards = GenerateExplorerRewards()
        };
    }

    private FactionDefinition CreateBanditFaction(string worldRefName)
    {
        return new FactionDefinition
        {
            RefName = GenerateRefName("BANDITS"),
            DisplayName = "[AI: Bandit Faction Name]",
            Description = "[AI: Outlaws and raiders threatening civilization]",
            StartingReputation = -3000,
            Category = "Criminal",
            Relationships = new List<FactionRelationship>
            {
                new() { FactionRef = "CITY_GUARDS", RelationshipType = "Enemy", SpilloverPercent = 0.50f },
                new() { FactionRef = "MERCHANTS_GUILD", RelationshipType = "Enemy", SpilloverPercent = 0.30f }
            },
            ReputationRewards = GenerateReputationRewards("BANDITS")
        };
    }

    private FactionDefinition CreateWildernessFaction(string worldRefName)
    {
        return new FactionDefinition
        {
            RefName = GenerateRefName("WILDERNESS_WARDENS"),
            DisplayName = "[AI: Wilderness Wardens Name]",
            Description = "[AI: Guardians of nature and ancient forests]",
            StartingReputation = 0,
            Category = "Nature",
            Relationships = new List<FactionRelationship>
            {
                new() { FactionRef = "EXPLORERS_SOCIETY", RelationshipType = "Allied", SpilloverPercent = 0.20f }
            },
            ReputationRewards = GenerateNatureRewards()
        };
    }

    private string GenerateRefName(string baseName)
    {
        // Simple RefName generation - just ensure uppercase and no spaces
        return baseName.ToUpper().Replace(" ", "_").Replace("-", "_");
    }

    private string DetermineThreadCategory(int threadIndex)
    {
        // Distribute categories across story threads
        return threadIndex switch
        {
            0 => "City",
            1 => "Guild",
            2 => "Religious",
            _ => "Other"
        };
    }

    private string DetermineCategory(string theme)
    {
        return theme.ToLower() switch
        {
            var t when t.Contains("militar") || t.Contains("war") || t.Contains("guard") => "Military",
            var t when t.Contains("trade") || t.Contains("merchant") || t.Contains("commerce") => "Merchant",
            var t when t.Contains("relig") || t.Contains("temple") || t.Contains("faith") => "Religious",
            var t when t.Contains("bandit") || t.Contains("criminal") || t.Contains("outlaw") => "Criminal",
            var t when t.Contains("nature") || t.Contains("forest") || t.Contains("wild") => "Nature",
            var t when t.Contains("guild") || t.Contains("craft") || t.Contains("explorer") => "Guild",
            _ => "Other"
        };
    }

    private List<FactionRelationship> GenerateRelationships(string factionRef, string category)
    {
        var relationships = new List<FactionRelationship>();

        // Generate relationships based on category
        if (category == "Military")
        {
            relationships.Add(new FactionRelationship
            {
                FactionRef = "BANDITS",
                RelationshipType = "Enemy",
                SpilloverPercent = 0.40f
            });
        }

        return relationships;
    }

    private List<ReputationReward> GenerateReputationRewards(string factionRef)
    {
        return new List<ReputationReward>
        {
            new()
            {
                RequiredLevel = "Friendly",
                Items = new List<RewardItem>
                {
                    new() { RefName = "[AI: Basic faction item]", Type = "Equipment", DiscountPercent = 0.10f }
                }
            },
            new()
            {
                RequiredLevel = "Honored",
                Items = new List<RewardItem>
                {
                    new() { RefName = "[AI: Rare faction item]", Type = "Equipment", DiscountPercent = 0.20f }
                }
            },
            new()
            {
                RequiredLevel = "Revered",
                Items = new List<RewardItem>
                {
                    new() { RefName = "[AI: Epic faction item]", Type = "Equipment", DiscountPercent = 0.30f }
                }
            },
            new()
            {
                RequiredLevel = "Exalted",
                Items = new List<RewardItem>
                {
                    new() { RefName = "[AI: Legendary faction item]", Type = "Equipment", DiscountPercent = 0.50f }
                }
            }
        };
    }

    private List<ReputationReward> GenerateMerchantRewards()
    {
        return new List<ReputationReward>
        {
            new()
            {
                RequiredLevel = "Friendly",
                Items = new List<RewardItem>
                {
                    new() { RefName = "[AI: Merchant discount items]", Type = "Consumable", DiscountPercent = 0.15f }
                }
            },
            new()
            {
                RequiredLevel = "Honored",
                Items = new List<RewardItem>
                {
                    new() { RefName = "[AI: Rare trade goods]", Type = "Equipment", DiscountPercent = 0.25f }
                }
            },
            new()
            {
                RequiredLevel = "Exalted",
                Items = new List<RewardItem>
                {
                    new() { RefName = "[AI: Exclusive merchant items]", Type = "Equipment", DiscountPercent = 0.60f }
                }
            }
        };
    }

    private List<ReputationReward> GenerateExplorerRewards()
    {
        return new List<ReputationReward>
        {
            new()
            {
                RequiredLevel = "Friendly",
                Items = new List<RewardItem>
                {
                    new() { RefName = "[AI: Explorer's map or compass]", Type = "Equipment" }
                }
            },
            new()
            {
                RequiredLevel = "Honored",
                Items = new List<RewardItem>
                {
                    new() { RefName = "[AI: Advanced navigation tools]", Type = "Equipment" }
                }
            }
        };
    }

    private List<ReputationReward> GenerateNatureRewards()
    {
        return new List<ReputationReward>
        {
            new()
            {
                RequiredLevel = "Friendly",
                Items = new List<RewardItem>
                {
                    new() { RefName = "[AI: Nature-based consumables]", Type = "Consumable" }
                }
            },
            new()
            {
                RequiredLevel = "Revered",
                Items = new List<RewardItem>
                {
                    new() { RefName = "[AI: Druidic equipment]", Type = "Equipment" }
                }
            }
        };
    }
}

/// <summary>
/// Intermediate faction definition for generation
/// </summary>
public class FactionDefinition
{
    public string RefName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int StartingReputation { get; set; }
    public string Category { get; set; } = string.Empty;
    public List<FactionRelationship> Relationships { get; set; } = new();
    public List<ReputationReward> ReputationRewards { get; set; } = new();
}

public class FactionRelationship
{
    public string FactionRef { get; set; } = string.Empty;
    public string RelationshipType { get; set; } = string.Empty; // Allied, Enemy, Rival
    public float SpilloverPercent { get; set; }
}

public class ReputationReward
{
    public string RequiredLevel { get; set; } = string.Empty;
    public List<RewardItem> Items { get; set; } = new();
}

public class RewardItem
{
    public string RefName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // Equipment, Consumable, QuestToken
    public float DiscountPercent { get; set; }
    public int Quantity { get; set; } = 1;
}
